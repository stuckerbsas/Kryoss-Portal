# Security Audit -- Kryoss API & Infrastructure

## Date: 2026-04-13
## Auditor: Automated Security Review (Claude)
## Scope: `KryossApi/src/KryossApi/` -- all middleware, services, functions, data layer, and infrastructure code

---

## CRITICAL FINDINGS

### CRIT-01: M365 Client Secrets Stored in Plaintext in Database

**File:** `Data/Entities/M365Tenant.cs`, `Functions/Portal/M365Function.cs`
**Severity:** CRITICAL
**CVSS Estimate:** 9.1

The `M365Tenant` entity stores `ClientSecret` as a plain `string?` column in Azure SQL. The `M365Function.Connect` endpoint receives the client secret in the request body and writes it directly to the database. The code itself contains a TODO comment acknowledging this: `// TODO: encrypt with Key Vault`.

**Impact:** If the database is compromised (SQL injection, backup leak, insider threat, or Azure SQL misconfiguration), an attacker obtains Azure AD app registration credentials for every connected customer tenant. These credentials grant read access to Conditional Access policies, user directories, mail rules, and admin role membership across all connected M365 tenants.

**Remediation:** Encrypt the client secret using the existing Key Vault infrastructure before persisting. On read, decrypt from Key Vault. Alternatively, store only the Key Vault secret reference in the database (same pattern as `CryptoService` does for RSA private keys). This is the single most impactful fix in this report.

---

### CRIT-02: External Scan Endpoint -- No Target Validation (SSRF Risk)

**File:** `Services/ExternalScanService.cs`, `Functions/Portal/ExternalScanFunction.cs`
**Severity:** CRITICAL
**CVSS Estimate:** 8.6

The `POST /v2/external-scan` endpoint accepts an arbitrary `target` string and the `ExternalScanService` resolves it via DNS, then opens TCP connections to 25 ports on every resolved IP. There is zero validation of the target:

- Internal/private IP ranges (10.x, 172.16-31.x, 192.168.x, 127.x, 169.254.x, Azure metadata 169.254.169.254) are not blocked.
- Azure internal endpoints (e.g., `169.254.169.254` for IMDS, `168.63.129.16` for wireserver) are reachable.
- Cloud resource private endpoints (Azure SQL, Key Vault, Storage) could be probed.
- The banner grabbing sends HTTP HEAD requests and reads responses, potentially leaking internal service banners.

**Impact:** An authenticated portal user can use the Azure Function App as a port-scanning proxy against Azure internal infrastructure, private VNets, or any third-party target. The Azure IMDS endpoint at `169.254.169.254` could expose managed identity tokens. This is a textbook Server-Side Request Forgery (SSRF) vulnerability.

**Remediation:**
1. Validate that resolved IPs are not in RFC 1918, RFC 6598, loopback, link-local, or cloud metadata ranges.
2. Implement rate limiting per organization (e.g., max 1 scan per 5 minutes).
3. Require the target to be a domain the organization owns (validate against org record or DNS TXT verification).
4. Block the Azure IMDS endpoint explicitly.

---

### CRIT-03: HMAC Signature Not Mandatory -- Agent Can Submit Plaintext Without HMAC

**File:** `Middleware/ApiKeyAuthMiddleware.cs` (lines 82-111)
**Severity:** CRITICAL
**CVSS Estimate:** 8.1

The HMAC validation in `ApiKeyAuthMiddleware` is conditional: it only runs if BOTH `org.ApiSecret` is non-empty AND the `X-Signature` header is present. If an attacker has only the API key (without the secret), they can omit the `X-Signature` and `X-Timestamp` headers entirely, and the middleware will pass the request through to downstream functions without any HMAC or replay validation.

```csharp
if (!string.IsNullOrEmpty(org.ApiSecret) && !string.IsNullOrEmpty(signature))
{
    // HMAC validation only happens here
}
// If signature is missing, request proceeds without HMAC check
```

**Impact:** If the API key is leaked (which is lower entropy than the API secret), an attacker can impersonate any agent in that organization, submit fabricated assessment results, or download control definitions -- all without needing the HMAC secret or passing replay detection.

**Remediation:** When `org.ApiSecret` is non-empty, the `X-Signature` header MUST be required. Return 401 if it is missing. The current logic should be:
```csharp
if (!string.IsNullOrEmpty(org.ApiSecret))
{
    if (string.IsNullOrEmpty(signature))
    {
        // REJECT: signature required but not provided
        return 401;
    }
    // validate HMAC...
}
```

