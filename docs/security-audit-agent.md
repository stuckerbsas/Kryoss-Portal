# Security Audit -- Kryoss Agent v1.2.2

## Date: 2026-04-13
## Auditor: Claude (Senior Cybersecurity Auditor)
## Scope: All .cs files in KryossAgent/src/KryossAgent/

---

### CRITICAL FINDINGS

**C-1. Plaintext Credential Storage in Registry (AgentConfig.cs, lines 78-89)**

- **File:** `Config/AgentConfig.cs`
- **Description:** `ApiKey`, `ApiSecret`, and `PublicKeyPem` are stored as plaintext REG_SZ values in `HKLM\SOFTWARE\Kryoss\Agent`. While the comment says "SYSTEM-only ACL in production," the code does not enforce this ACL -- it uses `Registry.LocalMachine.CreateSubKey()` which inherits default ACLs (readable by Administrators and potentially other local users).
- **Risk:** Any local administrator (or process running as admin) can read the API secret and impersonate this agent to the Kryoss API. On shared machines or compromised endpoints, this is a direct credential theft vector.
- **Recommendation:** (1) Use DPAPI (`ProtectedData.Protect`) to encrypt secrets at rest. (2) Explicitly set the registry key ACL to SYSTEM-only via `RegistryAccessRule` in the `Save()` method. (3) At minimum, document that the deployment script must lock down the ACL.

---

**C-2. Password Passed via Command Line to net use and PsExec (NetworkScanner.cs, lines 228-229; PsExecRunner.cs, lines 97-99)**

- **File:** `Services/NetworkScanner.cs`, `Services/PsExecRunner.cs`
- **Description:** Remote admin credentials (username + password) are passed as command-line arguments to `net use` and PsExec. On line 228: ``$"use {uncShare} /user:{username} \"{password}\" /y"``. On PsExecRunner line 98-99, password is added to `psi.ArgumentList`.
- **Risk:** Command-line arguments are visible to any process on the system via `wmic process list full`, `Get-Process | Select CommandLine`, or Task Manager. Any monitoring tool, EDR, or co-resident malware can harvest these credentials. The password also appears in process creation audit logs (Event ID 4688 if command-line logging is enabled).
- **Recommendation:** (1) For `net use`, consider using `WNetAddConnection2` P/Invoke which does not expose credentials on the command line. (2) For PsExec, there is no safe alternative -- document this risk and recommend using the current Windows identity (Kerberos) instead of explicit credentials whenever possible. (3) Zero the password string in memory after use (though .NET makes this difficult).

---

**C-3. Server-Controlled Command Execution (ShellEngine.cs + ControlDef.cs)**

- **File:** `Engines/ShellEngine.cs`, `Models/ControlDef.cs`
- **Description:** The `Executable` and `Arguments` fields in `ControlDef` are downloaded from the API server. The ShellEngine executes whatever executable the server specifies, as long as it resolves to a path under `C:\Windows\System32`, `C:\Windows\SysWOW64`, or `C:\Windows`. This means a compromised API server (or a MitM on the API connection) can instruct the agent to run **any System32 binary with arbitrary arguments** as SYSTEM.
- **Risk:** `C:\Windows\System32` contains hundreds of LOLBins (Living Off the Land Binaries): `powershell.exe`, `cmd.exe`, `certutil.exe`, `mshta.exe`, `regsvr32.exe`, `rundll32.exe`, `wscript.exe`, `cscript.exe`, `bitsadmin.exe`, `msiexec.exe`, etc. An attacker who compromises the API can achieve full remote code execution on every enrolled agent.
- **Recommendation:** (1) Implement a strict allowlist of permitted executables (e.g., only `bcdedit.exe`, `gpresult.exe`, `wbadmin.exe`, `cipher.exe`, `sfc.exe`, `manage-bde.exe`, `auditpol.exe`, `icacls.exe`) rather than allowing any System32 binary. (2) Validate arguments against a pattern/blocklist (block `&&`, `|`, `;`, backtick, `$(`, etc. to prevent command chaining). (3) Sign the control definitions payload so the agent can verify they came from the legitimate server.

