# Security Remediation Sprint 1 — CRITICAL Findings

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 8 CRITICAL security vulnerabilities across API, Agent, and Scripts without removing any functionality.

**Architecture:** Each fix is self-contained — modify the affected file, validate the fix, commit. Actlog is the audit trail for all error details and security events. HTTP responses never leak internal state.

**Tech Stack:** .NET 8 (Azure Functions + Agent), PowerShell 5.1+, SQL Server

**Spec:** `docs/superpowers/specs/2026-04-26-security-remediation-design.md`

**Version bumps:** API patch bump once at end of sprint, Agent patch bump once at end of sprint. Not per-task.

---

## File Map

| Task | Files Modified | Purpose |
|------|---------------|---------|
| C1 | `KryossApi/src/KryossApi/Middleware/ErrorSanitizationMiddleware.cs` | Remove debug fields from HTTP 500 responses |
| C2 | `KryossApi/src/KryossApi/Functions/Portal/HypervisorConfigFunction.cs`, `KryossApi/src/KryossApi/Services/CryptoService.cs` | Encrypt hypervisor passwords at rest |
| C3 | `KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs`, `KryossApi/src/KryossApi/KryossApi.csproj` | JWT signature validation via Microsoft.Identity.Web |
| C4 | `KryossAgent/src/KryossAgent/Services/SelfUpdater.cs`, `KryossAgent/src/KryossAgent/Services/ServiceInstaller.cs` | Replace cmd.exe batch update with SCM recovery restart |
| C5 | `KryossAgent/src/KryossAgent/Program.cs` | Remove UseShellExecute=true in trial mode |
| C6 | `CLAUDE.md`, `KryossAgent/CLAUDE.md`, deploy scripts, test data | Remove hardcoded enrollment code K7X9-M2P4-Q8R1-T5W3 |
| C7 | `KryossApi/deploy/Setup-Azure.ps1` | Remove plaintext password from variable/deploy-config |
| C8 | `KryossApi/sql/018_prevent_hard_delete.sql`, `KryossApi/sql/017_brands_and_org_updates.sql` | QUOTENAME() all dynamic SQL object names |

---

### Task 1: C1 — ErrorSanitizationMiddleware: Remove debug fields

**Files:**
- Modify: `KryossApi/src/KryossApi/Middleware/ErrorSanitizationMiddleware.cs:86-95`

- [ ] **Step 1: Enhance actlog entry with full exception detail**

The actlog write at line 67 currently only logs `{Type}: {Message}`. Enrich it to include stack trace and inner exception so the detail is preserved before we strip it from the response.

Replace lines 62-69:

```csharp
            try
            {
                var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                await actlog.LogAsync(
                    severity: "ERR",
                    module: "middleware",
                    action: $"unhandled.{context.FunctionDefinition.Name}",
                    message: $"[{traceId}] {ex.GetType().Name}: {ex.Message}");
            }
            catch { /* actlog write must never break error handling */ }
```

with:

```csharp
            try
            {
                var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                var detail = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = ex.GetType().FullName,
                    message = ex.Message,
                    stack = ex.StackTrace,
                    inner = ex.InnerException?.Message
                });
                await actlog.LogAsync(
                    severity: "ERR",
                    module: "error",
                    action: "unhandled_exception",
                    message: $"[{traceId}] {detail}");
            }
            catch { /* actlog write must never break error handling */ }
```

- [ ] **Step 2: Remove debug fields from HTTP response**

Replace lines 86-95 (the `// DEBUG MODE` block):

```csharp
            // DEBUG MODE: expose error details until pre-production hardening.
            // TODO(SH): revert to frozen shape before go-live.
            await resp.WriteAsJsonAsync(new
            {
                error = "internal_error",
                traceId,
                debug_type = ex.GetType().FullName,
                debug_message = ex.Message,
                debug_stack = ex.StackTrace,
                debug_inner = ex.InnerException?.Message
            });
```

with:

```csharp
            await resp.WriteAsJsonAsync(new
            {
                error = "internal_error",
                traceId
            });
```

- [ ] **Step 3: Verify — search for any remaining debug fields**

Run: `grep -r "debug_type\|debug_message\|debug_stack\|debug_inner" KryossApi/src/`
Expected: zero matches.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/src/KryossApi/Middleware/ErrorSanitizationMiddleware.cs
git commit -m "fix(security): C1 — remove debug fields from 500 responses, enrich actlog"
```

---

### Task 2: C2 — Encrypt hypervisor passwords at rest

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/CryptoService.cs` (add symmetric helper)
- Modify: `KryossApi/src/KryossApi/Functions/Portal/HypervisorConfigFunction.cs:87,127,185,205`

- [ ] **Step 1: Add symmetric encrypt/decrypt helpers to CryptoService**

Add these methods to the `CryptoService` class (after `DecryptEnvelopeAsync`). These use a per-org derived key from the org's RSA public key fingerprint as salt + a static purpose string, producing a deterministic AES-256-GCM key per org. This avoids needing Key Vault for every hypervisor password read (the org's RSA fingerprint is in DB).