---

### CRIT-04: Plaintext Payload Fallback in ResultsFunction

**File:** `Functions/Agent/ResultsFunction.cs` (lines 92-149)
**Severity:** CRITICAL
**CVSS Estimate:** 7.8

The `/v1/results` endpoint accepts both encrypted envelopes (`application/kryoss-envelope+json`) and plaintext JSON. The plaintext path only logs a warning but processes the payload normally. Combined with CRIT-03 (HMAC not enforced), an attacker with just the API key can submit completely unencrypted, unsigned fabricated assessment results.

**Impact:** Integrity of all assessment data is compromised. A malicious actor could submit perfect scores for a compromised machine, undermining the entire compliance posture of the platform.

**Remediation:** Add a hard cutoff date for the plaintext fallback. After the rollout window, reject plaintext payloads with 400. Better: make it a per-org configuration flag that defaults to "require encryption" for new enrollments.

---

## HIGH FINDINGS

### HIGH-01: IDOR on Multiple Portal Endpoints -- No Org Ownership Verification

**Files:** `Functions/Portal/MachinesFunction.cs`, `Functions/Portal/PortsFunction.cs`, `Functions/Portal/ExternalScanFunction.cs`, `Functions/Portal/ReportsFunction.cs`
**Severity:** HIGH
**CVSS Estimate:** 7.5

Several portal endpoints accept resource IDs (machine GUID, run GUID, scan GUID) without verifying that the resource belongs to the authenticated user's organization or franchise. RLS via `SESSION_CONTEXT` provides server-side filtering on LIST operations, but DETAIL endpoints bypass this:

1. **`GET /v2/machines/{id}`** -- Queries by `m.Id` only. No org filter. Any authenticated user with `machines:read` can access any machine's full detail (hostname, IP, MAC, serial number, domain, OS, hardware specs, assessment history).

2. **`GET /v2/machines/{machineId}/runs/{runId}`** -- No org filter. Any user can access any run's full results.

3. **`GET /v2/machines/{machineId}/software`** -- No org filter. Any user can access any machine's software inventory.

4. **`GET /v2/ports?machineId={guid}`** -- No org filter. Any user can access any machine's port scan results.

5. **`GET /v2/reports/{runId}`** -- No org filter. Any user with `reports:read` can generate a report for any assessment run, exposing the full security posture of another organization's machine.

6. **`GET /v2/external-scan/{scanId}`** -- No org filter. Any user can access any external scan's results.

7. **`GET /v2/hygiene?organizationId={any-guid}`** -- Accepts arbitrary orgId parameter. Any user can read any org's AD hygiene findings.

**Impact:** Complete cross-tenant data exposure. A user from Organization A can enumerate and read all security assessment data, hardware inventory, port scans, AD hygiene findings, and reports belonging to Organization B. For a security SaaS serving MSPs with multiple client organizations, this destroys multi-tenancy.

**Remediation:** Every detail endpoint must verify that the requested resource belongs to the authenticated user's franchise/organization scope. Add a helper method like `EnsureOrgAccess(resourceOrgId)` that checks against `_user.FranchiseId` or `_user.OrganizationId`.

---

### HIGH-02: Bearer Auth Trusts X-MS-CLIENT-PRINCIPAL Header Without Cryptographic Verification

**File:** `Middleware/BearerAuthMiddleware.cs`
**Severity:** HIGH
**CVSS Estimate:** 7.5

The `BearerAuthMiddleware` reads the `X-MS-CLIENT-PRINCIPAL` header, base64-decodes it, and trusts the claims within it as the authenticated identity. The CLAUDE.md documents that Easy Auth is configured with `requireAuthentication=false` and `unauthenticatedClientAction=AllowAnonymous`.

This means Azure Easy Auth is NOT blocking unauthenticated requests. The middleware relies entirely on the `X-MS-CLIENT-PRINCIPAL` header being present, but since Easy Auth is in pass-through mode, there is nothing preventing a direct caller from crafting a fake `X-MS-CLIENT-PRINCIPAL` header with arbitrary claims (any OID, any email, any role).