---

**C-4. HMAC Signing String Does Not Include AgentId or Hwid (ApiClient.cs, lines 240-248)**

- **File:** `Services/ApiClient.cs`
- **Description:** The HMAC canonical string is `timestamp + METHOD + path + sha256(body)`. It does NOT include the `X-Agent-Id` or `X-Hwid` headers. These headers are sent but are not part of the signed material.
- **Risk:** An attacker who intercepts a valid HMAC signature can replay it with a different `X-Agent-Id` or `X-Hwid` header, potentially submitting results under another machine's identity. The HMAC only proves the body was not tampered with, not which agent sent it.
- **Recommendation:** Include `agentId` and `hwid` in the canonical signing string. The code comments acknowledge this gap (line 232-235: "Not yet part of the HMAC canonical string (v1 format is frozen)") -- this should be prioritized as a breaking change.

---

**C-5. SPKI Pinning Defaults to Log-Only / Disabled (PinnedHttpHandler.cs, AgentConfig.cs)**

- **File:** `Services/PinnedHttpHandler.cs`, `Config/AgentConfig.cs`
- **Description:** SPKI pinning is only enforced when the `SpkiPins` registry value is populated. By default, it runs in log-only mode (line 86-97), which means **no MitM protection beyond standard CA validation**. A compromised or rogue CA can issue a certificate for `func-kryoss.azurewebsites.net` and intercept all agent traffic.
- **Risk:** Combined with C-3 (server-controlled command execution), a MitM attacker who can present a valid CA-signed cert for the API hostname can inject malicious control definitions and achieve RCE as SYSTEM on every agent. This is the attack scenario the SPKI pinning was designed to prevent, but it is not enforced by default.
- **Recommendation:** (1) Ship production binaries with at least one SPKI pin hardcoded (via binary patching or embedded config). (2) Add a `--require-pin` flag or build-time constant that fails hard if no pin is configured. (3) The current log-only mode is fine for initial deployment, but set a deadline to enforce pinning.

---

### HIGH FINDINGS

**H-1. PsExec Extracted to Predictable Path Without Integrity Verification (PsExecRunner.cs, lines 41-55)**