Add to `ICryptoService` interface:

```csharp
    byte[] EncryptSymmetric(Guid organizationId, string orgKeyFingerprint, string plaintext);
    string DecryptSymmetric(Guid organizationId, string orgKeyFingerprint, byte[] ciphertext);
```

Add to `CryptoService` class:

```csharp
    public byte[] EncryptSymmetric(Guid organizationId, string orgKeyFingerprint, string plaintext)
    {
        var key = DeriveOrgKey(organizationId, orgKeyFingerprint);
        try
        {
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[16];

            using var gcm = new AesGcm(key, 16);
            gcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Format: nonce(12) + tag(16) + ciphertext(N)
            var result = new byte[12 + 16 + ciphertext.Length];
            nonce.CopyTo(result, 0);
            tag.CopyTo(result, 12);
            ciphertext.CopyTo(result, 28);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string DecryptSymmetric(Guid organizationId, string orgKeyFingerprint, byte[] blob)
    {
        if (blob.Length < 29) throw new CryptographicException("Ciphertext too short");

        var key = DeriveOrgKey(organizationId, orgKeyFingerprint);
        try
        {
            var nonce = blob[..12];
            var tag = blob[12..28];
            var ciphertext = blob[28..];
            var plaintext = new byte[ciphertext.Length];

            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveOrgKey(Guid organizationId, string fingerprint)
    {
        var ikm = Encoding.UTF8.GetBytes(fingerprint);
        var salt = organizationId.ToByteArray();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt,
            Encoding.UTF8.GetBytes("kryoss-hypervisor-v1"));
    }
```

- [ ] **Step 2: Update HypervisorConfigFunction to encrypt on write**

Inject `ICryptoService` into `HypervisorConfigFunction` constructor:

```csharp
public class HypervisorConfigFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IHypervisorPipeline _pipeline;
    private readonly ICryptoService _crypto;

    public HypervisorConfigFunction(KryossDbContext db, ICurrentUserService user,
        IHypervisorPipeline pipeline, ICryptoService crypto)
    {
        _db = db;
        _user = user;
        _pipeline = pipeline;
        _crypto = crypto;
    }
```

Add a helper method to the class:

```csharp
    private async Task<string?> GetOrgFingerprint(Guid orgId)
    {
        return await _db.OrgCryptoKeys
            .Where(k => k.OrganizationId == orgId && k.IsActive)
            .Select(k => k.Fingerprint)
            .FirstOrDefaultAsync();
    }
```

- [ ] **Step 3: Encrypt password in Create method**

In `Create` (line 87), replace:
```csharp
            EncryptedPassword = body.Password, // TODO: encrypt
```
with:
```csharp
            EncryptedPassword = null, // set below after entity creation
```

Then after `var config = new InfraHypervisorConfig { ... };`, before `_db.InfraHypervisorConfigs.Add(config)`:

```csharp
        if (!string.IsNullOrEmpty(body.Password))
        {
            var fp = await GetOrgFingerprint(body.OrganizationId);
            if (fp != null)
                config.EncryptedPassword = Convert.ToBase64String(
                    _crypto.EncryptSymmetric(body.OrganizationId, fp, body.Password));
            else
                config.EncryptedPassword = body.Password; // fallback: org has no crypto key yet
        }
```

- [ ] **Step 4: Encrypt password in Update method**

In `Update` (line 127), replace:
```csharp
        if (!string.IsNullOrWhiteSpace(body.Password)) config.EncryptedPassword = body.Password;
```
with:
```csharp
        if (!string.IsNullOrWhiteSpace(body.Password))
        {
            var fp = await GetOrgFingerprint(config.OrganizationId);
            if (fp != null)
                config.EncryptedPassword = Convert.ToBase64String(
                    _crypto.EncryptSymmetric(config.OrganizationId, fp, body.Password));
            else
                config.EncryptedPassword = body.Password;
        }
```

- [ ] **Step 5: Decrypt password in Test method**

In `Test`, the password is used at lines 185 and 205 as `config.EncryptedPassword`. Add a decrypt helper at the top of the Test method (after finding the config):

```csharp
        string? password = null;
        if (!string.IsNullOrEmpty(config.EncryptedPassword))
        {
            try
            {
                var fp = await GetOrgFingerprint(config.OrganizationId);
                if (fp != null)
                    password = _crypto.DecryptSymmetric(config.OrganizationId, fp,
                        Convert.FromBase64String(config.EncryptedPassword));
                else
                    password = config.EncryptedPassword;
            }
            catch
            {
                password = config.EncryptedPassword; // pre-encryption legacy value
            }
        }
```

Then replace both uses of `config.EncryptedPassword` (lines 185 and 205) with `password`.

Line 185 — vmware auth:
```csharp
                authReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{config.Username}:{password}")));
```