**Impact:** Complete authentication bypass for the portal API. An attacker can impersonate any user, including super_admin, by constructing a base64-encoded JSON blob with the desired OID and injecting it as the `X-MS-CLIENT-PRINCIPAL` header.

**Mitigation note:** If the Function App is behind Azure Static Web Apps (SWA) proxy and the SWA is the only ingress path, then SWA strips/replaces the `X-MS-CLIENT-PRINCIPAL` header before forwarding. However, if the Function App URL (`func-kryoss.azurewebsites.net`) is directly accessible (not firewalled), this is exploitable.

**Remediation:**
1. **Immediate:** Restrict the Function App to only accept traffic from the SWA backend (Azure networking / IP restrictions).
2. **Better:** Validate the JWT token directly using `Microsoft.Identity.Web` or manual OIDC validation, instead of trusting the Easy Auth header. The `Authorization: Bearer <token>` header should be validated cryptographically.
3. **Alternative:** Set `requireAuthentication=true` in Easy Auth, which prevents unauthenticated requests from reaching the Function.

---

### HIGH-03: Bootstrap Super Admin Auto-Provisioning Race Condition

**File:** `Middleware/BearerAuthMiddleware.cs` (lines 106-161)
**Severity:** HIGH
**CVSS Estimate:** 7.2

When the users table is empty (`totalUsers == 0`), the first authenticated caller is automatically provisioned as `super_admin`. Combined with HIGH-02, if the Function App is directly accessible:

1. An attacker crafts a fake `X-MS-CLIENT-PRINCIPAL` header before legitimate setup.
2. The attacker becomes `super_admin` on the platform.
3. All subsequent legitimate users are denied access ("User not registered in Kryoss").

Even without HIGH-02, there is a TOCTOU race: two simultaneous first requests could both see `totalUsers == 0` and both attempt to create super_admin users (though EF Core unique constraints may prevent duplicate OID insertion).

**Remediation:** Remove the auto-provisioning. Use a seed script or CLI tool to create the initial super_admin user. If auto-provisioning is kept, restrict it to a specific Entra tenant ID configured in environment variables.

---

### HIGH-04: Nonce Cache Provides No Protection on Multi-Instance Deployments

**File:** `Services/NonceCache.cs`
**Severity:** HIGH
**CVSS Estimate:** 6.5

The `NonceCache` is an in-process `ConcurrentDictionary`. On Azure Functions Consumption plan, if the app scales to multiple instances, each instance has its own independent nonce cache. An attacker can replay a valid HMAC-signed request against a different instance within the 5-minute timestamp window and it will be accepted.

The code comments acknowledge this limitation but dismiss the risk as "low because 1-3 instances." However:
- Azure Functions can scale to 200 instances under load.
- A Function App restart clears the entire cache, opening a replay window for all recent signatures.
- The combination with CRIT-03 means this is only relevant when HMAC is actually validated.

**Remediation:** Implement the Redis-backed nonce cache mentioned in the backlog. Azure Cache for Redis is a natural fit for Azure Functions.

---

### HIGH-05: No Rate Limiting on Any Endpoint

**Files:** All function files, `Program.cs`
**Severity:** HIGH
**CVSS Estimate:** 6.3

There is no rate limiting on any API endpoint. Key attack vectors:

1. **Enrollment code brute-force:** Codes are 19 characters (4 groups of 4 alphanumeric, separated by dashes). The charset is 28 characters (A-Z minus O,I plus 2-9), giving ~614k combinations for 4-char groups. However, codes are composed of multiple groups, making brute-force impractical for random codes. BUT: the endpoint returns distinct error messages (`410 Gone`) that confirm code existence, which could be used for enumeration with known code prefixes.

2. **External scan abuse:** No limit on how many scans a user can trigger, enabling abuse of the server as a port-scanning proxy (amplifies CRIT-02).

