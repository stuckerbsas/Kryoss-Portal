# Kryoss Security Baseline

**Status:** Authoritative. This document is the zero-trust baseline for the
entire Kryoss stack (Agent → API → DB). Every change to auth, crypto, key
management, or ingest flow MUST be audited against this file before merging.

**Owner:** Federico / Geminis Computer S.A.
**Last reviewed:** 2026-04-08
**Review cadence:** every 6 months, on any crypto-library upgrade, and
immediately after any suspected key leak or token compromise.

---

## Threat model (one-paragraph version)

The Kryoss Agent runs as SYSTEM on **untrusted Windows endpoints** owned by
MSP clients. We must assume the machine may be fully compromised at any
time — an attacker with admin rights can read the token file, attach a
debugger to the agent process, and dump AES keys from memory. Our design
therefore does NOT try to keep the client machine trustworthy; it assumes
the client is a potentially hostile sensor and enforces integrity and
authenticity **server-side** using independent signals (HMAC over canonical
request + TPM-bound hardware fingerprint + nonce cache + rate limits +
anomaly detection on ingested data).

The only assets that MUST remain confidential are:
1. The RSA **private** key (lives in Azure Key Vault HSM, never exportable)
2. The per-org HMAC secret at rest in the DB (encrypted column)
3. User-identifying data in `control_results.finding` and `raw_*` snapshots

---

## Defense-in-depth layers

| Layer | Mechanism | Goal |
|---|---|---|
| Transport | TLS 1.2+ with SPKI pinning on intermediate cert | Prevent MITM |
| Envelope | RSA-OAEP-256 wrap + AES-256-GCM encrypt | Confidentiality at rest in logs/WAFs |
| Integrity | HMAC-SHA256 over canonical request (ts + method + path + hwid + tokenId + bodyHash) | Tamper + replay resistance |
| Replay | Nonce cache (signature as nonce, 10 min TTL) + ±5 min timestamp window | Exact-request replay |
| Identity | TPM-bound hardware fingerprint, verified on every request | Token cloning detection |
| Auth | Per-device API key (NOT the user-facing enrollment code) | Least privilege |
| RLS | SESSION_CONTEXT('org_id', …) enforced on every SQL connection | Cross-tenant data leak |
| Infra | Managed Identity to SQL, no connection strings anywhere | Credential exposure |
| App | DTOs separate from EF entities, no mass assignment | Privilege escalation via JSON |
| Errors | Global sanitization middleware, correlationId only | Information leakage |

---

## Canonical request format

The HMAC input is frozen. Changing ANY byte in this format is a breaking
change that MUST bump the `Alg` field in the envelope:

```
<unix_timestamp>\n
<UPPERCASE_HTTP_METHOD>\n
<request_path_without_host>\n
<hardware_fingerprint_hex>\n
<token_id>\n
<hex_sha256_of_envelope_bytes>
```

HMAC key is derived from the per-device shared secret via HKDF-SHA256:
- `ikm` = token secret (bytes)
- `salt` = `"kryoss:v1:hmac"`
- `info` = `"token:{tokenId}"`
- `outputLength` = 32

**Never** use the token directly as the HMAC key. HKDF separates domains
so the same token can be reused for other purposes (e.g. v2 signing)
without cross-protocol attacks.

---

## Envelope format

```json
{
  "kid": "kryoss-env-2026q2",
  "alg": "RSA-OAEP-256+A256GCM",
  "epk": "<base64 of RSA-wrapped AES-256 key>",
  "iv":  "<base64 of 96-bit GCM nonce>",
  "ct":  "<base64 of ciphertext>",
  "tag": "<base64 of 128-bit GCM tag>",
  "hwid": "<hardware fingerprint hex>"
}
```

GCM associated data (AAD) is length-prefixed:
`int32_be(hwidLen) || hwid || int32_be(tokenIdLen) || tokenId`

This binds the ciphertext to BOTH the machine and the token. A stolen
envelope replayed from a different machine fails GCM auth tag verification
even if the attacker also stole the HMAC key.

---

## Required HTTP headers on every ingest

| Header | Purpose |
|---|---|
| `X-Kryoss-Timestamp` | Unix seconds, ±300 s window |
| `X-Kryoss-Signature` | Base64 HMAC-SHA256 — also serves as nonce |
| `X-Kryoss-Key-Id` | Must match `kid` inside envelope (defense in depth) |
| `X-Kryoss-Token-Id` | Looks up per-device token record |
| `X-Kryoss-Hardware-Id` | Must match `tokenRecord.HardwareId` server-side |
| `X-Kryoss-Agent-Version` | For forensics + deprecation tracking |
| `Content-Type` | Must be `application/kryoss-envelope+json` |