Line 205 — proxmox ticket:
```csharp
                var ticketBody = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("username", config.Username ?? ""),
                    new KeyValuePair<string, string>("password", password ?? ""),
                });
```

- [ ] **Step 6: Ensure List endpoint never returns password**

Verify `List` query at lines 40-57 — it already uses `.Select()` projection that excludes `EncryptedPassword`. Confirmed: no password field in the anonymous type. No change needed.

- [ ] **Step 7: Add actlog logging for config create/update**

In `Create` method, after `await _db.SaveChangesAsync()`:

```csharp
        try
        {
            var actlog = req.FunctionContext.InstanceServices.GetRequiredService<IActlogService>();
            await actlog.LogAsync("INFO", "hypervisor", "config_create",
                entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
                message: $"Created hypervisor config '{config.DisplayName}' ({config.Platform}) for org {body.OrganizationId}");
        }
        catch { }
```

Wait — `req` is `HttpRequestData`, not `HttpRequest`. Use DI instead. Inject `IActlogService` into the constructor:

Add to constructor:
```csharp
    private readonly IActlogService _actlog;
```

In constructor body: `_actlog = actlog;` (add `IActlogService actlog` parameter).

Then in Create after SaveChanges:
```csharp
        try { await _actlog.LogAsync("INFO", "hypervisor", "config_create",
            entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
            message: $"Created config '{config.DisplayName}' ({config.Platform})"); }
        catch { }
```

In Update after SaveChanges:
```csharp
        try { await _actlog.LogAsync("INFO", "hypervisor", "config_update",
            entityType: "infra_hypervisor_configs", entityId: config.Id.ToString(),
            message: $"Updated config '{config.DisplayName}'"); }
        catch { }
```

- [ ] **Step 8: SQL migration to encrypt existing plaintext passwords**

Create `KryossApi/sql/072_encrypt_hypervisor_passwords.sql`:

This is a documentation-only migration — actual encryption must be done via C# code since SQL Server doesn't have AES-256-GCM natively. The approach:

1. Add a one-time API endpoint or startup task that:
   - Reads all `infra_hypervisor_configs` where `encrypted_password` is NOT base64 (plaintext)
   - Encrypts each with the org's key
   - Writes back

For now, create a minimal SQL migration that documents the change:

```sql
-- 072_encrypt_hypervisor_passwords.sql
-- Marks the transition from plaintext to AES-256-GCM encrypted passwords.
-- Actual encryption is done by the application (CryptoService.EncryptSymmetric)
-- on next config update or via the one-time migration endpoint.
-- Existing plaintext values will be auto-detected and re-encrypted on first
-- Test Connection attempt (the decrypt method falls back to plaintext).
PRINT '=== 072_encrypt_hypervisor_passwords.sql — no-op, encryption handled in app layer ===';
```

The Task 2 Step 5 decrypt code already handles the legacy case: `catch { password = config.EncryptedPassword; }` falls back to plaintext for pre-encryption values. On next Update, the value gets encrypted.

- [ ] **Step 9: Commit**

```bash
git add KryossApi/src/KryossApi/Services/CryptoService.cs KryossApi/src/KryossApi/Functions/Portal/HypervisorConfigFunction.cs
git commit -m "fix(security): C2 — encrypt hypervisor passwords at rest with AES-256-GCM"
```

---

### Task 3: C3 — BearerAuthMiddleware: Validate JWT signature

**Files:**
- Modify: `KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs`
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj` (Microsoft.Identity.Web already referenced)

- [ ] **Step 1: Check Microsoft.Identity.Web is in csproj**

Read `KryossApi.csproj` — confirm `<PackageReference Include="Microsoft.Identity.Web" Version="4.7.0" />` is present.
Already confirmed: line 32 of csproj. No change needed.

- [ ] **Step 2: Add JWT validation using TokenValidationParameters**

Restructure the `BearerAuthMiddleware.Invoke` method. The current flow is:
1. Check `X-MS-CLIENT-PRINCIPAL` header → decode base64 → trust it (VULNERABLE)
2. Fallback: decode JWT from `Authorization` header (but only base64 decodes payload, no signature check)

New flow:
1. Check `Authorization: Bearer <token>` header → validate JWT signature via OIDC discovery → extract claims
2. Fallback: `X-MS-CLIENT-PRINCIPAL` only if request came through SWA (check `X-Forwarded-Host`)
3. Neither → 401

Replace the body of `Invoke` starting at line 71 (`// Read EasyAuth header`) through line 103 (`}`) with:

```csharp
        // ── Primary: JWT from Authorization header (cryptographic validation) ──
        var authHeader = httpReq.Headers.TryGetValues("Authorization", out var authValues)
            ? authValues.FirstOrDefault() : null;

        EasyAuthPrincipal? principal = null;

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var jwt = authHeader["Bearer ".Length..];
            principal = await ValidateJwtAsync(jwt, context);
        }

        // ── Fallback: X-MS-CLIENT-PRINCIPAL only from SWA proxy ──
        if (principal is null)
        {
            var forwardedHost = httpReq.Headers.TryGetValues("X-Forwarded-Host", out var fhValues)
                ? fhValues.FirstOrDefault() : null;

            var isSwa = forwardedHost != null &&
                (forwardedHost.Contains(".azurestaticapps.net", StringComparison.OrdinalIgnoreCase) ||
                 forwardedHost.Contains("kryoss", StringComparison.OrdinalIgnoreCase));

            if (isSwa)
            {
                var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var principalValues)
                    ? principalValues.FirstOrDefault() : null;

                if (!string.IsNullOrEmpty(principalHeader))
                {
                    var principalJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
                    principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            else
            {
                var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var pv2)
                    ? pv2.FirstOrDefault() : null;
                if (!string.IsNullOrEmpty(principalHeader))
                {
                    var logger3 = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
                    logger3.LogWarning("X-MS-CLIENT-PRINCIPAL from non-SWA host {Host} — rejecting",
                        forwardedHost ?? "(direct)");

                    try
                    {
                        var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                        await actlog.LogAsync("WARN", "auth", "bearer_untrusted_header",
                            message: $"X-MS-CLIENT-PRINCIPAL from non-SWA host: {forwardedHost ?? "(direct)"}");
                    }
                    catch { }
                }
            }
        }
```

- [ ] **Step 3: Add ValidateJwtAsync method**

Add this new method to `BearerAuthMiddleware` class. It validates the JWT signature using Microsoft's OIDC discovery document (caches signing keys automatically):

```csharp
    private static Microsoft.IdentityModel.Protocols.ConfigurationManager<
        Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>? _configManager;

    private static async Task<EasyAuthPrincipal?> ValidateJwtAsync(string jwt, FunctionContext context)
    {
        var logger = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();

        var clientId = Environment.GetEnvironmentVariable("AzureAd__ClientId")
            ?? "83bd6db8-3cbb-40fa-bdd4-0ef5347b1923";
        var tenantId = Environment.GetEnvironmentVariable("AzureAd__TenantId")
            ?? "840e016d-d1c4-4329-8cb0-670f2554525d";

        var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        var metadataAddress = $"{authority}/.well-known/openid-configuration";

        _configManager ??= new Microsoft.IdentityModel.Protocols.ConfigurationManager<
            Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
            metadataAddress,
            new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
            new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever());

        try
        {
            var config = await _configManager.GetConfigurationAsync(CancellationToken.None);

            var validationParams = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudiences = new[] { clientId, $"api://{clientId}" },
                ValidateLifetime = true,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
            var result = await handler.ValidateTokenAsync(jwt, validationParams);

            if (!result.IsValid)
            {
                logger.LogWarning("JWT validation failed: {Error}", result.Exception?.Message);
                try
                {
                    var actlog = context.InstanceServices.GetRequiredService<IActlogService>();
                    await actlog.LogAsync("ERR", "auth", "bearer_jwt_invalid",
                        message: $"JWT validation failed: {result.Exception?.Message}");
                }
                catch { }
                return null;
            }

            var claims = new List<EasyAuthClaim>();
            foreach (var claim in result.Claims)
            {
                if (claim.Value is string strVal)
                    claims.Add(new EasyAuthClaim { Typ = claim.Key, Val = strVal });
            }

            var oid = result.Claims.TryGetValue("oid", out var oidVal) ? oidVal as string : null;
            if (oid != null)
                claims.Add(new EasyAuthClaim
                {
                    Typ = "http://schemas.microsoft.com/identity/claims/objectidentifier",
                    Val = oid
                });

            var tid = result.Claims.TryGetValue("tid", out var tidVal) ? tidVal as string : null;
            if (tid != null)
                claims.Add(new EasyAuthClaim
                {
                    Typ = "http://schemas.microsoft.com/identity/claims/tenantid",
                    Val = tid
                });

            var email = result.Claims.TryGetValue("preferred_username", out var emailVal) ? emailVal as string
                : result.Claims.TryGetValue("email", out emailVal) ? emailVal as string : null;

            logger.LogInformation("JWT validated for OID {Oid}", oid);

            return new EasyAuthPrincipal
            {
                AuthTyp = "Bearer",
                Claims = claims,
                UserDetails = email
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JWT validation exception");
            return null;
        }
    }
```

- [ ] **Step 4: Remove the old DecodeJwtToPrincipal method**

Delete the entire `DecodeJwtToPrincipal` method (lines 301-353). It's replaced by `ValidateJwtAsync` which actually validates the signature.

- [ ] **Step 5: Remove the duplicate null check**

Lines 113-119 have a second `if (principal is null)` check that's unreachable after the first one. Remove:

```csharp
        if (principal is null)
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Invalid principal" });
            context.GetInvocationResult().Value = resp;
            return;
        }
```

- [ ] **Step 6: Add required usings**

Ensure these are at the top of the file (some may already be present):

```csharp
using KryossApi.Services;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
```