- **File:** `Services/PsExecRunner.cs`
- **Description:** PsExec64.exe is extracted from embedded resources to `%TEMP%\KryossAgent_PsExec64_{PID}.exe`. While the PID makes it less predictable, (a) the filename prefix is fixed and predictable, (b) there is no hash verification of the extracted file, and (c) the temp directory is typically writable by any user on the system.
- **Risk:** A local attacker could pre-create the file (race condition) or replace it between extraction and execution. Additionally, since `Assembly.GetExecutingAssembly()` uses reflection, this may not work correctly under AOT (if AOT is ever enabled).
- **Recommendation:** (1) Verify the SHA-256 hash of the extracted file against a compiled constant before execution. (2) Extract to a SYSTEM-only directory (e.g., `C:\ProgramData\Kryoss\bin\`) instead of `%TEMP%`. (3) Set restrictive ACLs on the extracted file.

---

**H-2. Remote Agent Binary Dropped to Predictable Path (NetworkScanner.cs, line 196)**

- **File:** `Services/NetworkScanner.cs`
- **Description:** The agent binary is copied to `C:\Windows\Temp\KryossAgent.exe` on remote targets via admin share (`\\host\C$\Windows\Temp\KryossAgent.exe`). This is a fixed, well-known path.
- **Risk:** An attacker who gains write access to `C:\Windows\Temp\` on a target machine can replace the binary before PsExec runs it, achieving code execution as SYSTEM. The agent does not verify the integrity of the remote binary.
- **Recommendation:** (1) Use a randomized filename or include a nonce. (2) Sign the agent binary and verify the signature before execution (though this is complex with PsExec). (3) Clean up the binary after execution (this IS done in the `finally` block on line 368, which is good).

---

**H-3. Enrollment Response Contains Secrets in Plaintext HTTP Body (ApiClient.cs, lines 67-95)**

- **File:** `Services/ApiClient.cs`
- **Description:** The enrollment endpoint (`POST /v1/enroll`) returns `ApiKey`, `ApiSecret`, and `PublicKey` in the response body. This response is not encrypted (no envelope encryption for the enrollment flow). The only protection is TLS, which is in log-only SPKI mode by default.
- **Risk:** If TLS is compromised (rogue CA, corporate proxy, MitM), the API key and secret are exposed in the clear, giving the attacker permanent access to impersonate this agent.
- **Recommendation:** (1) Consider implementing a Diffie-Hellman key exchange for the enrollment flow so secrets are never transmitted in the clear even if TLS is compromised. (2) At minimum, enforce SPKI pinning before enrollment.

---

**H-4. Offline Store Saves Sensitive Data as Plaintext JSON (OfflineStore.cs)**

- **File:** `Services/OfflineStore.cs`
- **Description:** When the API is unreachable, the full assessment payload (including hardware info, software inventory, IP addresses, MAC addresses, serial numbers, domain names, and threat findings) is saved as plaintext JSON in `C:\ProgramData\Kryoss\PendingResults\`. The directory inherits default `ProgramData` ACLs (all authenticated users can read).
- **Risk:** Any local user can read the security assessment results, which contain detailed information about the machine's security posture, installed software, and detected threats. This is valuable reconnaissance data.
- **Recommendation:** (1) Encrypt the offline payload using DPAPI or the server's public key (already available after enrollment). (2) Restrict the directory ACL to SYSTEM-only. (3) Add integrity verification (HMAC) to detect tampering of offline payloads before upload.

---

**H-5. Enrollment Code Passed on Command Line and Visible in Process List (Program.cs, NetworkScanner.cs)**

- **File:** `Program.cs`, `Services/NetworkScanner.cs`
- **Description:** The enrollment code is passed as `--code K7X9-M2P4-Q8R1-T5W3` on the command line. In NetworkScanner (line 117), it is also forwarded to remote PsExec executions. Enrollment codes can be multi-use and represent organizational access.
- **Risk:** Enrollment codes are visible in process listings, command history, and audit logs. If the code supports multi-use enrollment, a leaked code allows an attacker to enroll rogue machines into the organization.
- **Recommendation:** (1) Support reading the enrollment code from a file or environment variable instead of the command line. (2) For patched binaries (which embed the code), this is less of an issue since the code is not on the command line.

---

### MEDIUM FINDINGS

**M-1. No Validation of API Response Data (ApiClient.cs, multiple locations)**

- **File:** `Services/ApiClient.cs`
- **Description:** API responses are deserialized directly into model objects without validation. There is no check that `ControlsResponse.Checks` contains valid control definitions, no bounds checking on `TimeoutSeconds`, no validation that `Executable` fields are reasonable.
- **Risk:** A compromised or buggy API could send malformed data that causes unexpected behavior. The `TimeoutSeconds` field from the server directly controls how long processes run (ShellEngine.cs line 113).
- **Recommendation:** (1) Validate `TimeoutSeconds` has a reasonable upper bound (e.g., 60 seconds max). (2) Validate `Executable` at deserialization time, not just at execution time.

---

**M-2. Error Messages May Leak Internal Information (Multiple files)**

- **Files:** `Program.cs` (lines 14, 98, 209, 241, etc.), `ApiClient.cs` (lines 91, 110, 172)
- **Description:** Exception messages including stack traces are written to stderr and stdout. API error responses are echoed directly to the console. In verbose mode, even more detail is exposed.
- **Risk:** In non-silent deployments, detailed error messages (including API URLs, exception types, stack frames) could be captured by monitoring tools or screen-sharing sessions and used for reconnaissance.
- **Recommendation:** (1) Sanitize error output in production (non-verbose) mode. (2) Do not echo raw API error responses -- they may contain internal server details.

---

**M-3. Global Exception Handler Logs Full Exception Object (Program.cs, line 14)**

- **File:** `Program.cs`
- **Description:** `Console.Error.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}")` logs the full exception object, including inner exceptions, stack trace, and any sensitive data in exception messages.
- **Risk:** If an exception contains sensitive data (e.g., a SQL connection string, an API key in a URL, etc.), it will be logged to stderr.
- **Recommendation:** Log only the exception type and a sanitized message. Full stack traces should only appear in verbose mode.