---

## Hardware fingerprint composition

Priority order (combine at least two available sources):

1. **TPM 2.0 Endorsement Key public hash** — tamper-resistant, reuses
   `TpmEngine` code. Required on W11. Preferred on all platforms.
2. **UEFI SMBIOS UUID** (`Win32_ComputerSystemProduct.UUID`) — stable,
   survives OS reinstall.
3. **MachineGuid registry key** — fallback only, easy to clone.

Fingerprint = `SHA-256(component1 || '|' || component2 || ...)` in hex.

If NO components are available, enrollment MUST fail hard — never
generate a fingerprint from process data, environment variables, or
network MAC.

---

## Key management lifecycle

### RSA envelope keys (server-side private key)

- **Generation:** `az keyvault key create --kty RSA-HSM --size 2048
  --ops unwrapKey wrapKey` inside Managed HSM. Private key material
  never leaves the HSM boundary.
- **Storage:** Azure Key Vault Premium (HSM-backed). The Function App's
  Managed Identity has ONLY `unwrapKey` permission on the specific key.
- **Public key distribution:** exported once as PEM at deploy time,
  embedded in the signed agent binary as a resource. Safe because public.
- **Routine rotation:** every 12 months. Automated timer creates new
  `kid`, server accepts BOTH during 30-day transition window, adoption
  monitored via `assessment_runs.key_id` histogram. Old kid revoked when
  < 1% of traffic still uses it.
- **Emergency rotation:** 24 h window, forced agent reinstall via RMM.
- **Deletion:** never. Key versions are archived, not deleted, for
  forensic re-decryption of historical payloads if needed.

### HMAC secrets (per-device shared secrets)

- **Generation:** server-side `RandomNumberGenerator.GetBytes(32)` during
  enrollment, returned ONCE to the agent over TLS + envelope.
- **Storage (server):** encrypted at rest in `auth_api_keys.secret_enc`
  via SQL Always Encrypted with column master key in Key Vault.
- **Storage (agent):** DPAPI-protected blob in
  `C:\ProgramData\Kryoss\agent.bin`, readable only by SYSTEM.
- **Rotation:** 90 days routine, immediate on suspicion. Agent fetches
  a new secret via signed rotation endpoint using the old secret, with
  a 24 h grace period where both are accepted.

### User-facing enrollment codes

- **Format:** `XXXX-XXXX-XXXX-XXXX` uppercase alphanumeric (no ambiguous
  chars — no 0/O/1/I/L).
- **Lifetime:** 24 h from issue, **single use**. Expiry enforced by
  `enrollment_codes.expires_at` check in `EnrollmentService`.
- **Single-use enforcement:** `enrollment_codes.used_by` MUST be set to
  the new `machines.id` in the same transaction as the machine row.
  If this is not set, single-use is silently broken. **KNOWN BUG as of
  2026-04-08, tracked in Known gaps.**

---

## The weakest link

Current and foreseeable: **a compromised endpoint submitting crafted,
valid payloads**. Envelope encryption and HMAC cannot prevent this
because both primitives can be exercised by malware running as SYSTEM
on the victim machine.

Mitigation chain (none is sufficient alone):

1. TPM-bound hardware fingerprint → clones on a second machine are
   rejected as `hardware_mismatch`.
2. Per-machine rate limit → one run per X minutes max.
3. Simultaneous-use detection → same `TokenId` active from two
   `HardwareId`s within N minutes auto-revokes the token.
4. Server-side anomaly scoring → impossible state transitions in
   `raw_security_posture` (e.g. BitLocker flipping off→on→off) flag
   the run.
5. Authenticode-signed agent binary → supply-chain integrity.
6. NinjaRMM deploy script refuses unsigned builds.

**Principle:** the agent is a sensor, not an oracle. Score on server
signals, not client self-attestation.

---

## Code references

These files are the authoritative implementation of the baseline.
Changes here require a security review:

| Concern | File | Status |
|---|---|---|
| Client envelope + HMAC | `KryossAgent/src/KryossAgent/Services/SecurityService.cs` | ✅ implemented, wired into `ApiClient.SubmitResultsAsync` |
| Client SSL pinning | `KryossAgent/src/KryossAgent/Services/PinnedHttpHandler.cs` | ✅ implemented (log-only by default — set `SpkiPins` to enforce) |
| Client hardware fingerprint | `KryossAgent/src/KryossAgent/Services/HardwareFingerprint.cs` | ✅ implemented (registry-based; TPM EK upgrade is Phase 2) |
| Dead-code legacy crypto | `KryossAgent/src/KryossAgent/Services/CryptoService.cs` | ✅ retired (replaced by SecurityService) |
| Current HMAC auth middleware | `KryossApi/src/KryossApi/Middleware/ApiKeyAuthMiddleware.cs` | ✅ plus nonce cache; canonical format still v1 (ts+method+path+bodyHash). Upgrading to v2 canonical (with hwid + tokenId) is a coordinated agent+server breaking change, tracked separately. |
| Ingest function | `KryossApi/src/KryossApi/Functions/Agent/ResultsFunction.cs` | ✅ envelope decrypt + header-based identity + hwid verify + scoped tenant lookup |
| Nonce cache | `KryossApi/src/KryossApi/Services/NonceCache.cs` | ✅ in-process ConcurrentDictionary; Redis upgrade queued for multi-instance scale |
| Hwid verifier | `KryossApi/src/KryossApi/Services/HwidVerifier.cs` | ✅ backfill + mismatch detection |
| Error sanitization | `KryossApi/src/KryossApi/Middleware/ErrorSanitizationMiddleware.cs` | ✅ registered first in pipeline |
| Managed Identity DbContext | `KryossApi/src/KryossApi/Program.cs` | ✅ `AccessTokenCallback` + no connection strings in config |
| RLS middleware | `KryossApi/src/KryossApi/Middleware/RlsMiddleware.cs` | OK |

---

## Implementation backlog (ordered by priority)

Priority legend: **P0** = exploitable now, **P1** = breaks the baseline,
**P2** = hardening.

1. ✅ **P0** — Fix `enrollment_codes.used_by` not being set in
   `EnrollmentService`. Verified 2026-04-09: the filter +
   `enrollment.UsedBy = machine.Id` setter are in place. Single-use
   guarantee holds.
2. ✅ **P0** — `SqlConnectionString` removed; `Program.cs` now uses
   Managed Identity via `AccessTokenCallback`. Function App executes
   as external SQL user `func-kryoss`.
3. ✅ **P0** — `ResultsFunction` now treats `X-Agent-Id` (HMAC-signed
   header) as the sole identity source; body `AgentId` must match or
   the request is rejected 401. Machine lookup is scoped by
   `OrganizationId` — no more cross-tenant hazard. (Full DTO split is
   still pending as a code-quality follow-up but the exploitable path
   is closed.)
4. ✅ **P1** — `SecurityService.cs` envelope encryption implemented
   and wired into `ApiClient.SubmitResultsAsync`. Dead
   `CryptoService.cs` retired.
5. ✅ **P1** — `NonceCache.cs` implemented as in-process
   `ConcurrentDictionary` with 10-min TTL and amortized sweep.
   Registered as singleton, checked in `ApiKeyAuthMiddleware` after
   HMAC validation. NOTE: per-instance only — a Redis upgrade is
   tracked as a follow-on when the Function App scales past one
   consumption-plan instance.
6. ✅ **P1** — `ErrorSanitizationMiddleware.cs` registered as FIRST
   middleware in `Program.cs`. Returns frozen
   `{"error":"internal_error","traceId":...}` on any unhandled
   exception; real stack trace stays in App Insights.
7. ✅ **P1** — `HardwareFingerprint.cs` implemented on the agent
   (MachineGuid + BIOS/baseboard SHA-256). Agent sends `X-Hwid` on
   every request including enrollment. Server stores
   `machines.hwid`, backfills on first contact via `HwidVerifier`,
   rejects mismatches on every subsequent request.
   TPM EK attestation is Phase 2 (tracked in phase-roadmap.md).
8. 🟡 **P2** — Key Vault key still `--kty RSA`. Promotion to
   `--kty RSA-HSM` blocked on Key Vault Premium SKU spend approval.
   See **Runbook: RSA key HSM promotion** below.
9. ✅ **P2** — `PinnedHttpHandler.cs` implemented on the agent with
   SPKI pinning. Ships in **log-only** mode by default (prints
   observed SPKI hash on first connect) so operators can capture the
   production value before enforcing. Switch to enforce-mode by
   populating `HKLM\SOFTWARE\Kryoss\Agent\SpkiPins` (comma-separated,
   minimum two pins for rotation).
10. 🟡 **P2** — Rotation runbook documented below. Timer trigger
    (scheduled reminder every 12 months) tracked as a separate work
    item — low priority because runbook steps are manual anyway.