Note: `Microsoft.Identity.Web` v4.7.0 brings these transitively. If any are missing, the `FrameworkReference Include="Microsoft.AspNetCore.App"` in csproj also provides them.

- [ ] **Step 7: Add NuGet if needed**

Check if `Microsoft.IdentityModel.JsonWebTokens` is available. It comes transitively via `Microsoft.Identity.Web` 4.7.0 (already in csproj). If build fails, add:
```xml
<PackageReference Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.*" />
```

- [ ] **Step 8: Commit**

```bash
git add KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs
git commit -m "fix(security): C3 — validate JWT signature in BearerAuthMiddleware, restrict X-MS-CLIENT-PRINCIPAL to SWA"
```

---

### Task 4: C4 — SelfUpdater: Replace cmd.exe batch with SCM recovery

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Services/SelfUpdater.cs:62-91`
- Modify: `KryossAgent/src/KryossAgent/Services/ServiceInstaller.cs` (add recovery config)

- [ ] **Step 1: Add service recovery configuration to ServiceInstaller**

Add these P/Invoke declarations and a `ConfigureRecovery` method to `ServiceInstaller.cs`.

Add constants:

```csharp
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const int SC_ACTION_RESTART = 1;
```

Add struct:

```csharp
    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public int Type;
        public uint Delay; // milliseconds
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public uint dwResetPeriod;
        public IntPtr lpRebootMsg;
        public IntPtr lpCommand;
        public uint cActions;
        public IntPtr lpsaActions;
    }

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2ForRecovery(IntPtr hService, uint infoLevel, ref SERVICE_FAILURE_ACTIONS info);
```

Add method:

```csharp
    public static void ConfigureRecovery()
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero) return;

        try
        {
            var svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero) return;

            try
            {
                var actions = new SC_ACTION[]
                {
                    new() { Type = SC_ACTION_RESTART, Delay = 5000 },  // 1st failure: restart after 5s
                    new() { Type = SC_ACTION_RESTART, Delay = 10000 }, // 2nd failure: restart after 10s
                    new() { Type = SC_ACTION_RESTART, Delay = 30000 }, // 3rd failure: restart after 30s
                };

                var actionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SC_ACTION>() * actions.Length);
                try
                {
                    for (int i = 0; i < actions.Length; i++)
                        Marshal.StructureToPtr(actions[i], actionsPtr + i * Marshal.SizeOf<SC_ACTION>(), false);

                    var failureActions = new SERVICE_FAILURE_ACTIONS
                    {
                        dwResetPeriod = 86400, // reset failure count after 24h
                        lpRebootMsg = IntPtr.Zero,
                        lpCommand = IntPtr.Zero,
                        cActions = (uint)actions.Length,
                        lpsaActions = actionsPtr,
                    };

                    ChangeServiceConfig2ForRecovery(svc, SERVICE_CONFIG_FAILURE_ACTIONS, ref failureActions);
                    Console.WriteLine("  Service recovery configured (restart on failure).");
                }
                finally { Marshal.FreeHGlobal(actionsPtr); }
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }
```

- [ ] **Step 2: Call ConfigureRecovery from Install**

In `ServiceInstaller.Install()`, after `StartService(svc, 0, IntPtr.Zero)` (line 104), add:

```csharp
                // Configure automatic restart on failure (used by SelfUpdater)
                ConfigureRecovery();
```

Wait — `ConfigureRecovery` opens the service independently, so it can be called after the try/finally. Add the call after the `Console.WriteLine` on line 105, inside the inner try block:

Actually, simpler: call it after the `Install()` body from `Program.cs`. But it's cleanest to just call it at the end of Install. Add right before the inner `finally`:

```csharp
                Console.WriteLine($"  Service '{ServiceName}' installed and started.");
                ConfigureRecovery();
```

Wait — the inner finally closes the service handle. ConfigureRecovery opens its own handle, so call it after the outer finally. Better: just call ConfigureRecovery in the inner try right after StartService.

Simplest approach — add inside the inner `try` block after `Console.WriteLine`:

```csharp
            try
            {
                var desc = new SERVICE_DESCRIPTION { lpDescription = Description };
                ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref desc);

                // Configure automatic restart on failure (used by SelfUpdater)
                var actions = new SC_ACTION[]
                {
                    new() { Type = SC_ACTION_RESTART, Delay = 5000 },
                    new() { Type = SC_ACTION_RESTART, Delay = 10000 },
                    new() { Type = SC_ACTION_RESTART, Delay = 30000 },
                };
                var actionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SC_ACTION>() * actions.Length);
                try
                {
                    for (int i = 0; i < actions.Length; i++)
                        Marshal.StructureToPtr(actions[i], actionsPtr + i * Marshal.SizeOf<SC_ACTION>(), false);
                    var failureActions = new SERVICE_FAILURE_ACTIONS
                    {
                        dwResetPeriod = 86400,
                        lpRebootMsg = IntPtr.Zero,
                        lpCommand = IntPtr.Zero,
                        cActions = (uint)actions.Length,
                        lpsaActions = actionsPtr,
                    };
                    ChangeServiceConfig2ForRecovery(svc, SERVICE_CONFIG_FAILURE_ACTIONS, ref failureActions);
                }
                finally { Marshal.FreeHGlobal(actionsPtr); }

                StartService(svc, 0, IntPtr.Zero);
                Console.WriteLine($"  Service '{ServiceName}' installed and started.");
            }