---

**M-4. No Rate Limiting on Enrollment Attempts (Program.cs)**

- **File:** `Program.cs`
- **Description:** The agent will attempt enrollment as many times as it is launched. There is no client-side rate limiting or backoff for failed enrollment attempts.
- **Risk:** An attacker with a leaked enrollment code could rapidly enroll many rogue machines. (Note: server-side rate limiting may exist but is out of scope for this agent audit.)
- **Recommendation:** (1) Implement exponential backoff on enrollment failures. (2) Limit total enrollment attempts before requiring manual intervention.

---

**M-5. NetworkScanner RunProcess Has Classic Stdout/Stderr Deadlock Pattern (NetworkScanner.cs, lines 425-450)**

- **File:** `Services/NetworkScanner.cs`
- **Description:** The `RunProcess` helper (used for `net use`, `sc`, `powershell`) uses the classic deadlock-prone pattern: `ReadToEndAsync()` on both stdout and stderr, then `WaitForExit()`. If the child process produces enough output to fill the pipe buffer before the reads complete, this can deadlock.
- **Risk:** Operational reliability issue. Could cause network scan threads to hang indefinitely (the timeout on line 442 mitigates this somewhat).
- **Recommendation:** Use the same `BeginOutputReadLine/BeginErrorReadLine` pattern from `ProcessHelper.cs`, which was specifically designed to avoid this deadlock.

---

**M-6. Patched Binary Sentinels Are Extractable (EmbeddedConfig.cs)**

- **File:** `Config/EmbeddedConfig.cs`
- **Description:** The sentinel strings (`@@KRYOSS_ENROLL:`, `@@KRYOSS_APIURL:`, etc.) are easily searchable in the compiled binary. After patching, the enrollment code, API URL, org name, and MSP name are stored as UTF-16LE strings in the binary with known prefixes.
- **Risk:** Anyone with access to a patched binary can extract the embedded enrollment code, API URL, and organization details using a hex editor or `strings` command. For multi-use enrollment codes, this could allow unauthorized enrollment.
- **Recommendation:** (1) Accept this as an inherent limitation of binary patching -- it is defense against casual inspection, not against determined reverse engineering. (2) Ensure enrollment codes have limited use counts. (3) Consider obfuscating the sentinel values (XOR with a fixed key) to raise the bar slightly.

---

**M-7. Hygiene Upload Creates a Second ApiClient from Registry (NetworkScanner.cs, lines 599-601)**

- **File:** `Services/NetworkScanner.cs`
- **Description:** `UploadHygieneReport` and `UploadPortResults` create new `ApiClient` instances by calling `AgentConfig.Load()` from registry. This means they re-read credentials from the registry independently rather than receiving them from the caller.
- **Risk:** If the registry is tampered with between the initial load and the hygiene upload, the agent could use modified credentials. This is a minor TOCTOU issue.
- **Recommendation:** Pass the existing `AgentConfig` instance through to these methods instead of re-loading from registry.

---

### LOW / INFORMATIONAL

**L-1. Agent Version Hardcoded in Multiple Places**

- **Files:** `Program.cs` (line 387, line 82), `Models/AssessmentPayload.cs` (line 15)
- **Description:** Agent version string "1.2.2" appears in multiple locations. A version mismatch could cause confusion.
- **Recommendation:** Define a single `const string` for the version.

---