---

## Runbook: RSA key HSM promotion (P2 #8)

Prerequisite: Key Vault Premium SKU (~$1/key/month + $0.03/10k ops).

1. Create a new HSM-backed key alongside the existing software key:
   ```
   az keyvault key create \
     --vault-name kv-kryoss \
     --name kryoss-env-2026q2-hsm \
     --kty RSA-HSM --size 2048 \
     --ops unwrapKey wrapKey
   ```
2. Export public key PEM:
   ```
   az keyvault key download --vault-name kv-kryoss \
     --name kryoss-env-2026q2-hsm --encoding PEM --file pub.pem
   ```
3. Update `CryptoService.cs` (server) to accept EITHER `kid` during
   the transition window. Deploy.
4. Update all `org_crypto_keys.public_key_pem` rows for orgs whose
   agents can be redeployed (push via portal rotation endpoint).
5. Monitor `assessment_runs.kid` histogram. When the old kid drops
   below 1% of traffic, revoke it via `az keyvault key set-attributes
   --enabled false`.
6. Do NOT delete the old key version. Re-decryption of historical
   ciphertext may be needed for compliance audits.

**Rollback:** if the new HSM key fails to unwrap for any reason,
re-enable the old software key via `set-attributes --enabled true`
within 5 minutes. The server accepts both kids simultaneously so
this is zero-downtime.

---

## Runbook: Key + secret rotation (P2 #10)

Three independent rotation cycles; never rotate two at once in the
same maintenance window.

### RSA envelope key (every 12 months)

1. Create new `kryoss-env-<year>q<quarter>` in Key Vault (HSM when
   available, software otherwise).
2. Set server to accept both kids (`CryptoService` reads all enabled
   versions from Key Vault at startup; a restart picks up the new key).
3. Generate new `org_crypto_keys` rows for every org with the new
   public key. Mark old rows `rotated_at = NOW()` (but leave
   `is_active = 1` until transition window ends).
4. Agents continue uploading with OLD kid — server accepts.
5. Trigger agent redeploys via RMM. New agents pull the new public
   key on next `/v1/enroll` (for fresh enrollments) or via a future
   `/v1/rotate` endpoint (for existing agents).
6. After 30 days, disable old kid: `az keyvault key set-attributes
   --enabled false`. Mark old `org_crypto_keys` rows `is_active = 0`.
7. Archive the old private key version (never delete).

### HMAC secret (per device, every 90 days)

1. Agent calls `/v1/rotate-secret` signed with the OLD secret.
2. Server validates, generates new 32-byte random secret, returns
   it inside an envelope encrypted under the current RSA public key.
3. Server sets `api_keys.previous_secret` = old secret,
   `api_keys.secret` = new secret, `api_keys.rotated_at = NOW()`.
4. For 24 h server accepts HMAC signed with EITHER secret. After 24 h,
   `previous_secret` is nulled out and only the new secret is valid.
5. Agent persists the new secret to DPAPI-protected registry before
   deleting the old one.

**Emergency rotation** (suspected leak): set
`api_keys.previous_secret = NULL` immediately, force agent
re-enrollment via RMM. Machines without RMM reach become offline
until manually touched.

### Hardware fingerprint salt (bump on schema change)

1. Bump `HardwareFingerprint.Salt` constant in the agent (e.g.
   `"kryoss-hwid-v1"` → `"kryoss-hwid-v2"`).
2. Ship new agent build. New hwid will differ on every machine.
3. Server-side, run:
   ```sql
   UPDATE machines SET hwid = NULL WHERE hwid IS NOT NULL;
   ```
   to force backfill on next contact.
4. During the 72 h rollout window, `HwidVerifier` will backfill the
   new hwid on first contact for each machine. After the window,
   any machine still sending the old hwid is treated as suspicious
   (log a SEC-level actlog entry, do NOT auto-block — a forgotten
   offline machine should not trigger an incident).
5. Schedule: only on a deliberate forensic rotation or when adding
   new fingerprint components. Not a routine cadence.

---

## Audit trail

| Date | Event |
|---|---|
| 2026-04-08 | Baseline written. Audit identified 10 open items (3 P0, 4 P1, 3 P2). |
| 2026-04-09 | P0 #1, P0 #2, P0 #3 closed. P1 #4/5/6/7 closed. P2 #9 closed (log-only mode, enforce flip pending ops capture). P2 #8 + #10 documented as runbooks; implementation/cost approval pending. |