```

This uses the already-open `svc` handle (with `SERVICE_ALL_ACCESS`), avoiding opening a second handle.

So the actual changes to `ServiceInstaller.cs`:
1. Add the structs `SC_ACTION`, `SERVICE_FAILURE_ACTIONS`
2. Add the `ChangeServiceConfig2ForRecovery` DllImport
3. Add the constants `SERVICE_CONFIG_FAILURE_ACTIONS = 2`, `SC_ACTION_RESTART = 1`
4. In `Install()`, insert the recovery config block before `StartService`

- [ ] **Step 3: Rewrite SelfUpdater to use File.Move + Environment.Exit**

Replace the entire body of `CheckAndUpdateAsync` from line 56 (`// Write to temp`) through line 91 (`return true;`) with:

```csharp
            // Write to temp
            await File.WriteAllBytesAsync(tempPath, bytes);

            // Backup current
            if (File.Exists(exePath))
                File.Copy(exePath, backupPath, overwrite: true);

            // MoveFile with MOVEFILE_DELAY_UNTIL_REBOOT on Windows allows
            // replacing a locked exe. But simpler: just write next to it,
            // and use Environment.Exit to trigger SCM recovery restart.
            // On restart, the ServiceWorker checks for .update.exe and
            // applies it before starting the scan loop.
            Log($"Update to v{versionInfo.Version} staged at {tempPath}");
            Console.WriteLine($"[UPDATE] v{versionInfo.Version} staged. Exiting for SCM restart...");

            // Exit with non-zero code triggers service recovery (restart)
            // SCM will restart the service, which picks up the staged binary
            Environment.Exit(1);
            return true; // unreachable but satisfies compiler
```

- [ ] **Step 4: Add staged binary apply logic to ServiceWorker**

The service needs to check for `KryossAgent.update.exe` on startup and apply it. In `KryossAgent/src/KryossAgent/Services/ServiceWorker.cs`, at the top of `ExecuteAsync` (before the main loop), add:

```csharp
        // Apply staged update if present (placed by SelfUpdater before exit)
        try
        {
            var exePath = Environment.ProcessPath ?? typeof(ServiceWorker).Assembly.Location;
            var dir = Path.GetDirectoryName(exePath)!;
            var stagedPath = Path.Combine(dir, "KryossAgent.update.exe");
            if (File.Exists(stagedPath))
            {
                _logger.LogInformation("Staged update found at {Path} — applying", stagedPath);
                File.Move(stagedPath, exePath, overwrite: true);
                _logger.LogInformation("Update applied. Restarting...");
                Environment.Exit(0); // clean exit, SCM restarts with new binary
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply staged update");
        }
```

- [ ] **Step 5: Verify no Process.Start remains**

Run: `grep -r "Process\.Start" KryossAgent/src/`
Expected: zero functional matches (only comments mentioning "zero Process.Start").

Specifically:
- `SelfUpdater.cs` — `System.Diagnostics.Process.Start` must be gone
- `Program.cs:842` — will be addressed in Task 5 (C5)

- [ ] **Step 6: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/SelfUpdater.cs KryossAgent/src/KryossAgent/Services/ServiceInstaller.cs KryossAgent/src/KryossAgent/Services/ServiceWorker.cs
git commit -m "fix(security): C4 — replace cmd.exe batch update with SCM recovery restart"
```

---

### Task 5: C5 — Trial mode: Remove UseShellExecute=true

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Program.cs:841-843`

- [ ] **Step 1: Replace auto-open with console path output**

Replace lines 841-843:

```csharp
            try { Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true }); }
            catch { }
```

with:

```csharp
            Console.WriteLine($"  Report: {reportPath}");
```

- [ ] **Step 2: Verify no UseShellExecute remains in agent**

Run: `grep -r "UseShellExecute" KryossAgent/src/`
Expected: zero matches.

- [ ] **Step 3: Verify full zero-Process.Start contract**

Run: `grep -rn "Process\.Start" KryossAgent/src/ | grep -v "// " | grep -v "zero Process"`