**L-2. ThreatDetector Uses Broad Pattern Matching (ThreatDetector.cs)**

- **File:** `Services/ThreatDetector.cs`
- **Description:** Process name matching uses `Contains` with case-insensitive comparison. Patterns like "Empire", "Hydra", "Chisel", "Beacon" could match legitimate software.
- **Risk:** False positives in threat detection reports. Not a security vulnerability but could erode trust in the tool's accuracy.
- **Recommendation:** (1) Use exact process name matching where possible. (2) Add a known-good exclusion list. (3) Document the false positive risk in reports.

---

**L-3. PlatformDetector Spawns PowerShell for Disk Type Detection (PlatformDetector.cs, lines 143-220)**

- **File:** `Services/PlatformDetector.cs`
- **Description:** Three separate PowerShell processes are spawned to detect disk media types. This is inconsistent with the "no WMI, no PowerShell" design goal stated in CLAUDE.md.
- **Risk:** No direct security risk, but PowerShell execution on an endpoint may trigger EDR alerts or be blocked by AppLocker/WDAC policies, causing the agent to fail silently.
- **Recommendation:** Use the Storage Management WMI provider via .NET's `ManagementObjectSearcher` or P/Invoke `DeviceIoControl` with `IOCTL_STORAGE_QUERY_PROPERTY` to avoid PowerShell.

---

**L-4. Password Entered via Custom Console ReadKey Loop (NetworkScanner.cs, lines 809-832)**

- **File:** `Services/NetworkScanner.cs`
- **Description:** `ReadMaskedPassword()` collects the password character by character into a `StringBuilder`. Strings in .NET are immutable and persist in memory until garbage collected. The password string returned by this method cannot be securely wiped.
- **Recommendation:** Use `SecureString` (despite its deprecation, it is still better than a regular string for short-lived passwords) or at minimum, overwrite the `StringBuilder` buffer after use.

---

**L-5. No Certificate Revocation Checking (PinnedHttpHandler.cs)**

- **File:** `Services/PinnedHttpHandler.cs`
- **Description:** The handler validates the TLS chain via `SslPolicyErrors` but does not explicitly enable CRL/OCSP checking. .NET's default behavior varies by platform and may not check revocation.
- **Recommendation:** Explicitly set `CheckCertificateRevocationList = true` on the handler if revocation checking is desired.

---

**L-6. Software Inventory Enumerates All Installed Software (SoftwareInventory.cs)**

- **File:** `Services/SoftwareInventory.cs`
- **Description:** The full software inventory (name, version, publisher) is transmitted to the API server. This is inherent to the product's purpose but represents a privacy consideration.
- **Risk:** Software inventory data can reveal security tools in use, development tools, personal applications, and potentially sensitive business applications.
- **Recommendation:** Document this data collection in the MSP's privacy policy and client agreements. Consider allowing customers to exclude specific software from reporting.

---

**L-7. AES Key Material Briefly Lives on Heap (SecurityService.cs, line 106)**

- **File:** `Services/SecurityService.cs`
- **Description:** `aesKey.ToArray()` on line 106 copies the stack-allocated key to a heap-allocated byte array for the RSA encrypt call. This heap copy is not explicitly zeroed.
- **Risk:** The AES key material persists in heap memory until garbage collected. This is a minor issue given that the process lifetime is short.
- **Recommendation:** Wrap the `ToArray()` result in a try/finally that calls `CryptographicOperations.ZeroMemory` on the copy as well.

---

**L-8. Unused CryptoService.cs File Referenced in CLAUDE.md**

- **File:** CLAUDE.md references `Services/CryptoService.cs` but the actual implementation is in `Services/SecurityService.cs`.
- **Description:** Either CryptoService.cs was renamed or there are two files. The CLAUDE.md calls it "dormant." The live implementation is SecurityService.cs which IS wired into ApiClient.
- **Recommendation:** Clean up documentation to match actual file names.

---

### POSITIVE OBSERVATIONS