3. **M365 scan abuse:** No limit on M365 scans, which make many Graph API calls per scan (could trigger Microsoft throttling on the customer's app registration).

4. **DoS on assessment processing:** The `/v1/results` endpoint processes potentially large payloads (175 KB per run per the docs) with database-heavy evaluation. No payload size limit is enforced.

**Remediation:** Implement rate limiting at minimum on: enrollment (`/v1/enroll`), external scan, M365 scan, and results submission. Azure Functions supports Azure API Management or custom middleware-based rate limiting.

---

### HIGH-06: No Payload Size Limits on Agent Submission Endpoints

**File:** `Functions/Agent/ResultsFunction.cs`, `Functions/Portal/HygieneFunction.cs`, `Functions/Portal/PortsFunction.cs`
**Severity:** HIGH
**CVSS Estimate:** 6.0

No request body size validation on any agent submission endpoint. An attacker (or malfunctioning agent) could submit:
- A multi-gigabyte JSON payload to `/v1/results`, consuming Function App memory.
- Thousands of hygiene findings to `/v1/hygiene`, inserting unbounded rows.
- Thousands of port entries to `/v1/ports`, inserting unbounded rows.

The `EvaluationService.EvaluateAsync` loads all results into memory and writes them all to the database in a single `SaveChangesAsync` call.

**Remediation:** Enforce maximum body size in middleware or early in each function. Cap hygiene findings and port entries at reasonable maximums (e.g., 10,000 and 200 respectively).

---

## MEDIUM FINDINGS

### MED-01: No CORS Configuration

**Files:** `Program.cs`
**Severity:** MEDIUM

No CORS headers or CORS middleware is configured. When the Function App is accessed directly (bypassing SWA), the browser will block cross-origin requests from the portal. However, the absence of CORS means:
- If any CORS is configured at the Azure level (host.json or platform settings), it may be overly permissive.
- Without explicit restrictive CORS, a misconfiguration at the platform level could allow any origin to make authenticated requests.

**Remediation:** Explicitly configure CORS in the Function App to allow only the SWA origin (`zealous-dune-0ac672d10.6.azurestaticapps.net`).

---

### MED-02: Enrollment Code Entropy -- Modulo Bias in Generation

**File:** `Services/EnrollmentService.cs` (lines 201-214)
**Severity:** MEDIUM

The `GenerateRandomCode` method uses `bytes[i] % chars.Length` where `chars.Length = 28`. Since `256 % 28 = 4`, character indices 0-3 have a slightly higher probability than indices 4-27. The bias is approximately 1.6% per character, which is low but non-zero for a security-critical token.

Additionally, the code inserts dashes at fixed positions (`i % 5 == 4`), consuming character slots for formatting. The effective key space for a 19-character code is 15 random characters from a 28-char alphabet, giving approximately `28^15 = 1.7 * 10^21` combinations -- adequate for brute-force resistance but the bias reduces effective entropy slightly.

**Remediation:** Use rejection sampling instead of modulo: reject bytes >= `256 - (256 % chars.Length)` and re-sample.

---

### MED-03: HMAC Debug Logging Leaks Partial Signatures

**File:** `Middleware/ApiKeyAuthMiddleware.cs` (lines 188-193)
**Severity:** MEDIUM

On HMAC validation failure, the middleware logs the first 16 characters of both the expected and received signatures:
```csharp
logger.LogWarning("  Expected:   {Exp}", expectedSig[..16] + "...");
logger.LogWarning("  Got:        {Got}", signature.ToLowerInvariant()[..Math.Min(16, signature.Length)] + "...");
```

While helpful for debugging, this leaks partial HMAC values to Application Insights / log analytics. If logs are compromised, this partial information could help narrow a brute-force attack on the HMAC secret.

**Remediation:** Remove the expected signature from the log. Only log the fact of mismatch plus the request metadata (method, path, body length).

---

### MED-04: Agent Binary Template Accessible via Storage Connection String

**File:** `Functions/Portal/AgentDownloadFunction.cs`
**Severity:** MEDIUM

The agent download endpoint uses `AzureWebJobsStorage` connection string to access blob storage. This is the same storage account used by Azure Functions for its internal state (triggers, logs). The agent template binary is stored alongside operational data.

If the `AzureWebJobsStorage` connection is compromised (it is typically a full-access connection string), an attacker could:
1. Replace the agent template binary with a trojaned version.
2. All subsequent agent downloads would distribute the compromised binary.

**Remediation:** Use a separate storage account with a SAS token scoped to read-only access on the `kryoss-agent-templates` container. Use Managed Identity to access it instead of connection strings.

---

### MED-05: Hardware Fingerprint (HWID) Binding Is Lenient During Rollout

**File:** `Services/HwidVerifier.cs`
**Severity:** MEDIUM

The HWID verifier has three lenient paths:
1. If neither agent nor machine has an HWID, it passes silently.
2. If the agent sends no HWID but the machine has one stored, it returns `MissingHeader` (currently treated as a warning, not a rejection).
3. The HWID is a simple SHA-256 hash of registry values, which can be trivially spoofed from a compromised machine.

**Remediation:** Set a deadline to enforce HWID on all requests (reject `MissingHeader`). Document when the rollout window ends. Consider TPM-based attestation for Phase 2 as noted in the roadmap.

---

### MED-06: ExternalScanFunction Does Not Verify Organization Ownership

**File:** `Functions/Portal/ExternalScanFunction.cs` (line 64)
**Severity:** MEDIUM

The `StartScan` endpoint checks that `orgExists` but does not verify that the authenticated user has access to that organization. A user from Franchise A could trigger scans against organizations belonging to Franchise B.

**Remediation:** Verify that `body.OrganizationId` belongs to the user's franchise scope.

---

### MED-07: M365Function Does Not Verify Organization Ownership

**File:** `Functions/Portal/M365Function.cs`
**Severity:** MEDIUM

Similar to MED-06, the `Connect`, `Scan`, and `Disconnect` endpoints accept `body.OrganizationId` without verifying franchise/user ownership. A user from one franchise can connect M365 tenants to, scan, or disconnect organizations from another franchise.

**Remediation:** Verify org ownership on all M365 operations.

---

### MED-08: API Key and Secret Returned in Enrollment Response

**File:** `Functions/Agent/EnrollFunction.cs` (lines 62-71)
**Severity:** MEDIUM

The enrollment response returns both the `apiKey` and `apiSecret` in the HTTP response body. If this response is intercepted (even over TLS, via compromised proxy, logging middleware, or Application Insights), the attacker gains full agent authentication credentials.

**Remediation:** Consider a more secure key exchange mechanism. At minimum, ensure Application Insights does not log response bodies for this endpoint.

---

## LOW / INFORMATIONAL

### LOW-01: No Request Body Size Limits in Azure Functions Configuration

There is no `host.json` configuration enforcing `maxRequestBodySize`. The default Azure Functions limit is 100 MB, which is excessive for API payloads that should be < 1 MB.

### LOW-02: Agent Download Creates Multi-Use Enrollment Code with 999 Uses

**File:** `Functions/Portal/AgentDownloadFunction.cs` (line 72)

When no suitable enrollment code exists, the download endpoint creates a new one with `maxUses=999` and 30-day expiry. This is a very generous default that could be abused if the download URL is shared.

### LOW-03: Soft-Delete Bypass via IgnoreQueryFilters

**File:** `Middleware/ApiKeyAuthMiddleware.cs` (line 69)

The API key lookup uses `.IgnoreQueryFilters()` with an explicit `DeletedAt == null` check. This is correct but worth noting: if the filter logic ever drifts from the query filter definition, soft-deleted orgs could authenticate.

### LOW-04: ErrorSanitizationMiddleware Does Not Cover Non-HTTP Functions

The middleware rethrows exceptions for non-HTTP triggers (timer, queue). While this is intentional for retry behavior, any non-HTTP functions added in the future that process sensitive data could leak exception details to Azure Functions logs.

### LOW-05: No Content Security Policy on Report HTML

Reports are served as `text/html` with no `Content-Security-Policy` header. While `HtmlEncode` is used consistently (via `System.Net.WebUtility.HtmlEncode`), the lack of CSP means any XSS bypass would have no additional defense layer.

### LOW-06: ReportService Properly Encodes All User-Controlled Data

The `HtmlEncode` helper wraps `System.Net.WebUtility.HtmlEncode` and is consistently applied to all dynamic values in report HTML (hostnames, OS names, control names, findings, remediations). No `dangerouslySetInnerHTML` was found in the React portal. This is a positive observation included for completeness.

### LOW-07: Timestamp Skew Window Is 5 Minutes

The HMAC timestamp window (`MaxTimestampSkew`) is 5 minutes. This is reasonable but generous. A tighter window (e.g., 2 minutes) would reduce the replay attack surface.

### LOW-08: Organization API Key and Secret Have No Rotation Mechanism

Once generated during first enrollment, the org's `ApiKey` and `ApiSecret` are never rotated. There is no endpoint to rotate credentials. If credentials are leaked, the only mitigation is manual database update.

---

## POSITIVE OBSERVATIONS

### POS-01: Timing-Safe HMAC Comparison
The HMAC validation uses `CryptographicOperations.FixedTimeEquals`, which prevents timing side-channel attacks. This is correct and well-implemented.

### POS-02: Strong Crypto Implementation
The `CryptoService` implements RSA-OAEP-256 + AES-256-GCM correctly, with algorithm enforcement (rejects non-v1 envelopes), proper key zeroing (`CryptographicOperations.ZeroMemory`), and private key storage in Azure Key Vault.

### POS-03: SQL Injection Prevention
No raw SQL (`FromSqlRaw`, `ExecuteSqlRaw`) is used anywhere in the codebase. All data access goes through EF Core's parameterized queries. The `RlsMiddleware` uses properly parameterized `sp_set_session_context` calls.

### POS-04: SqlConnectionFactory Enforces Managed Identity
The `SqlConnectionFactory` explicitly rejects connection strings containing passwords or User IDs, enforces TLS, and defaults to Active Directory authentication. This is defense-in-depth at the infrastructure level.

### POS-05: Agent Identity Trust from Headers, Not Body
`ResultsFunction` correctly ignores `payload.AgentId` from the body and trusts only the HMAC-signed `X-Agent-Id` header. This prevents cross-tenant data injection via the payload.

### POS-06: Consistent Error Sanitization
`ErrorSanitizationMiddleware` is registered first in the pipeline and converts all unhandled exceptions to a generic `{"error":"internal_error","traceId":"..."}` response. No stack traces reach the wire.

### POS-07: Comprehensive Audit Logging
The `ActlogMiddleware` logs every HTTP request with method, path, response code, duration, and user context. Security-relevant events (failed auth, enrollment) are explicitly logged with severity "SEC".

### POS-08: Proper Soft-Delete Implementation
The `AuditInterceptor` intercepts `EntityState.Deleted` and converts it to a soft-delete (`DeletedAt` timestamp), preventing accidental hard deletes. Global query filters in `KryossDbContext` ensure soft-deleted records are excluded by default.

### POS-09: RBAC Implementation Is Thorough
Every portal endpoint has a `[RequirePermission]` attribute (except `/v2/me` which intentionally allows any authenticated user). The `RbacMiddleware` resolves attributes via reflection and enforces them consistently.

### POS-10: Defense-in-Depth Layering
The middleware pipeline order (Error Sanitization > API Key Auth > Bearer Auth > RBAC > RLS > Actlog) demonstrates good security layering. Each layer is independent and additive.

---

## SUMMARY

### By Severity

| Severity | Count |
|----------|-------|
| CRITICAL | 4 |
| HIGH | 6 |
| MEDIUM | 8 |
| LOW/INFO | 8 |

### Top 5 Priority Fixes

1. **CRIT-02 + CRIT-01:** Fix SSRF in external scan (block private IPs) and encrypt M365 secrets in Key Vault. These are the two most dangerous vulnerabilities that could cause immediate customer harm.

2. **CRIT-03:** Make HMAC signature mandatory when ApiSecret exists. This is a one-line fix that closes a major authentication gap.

3. **HIGH-01:** Add org-ownership checks on all detail endpoints. This is the most pervasive issue (affects 6+ endpoints) and completely breaks multi-tenancy.

4. **HIGH-02:** Either restrict Function App network access to SWA-only, or implement proper JWT validation instead of trusting Easy Auth headers.

5. **HIGH-05 + HIGH-06:** Add rate limiting and payload size limits to prevent DoS and abuse.

### Architecture Assessment

The codebase demonstrates strong security awareness in its foundational design: Managed Identity for SQL, Key Vault for private keys, HMAC for agent auth, RSA+AES-GCM for payload encryption, timing-safe comparisons, error sanitization, audit logging, RBAC, and RLS. The security baseline document shows mature threat modeling.

However, the implementation has gaps where the security intent is not fully realized:
- Authentication is strong at the middleware layer but the bearer auth trusts an unverified header.
- HMAC is implemented but not enforced.
- Multi-tenancy is designed at the RLS level but not enforced at the application level for detail endpoints.
- Sensitive data (M365 secrets) bypasses the existing Key Vault infrastructure.
- The external scan feature introduces SSRF without compensating controls.

The good news: all critical and high findings are fixable with targeted changes. The architecture does not need redesign -- it needs the enforcement gaps closed.