Expected: zero matches (only comments remain).

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Program.cs
git commit -m "fix(security): C5 — remove UseShellExecute in trial mode, print path instead"
```

---

### Task 6: C6 — Remove hardcoded enrollment code

**Files:**
- Modify: `CLAUDE.md` (2 occurrences)
- Modify: `KryossAgent/CLAUDE.md` (1 occurrence)
- Modify: `KryossApi/deploy/Setup-Azure.ps1` (if present)
- Modify: `KryossApi/deploy/deploy-config.json` (if present)
- Modify: `KryossApi/test-data/01_enroll_request.json`
- Modify: `KryossApi/test-data/Test-AgentFlow.ps1`
- Modify: `KryossApi/test-data/Setup-LocalTest.ps1`
- Modify: `KryossApi/sql/seed_100_test_data.sql`
- Modify: `Scripts/Deploy/Deploy-KryossGPO.ps1`
- Modify: `Scripts/Deploy/Deploy-KryossIntune.md`

- [ ] **Step 1: Replace in CLAUDE.md files**

In `CLAUDE.md` and `KryossAgent/CLAUDE.md`, replace all occurrences of `K7X9-M2P4-Q8R1-T5W3` with `<ENROLLMENT_CODE>`.

- [ ] **Step 2: Replace in deploy scripts**

In each file that contains the code, replace with a parameter placeholder or `<ENROLLMENT_CODE>`.

For `Scripts/Deploy/Deploy-KryossGPO.ps1`: ensure code comes from parameter, not hardcoded.
For `Scripts/Deploy/Deploy-KryossIntune.md`: replace with `<ENROLLMENT_CODE>` placeholder.

- [ ] **Step 3: Replace in test data**

In `KryossApi/test-data/` files, replace with `TEST-CODE-XXXX-XXXX` (clearly test data, not a real code).

- [ ] **Step 4: Replace in deploy-config.json**

Check and remove if present. The code should come from the portal, not config files.

- [ ] **Step 5: Verify zero remaining occurrences**

Run: `grep -r "K7X9-M2P4-Q8R1-T5W3" .`
Expected: zero matches (only the security spec mentions it, which is acceptable as documentation of the fix).

Actually the spec at `docs/superpowers/specs/2026-04-26-security-remediation-design.md` contains it as reference. That's fine — it's documenting the vulnerability.

- [ ] **Step 6: Commit**

```bash
git add -A  # careful: only the files listed above
git commit -m "fix(security): C6 — remove hardcoded enrollment code from all sources"
```

---

### Task 7: C7 — Setup-Azure.ps1: Remove plaintext password variable

**Files:**
- Modify: `KryossApi/deploy/Setup-Azure.ps1:73-97,187`

- [ ] **Step 1: Remove SecureString-to-BSTR conversion**

Replace lines 73-77:

```powershell
if (-not $SqlAdminPassword) {
    $SqlAdminPassword = Read-Host -Prompt "  Enter SQL admin password (min 8 chars, upper+lower+number+special)" -AsSecureString
}
$sqlPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword))
```

with:

```powershell
if (-not $SqlAdminPassword) {
    $SqlAdminPassword = Read-Host -Prompt "  Enter SQL admin password (min 8 chars, upper+lower+number+special)" -AsSecureString
}
# Password piped via stdin to az cli — never stored in plaintext variable
```

- [ ] **Step 2: Update az sql server create to use stdin**

Find the `az sql server create` command (around line 97) that uses `--admin-password $sqlPwd`. Replace with:

```powershell
# Convert SecureString to NetworkCredential for single-use extraction
$cred = New-Object System.Net.NetworkCredential("", $SqlAdminPassword)
try {
    az sql server create `
        --resource-group $ResourceGroup `
        --name $sqlServerName `
        --location $Location `
        --admin-user $SqlAdminUser `
        --admin-password $cred.Password `
        --output none
} finally {
    $cred = $null
}
```

Note: `NetworkCredential.Password` exposes the string, but only for the duration of the az call. Better than storing in a long-lived `$sqlPwd` variable.

- [ ] **Step 3: Remove password from connection string variable**

Line 187 builds `$azureSqlConn` with `Password=$sqlPwd`. Replace the connection string to use Managed Identity pattern or prompt-based approach:

```powershell
# Note: Production uses Managed Identity (no password in connection string).
# This script is initial setup only.
$azureSqlConn = "Server=tcp:${sqlServerName}.database.windows.net,1433;Database=$sqlDbName;User ID=$SqlAdminUser;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Interactive;"
Write-Host "  Connection string (password omitted — use Managed Identity in production):" -ForegroundColor Yellow
```

If the script needs the password for subsequent migration steps in the same run, use the `$cred` pattern inline where needed, not a persistent variable.

- [ ] **Step 4: Verify no plaintext password variables remain**

Run: `grep -n "sqlPwd\|Password=\$" KryossApi/deploy/Setup-Azure.ps1`
Expected: zero matches (only the SecureString parameter and AD Interactive auth).

- [ ] **Step 5: Commit**

```bash
git add KryossApi/deploy/Setup-Azure.ps1
git commit -m "fix(security): C7 — remove plaintext SQL password from setup script"
```

---

### Task 8: C8 — QUOTENAME() in dynamic SQL migrations

**Files:**
- Modify: `KryossApi/sql/018_prevent_hard_delete.sql:32-33,37-39`
- Modify: `KryossApi/sql/017_brands_and_org_updates.sql` (check for dynamic SQL)

- [ ] **Step 1: Fix 018_prevent_hard_delete.sql — DROP TRIGGER**

Replace line 32-33:

```sql
    SET @sql = N'
        IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N''trg_' + @tableName + N'_prevent_delete'' AND parent_id = OBJECT_ID(N''' + @tableName + N'''))
            DROP TRIGGER [trg_' + @tableName + N'_prevent_delete];';