1. **Crypto implementation is solid.** RSA-OAEP-SHA256 + AES-256-GCM is the correct modern choice. No PKCS#1 v1.5. Key size enforcement (min 2048-bit). Nonce is randomly generated per envelope. GCM tag length is 128-bit. Stack-allocated key with explicit zeroing. This is well-implemented.

2. **ShellEngine allowlist exists.** While the allowlist is too broad (all of System32), the fact that arbitrary paths are rejected is a significant defense layer. The `ResolveExecutable` function correctly validates both rooted and relative paths.

3. **ProcessHelper is well-designed.** The deadlock-free process execution with `BeginOutputReadLine/BeginErrorReadLine`, bounded output capture, hard timeout, and process-tree kill is correct and robust. This is better than what most agent software implements.

4. **HMAC implementation is correct.** The signing string includes timestamp, method, path, and body hash. The timestamp enables replay window enforcement on the server side. HMACSHA256 is used correctly with proper key encoding.

5. **Hardware fingerprint design is thoughtful.** Using MachineGuid + BIOS serial + baseboard info provides reasonable stability and uniqueness. The salt allows coordinated rotation. The fallback to machine name prevents hard failures. Hashing before transmission avoids PII leakage.

6. **Patched binary cleanup on launch.** When `EmbeddedConfig.IsPatched` is true, the agent wipes previous enrollment data from the registry (Program.cs line 118). This prevents stale credential reuse when deploying to multiple machines.

7. **SPKI pinning architecture is correct.** Multi-pin support for rotation, log-only mode for initial deployment, proper SPKI hash computation (SHA-256 of SubjectPublicKeyInfo DER), chain validation still enforced alongside pinning. The design is right; only enforcement timing is the issue.

8. **Envelope encryption is defense-in-depth.** Even if HMAC is somehow bypassed, the payload is encrypted with the server's public key and authenticated via GCM. An attacker cannot read or modify the payload without the private key.

9. **Network scanner cleanup.** The `finally` block in `ScanSingleTarget` deletes the remote binary, disconnects `net use` sessions, and stops SMB if it was started. This is good operational hygiene.

10. **Process output is capped.** ShellEngine caps stdout at 4KB and stderr at 1KB. ProcessHelper enforces `stdoutCapBytes/stderrCapBytes`. This prevents memory exhaustion from malicious or runaway commands.

---

### SUMMARY

**Overall Risk Assessment: MEDIUM-HIGH**

The Kryoss Agent is architecturally well-thought-out with genuine defense-in-depth (HMAC + envelope encryption + SPKI pinning + hardware fingerprinting). The crypto implementation is notably good -- no common pitfalls like PKCS#1 v1.5, weak IVs, or missing authentication tags.

However, there are **three critical issues that should be addressed before this tool is used in production customer environments:**

1. **C-3 (Server-controlled command execution)** is the most dangerous finding. A compromised API server or successful MitM (trivial while SPKI pinning is in log-only mode) gives an attacker RCE as SYSTEM on every enrolled machine. The combination of C-3 + C-5 (no enforced SPKI) is an attack chain that should be considered P0.

2. **C-1 (Plaintext credential storage)** is a standard finding for Windows agents but is important for a security product. Your customers expect you to practice what you preach.

3. **C-2 (Passwords on command line)** is visible to any process on the system and will appear in security audit logs. For a security tool, this is embarrassing.

**Priority remediation order:**
1. Implement executable allowlist in ShellEngine (block LOLBins) -- **immediate**
2. Enforce SPKI pinning in production builds -- **immediate**
3. Include AgentId and Hwid in HMAC canonical string -- **next release**
4. Encrypt registry credentials with DPAPI -- **next release**
5. Address password-on-command-line for PsExec/net use -- **backlog** (limited options)
6. Encrypt offline payloads -- **backlog**

The positive: the team clearly understands the threat model (the security-baseline.md is thorough), the crypto is correct, and the architecture supports the mitigations needed. The gaps are more about "not yet enforced" than "fundamentally broken."