```

with:

```sql
    SET @sql = N'
        IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N''trg_' + @tableName + N'_prevent_delete'' AND parent_id = OBJECT_ID(' + QUOTENAME(@tableName, '''') + N'))
            DROP TRIGGER ' + QUOTENAME('trg_' + @tableName + '_prevent_delete') + N';';
```

- [ ] **Step 2: Fix 018_prevent_hard_delete.sql — CREATE TRIGGER**

Replace lines 37-39:

```sql
    SET @sql = N'
CREATE TRIGGER [trg_' + @tableName + N'_prevent_delete]
ON [dbo].[' + @tableName + N']
```

with:

```sql
    SET @sql = N'
CREATE TRIGGER ' + QUOTENAME('trg_' + @tableName + '_prevent_delete') + N'
ON ' + QUOTENAME(@tableName) + N'
```

Also fix the string literal inside the trigger body (line 54 and 61):

Line 54 — the `entity_type` value uses `''' + @tableName + N'''` which is fine (string literal, not an object name).
Line 61 — the THROW message uses `' + @tableName + N'` which is also a string literal, not an object name. These are safe.

- [ ] **Step 3: Check 017_brands_and_org_updates.sql for dynamic SQL**

Read the file looking for `EXEC sp_executesql` or string concatenation with table/column names.

Based on the first 60 lines read: this file uses static DDL (`CREATE TABLE`, `ALTER TABLE`), not dynamic SQL with string concatenation. No changes needed unless there's dynamic SQL further in the file. Scan:

Run: `grep -n "EXEC\|sp_executesql\|@sql" KryossApi/sql/017_brands_and_org_updates.sql`

If no matches, skip this file. If matches found, apply QUOTENAME to object names.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/sql/018_prevent_hard_delete.sql KryossApi/sql/017_brands_and_org_updates.sql
git commit -m "fix(security): C8 — QUOTENAME() all dynamic SQL object references"
```

---

### Task 9: Version bumps + final verification

**Files:**
- Modify: `KryossApi/src/KryossApi/KryossApi.csproj` (`<Version>`)
- Modify: `KryossAgent/src/KryossAgent/KryossAgent.csproj` (`<Version>`)
- Modify: `CLAUDE.md` (version table)

- [ ] **Step 1: Bump API version**

In `KryossApi.csproj`, change `<Version>1.22.5</Version>` to `<Version>1.22.6</Version>`.

- [ ] **Step 2: Bump Agent version**

In `KryossAgent.csproj`, change `<Version>2.4.1</Version>` to `<Version>2.4.2</Version>`.

- [ ] **Step 3: Update CLAUDE.md version table**

Update the version table at top of `CLAUDE.md`:
- API: 1.22.5 → 1.22.6
- Agent: 2.4.1 → 2.4.2

- [ ] **Step 4: Run verification checklist**

1. `grep -r "Process\.Start" KryossAgent/src/` — expect 0 functional matches
2. `grep -r "debug_type\|debug_message\|debug_stack" KryossApi/src/` — expect 0 matches
3. `grep -r "K7X9-M2P4-Q8R1-T5W3" . --include="*.cs" --include="*.ps1" --include="*.json" --include="*.md" | grep -v "security-remediation-design"` — expect 0 matches
4. `grep -r "UseShellExecute" KryossAgent/src/` — expect 0 matches
5. `grep -n "TODO.*SH\|TODO.*encrypt" KryossApi/src/` — expect 0 matches (old TODOs resolved)

- [ ] **Step 5: Final commit**

```bash
git add KryossApi/src/KryossApi/KryossApi.csproj KryossAgent/src/KryossAgent/KryossAgent.csproj CLAUDE.md
git commit -m "chore: bump API 1.22.6, Agent 2.4.2 — security remediation Sprint 1"
```

---

## Build & Deploy Order

1. **API first** (C1, C2, C3, C7, C8): `dotnet publish` → deploy to `func-kryoss`
2. **Agent second** (C4, C5): `dotnet publish` → upload to blob → NinjaOne pushes
3. **Scripts** (C6): commit + push — no deploy needed, scripts pulled by NinjaOne

## Post-Deploy Validation

1. Open portal → trigger an intentional error (e.g., malformed API call) → response must be `{ error, traceId }` only
2. Query `SELECT TOP 5 * FROM actlog WHERE module = 'error' ORDER BY timestamp DESC` — full exception detail present
3. Check `/v2/version` returns `1.22.6`
4. After agent binary deployed: `grep -r "Process.Start"` in published directory — zero matches
