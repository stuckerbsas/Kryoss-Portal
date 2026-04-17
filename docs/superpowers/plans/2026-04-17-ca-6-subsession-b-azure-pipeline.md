# CA-6 Subsession B — Azure Infrastructure Pipeline

**Parent plan:** `docs/superpowers/plans/2026-04-16-cloud-assessment-platform.md` (Phase CA-6).
**Subsession A:** Azure ARM consent flow, shipped 2026-04-17 (commits `e1144ce` → `c979fb9`).
**Branch:** `main` (matches pattern of prior CA-1..CA-5 and Subsession A).
**Execution:** `superpowers:subagent-driven-development` — one implementer subagent per task, two-stage review (spec then quality) after each.

## Goal

Make a connected Azure subscription produce an "azure" area score + findings inside a Cloud Assessment scan, alongside the existing Identity / Endpoint / Data / Productivity areas. Portal Azure tab gains a rich infrastructure view on top of the Subsession A consent UI.

## Scope

In-scope ARM surfaces (read-only, `Reader` RBAC sufficient):

- `GET /subscriptions/{id}/resources?api-version=2022-12-01` — resource inventory.
- `GET /subscriptions/{id}/providers/Microsoft.Security/assessments?api-version=2021-06-01` — Defender for Cloud posture (healthy / unhealthy / not-applicable).
- `GET /subscriptions/{id}/providers/Microsoft.Network/publicIPAddresses?api-version=2023-09-01` — public IP count.
- `GET /subscriptions/{id}/providers/Microsoft.Network/networkSecurityGroups?api-version=2023-09-01` — NSG rules, detect Any-Any allow.
- `GET /subscriptions/{id}/providers/Microsoft.Storage/storageAccounts?api-version=2023-01-01` — flag `allowBlobPublicAccess=true`, HTTPS-only off, soft-delete off.
- `GET /subscriptions/{id}/providers/Microsoft.KeyVault/vaults?api-version=2023-07-01` — check `enableSoftDelete`, `enablePurgeProtection`.
- `GET /subscriptions/{id}/providers/Microsoft.Compute/virtualMachines?api-version=2023-09-01` — VM count, disk encryption, managed identity.
- `GET /subscriptions/{id}/providers/Microsoft.PolicyInsights/policyStates/latest/summarize?api-version=2019-10-01` (optional) — policy compliance summary.

Out-of-scope (deferred): Log Analytics, Sentinel, Cost Management, Azure AD B2C, Update Manager, Backup/ASR, Container Registry. Full `CloudAssessmentReportBuilder` also deferred — none exists yet for CA, so Azure report section blocks on separate report scaffold work.

## Tasks

### B1 — SQL migration 032 + EF entity (azure resource cache)

**Files:**
- `KryossApi/sql/032_azure_resource_cache.sql` — new table `cloud_assessment_azure_resources`:
  - `id BIGINT IDENTITY PK`
  - `scan_id UNIQUEIDENTIFIER NOT NULL FK → cloud_assessment_scans(id) ON DELETE CASCADE`
  - `subscription_id VARCHAR(64) NOT NULL`
  - `resource_type VARCHAR(200) NOT NULL` — e.g. `Microsoft.Storage/storageAccounts`
  - `resource_id NVARCHAR(500) NOT NULL` — full ARM resource ID
  - `name NVARCHAR(200) NULL`
  - `location VARCHAR(50) NULL`
  - `kind NVARCHAR(100) NULL`
  - `properties_json NVARCHAR(MAX) NULL` — raw slice needed for recommendations (storage flags, KV properties, NSG rules)
  - `risk_flags NVARCHAR(MAX) NULL` — JSON array of detected issues (e.g. `["public_blob","http_enabled"]`)
  - `created_at DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME()`
  - Indexes: `ix_car_scan (scan_id, resource_type)`, `ix_car_subscription (scan_id, subscription_id)`.
- `KryossApi/src/KryossApi/Data/Entities/CloudAssessmentAzureResource.cs` — new entity matching columns above (snake_case via `HasColumnName`).
- `KryossApi/src/KryossApi/Data/KryossDbContext.cs` — register `DbSet<CloudAssessmentAzureResource> CloudAssessmentAzureResources`, add `OnModelCreating` config mirroring other `cloud_assessment_*` entries.

**Idempotency:** use `IF OBJECT_ID(...) IS NULL` guard on the CREATE TABLE; the file must be safe to re-run.

**Acceptance:** `dotnet build` passes, migration runnable against `KryossDb`, new DbSet resolves.

### B2 — AzurePipeline.cs + AzureInsights.cs core

**Files:**
- `KryossApi/src/KryossApi/Services/CloudAssessment/Pipelines/AzureInsights.cs` — pre-computed metric bag mirroring `EndpointInsights` style. Fields include:
  - Counters: `SubscriptionsScanned`, `ResourcesTotal`, resource counts per type (VMs, storage accounts, key vaults, NSGs, public IPs, databases).
  - Defender for Cloud: `AssessmentsHealthy`, `AssessmentsUnhealthy`, `AssessmentsNotApplicable`, `SecureScorePct` (when exposed).
  - Public exposure: `StorageAccountsPublicBlob`, `StorageAccountsHttpEnabled`, `PublicIpCount`, `NsgAnyAnyAllowRules`.
  - Key Vault: `KeyVaultsNoSoftDelete`, `KeyVaultsNoPurgeProtection`.
  - VMs: `VmsUnencryptedOsDisk`, `VmsWithoutManagedIdentity`.
  - Policy: `PolicyNonCompliantResources` (optional).
  - Per-subscription breakdown list (`List<AzureSubscriptionInsight>`) for reporting.
- `KryossApi/src/KryossApi/Services/CloudAssessment/Pipelines/AzurePipeline.cs` — static `RunAsync` following `EndpointPipeline` shape:
  - Signature: `public static async Task<PipelineResult> RunAsync(HttpClient armHttp, IReadOnlyList<string> subscriptionIds, Guid scanId, KryossDbContext db, ILogger log, CancellationToken ct)`.
  - If `armHttp` is null or `subscriptionIds` is empty → return `PipelineResult { Status = "skipped" }` with empty findings/metrics (no throw).
  - Parallel collectors per subscription via `Task.WhenAll`. Each collector catches own exceptions and logs via shared `CollectorErrorTracker` — 403 is non-fatal.
  - Collectors: `CollectResources`, `CollectDefenderAssessments`, `CollectPublicIps`, `CollectNsgs`, `CollectStorageAccounts`, `CollectKeyVaults`, `CollectVirtualMachines`, optional `CollectPolicyCompliance`.
  - After collection: build `AzureInsights`, call `AzureRecommendations.Generate(ins)` (Task B3), populate snake_case metrics dict mirroring Endpoint pattern.
  - Write per-resource rows to `cloud_assessment_azure_resources` in batches (flag risky ones via `risk_flags` JSON).
- Reuse existing `PipelineResult`, `CollectorErrorTracker`, `RecommendationResult` (from `CopilotReadiness.Pipelines` / `CopilotReadiness.Recommendations`).

**Notes for implementer:**
- ARM HttpClient pattern is already in `AzureConsentFunction.Verify` (lines 189-232 `AzureConsentFunction.cs`). Reuse `ClientSecretCredential` + `https://management.azure.com/.default` scope. Service constructor (Task B4) builds this once per scan, injects into pipeline.
- Paginate: ARM returns `nextLink`. Follow up to 20 pages per collector to bound cost.
- Do NOT call Graph from this pipeline (that's identity's job).

**Acceptance:** pipeline compiles, unit-safe (no DB side-effects when called with null/empty subs), metrics dictionary uses `snake_case` keys.

### B3 — AzureRecommendations.cs (≥20 checks)

**File:** `KryossApi/src/KryossApi/Services/CloudAssessment/Recommendations/AzureRecommendations.cs`

Mirror `EndpointRecommendations.cs`: static `Generate(AzureInsights ins)` returning `List<RecommendationResult>`. Services: `arm` / `defender-cloud` / `storage` / `keyvault` / `network` / `compute` / `policy`.

Required checks (add or adjust; aim ≥20 findings generated from collected data):

1. **Defender for Cloud not enabled** — when `AssessmentsHealthy + AssessmentsUnhealthy = 0` → Action Required, `defender-cloud`.
2. **High unhealthy ratio** — `Unhealthy / (Healthy+Unhealthy) > 0.3` → Action Required, `defender-cloud`.
3. **Secure Score low** — `SecureScorePct < 50` → Warning.
4. **Storage accounts allow public blob** — `StorageAccountsPublicBlob > 0` → Action Required, `storage`.
5. **Storage accounts HTTP enabled** — `StorageAccountsHttpEnabled > 0` → Action Required, `storage`.
6. **Storage soft-delete disabled** — derived from `properties_json` flag → Warning.
7. **Key Vault soft-delete disabled** — Action Required, `keyvault`.
8. **Key Vault purge protection disabled** — Warning, `keyvault`.
9. **Public IP sprawl** — `PublicIpCount > 5` → Warning, `network`.
10. **NSG Any-Any allow rules** — Action Required, `network`.
11. **VM unencrypted OS disk** — Action Required, `compute`.
12. **VM without Managed Identity** — Warning, `compute`.
13. **No subscriptions connected** — Informational / Insight (defensive; skipped if subs list empty but pipeline was called).
14. **Policy non-compliant resources > 0** — Warning, `policy` (only if policy collector succeeded).
15. **Resource sprawl** — `ResourcesTotal > 500 per subscription` → Informational.
16. **Mixed regions** — locations span > 3 regions without apparent DR plan → Informational.
17. **Multiple subscriptions, no naming convention** — subscription display name heuristic → Informational.
18. **Stale resource groups** — (defer if not collected; skip cleanly).
19. **No Defender for Cloud plans on Storage/KeyVault/SQL** — if Defender assessments include plan status → Warning.
20. **Success check** — when all posture gates pass, emit "Success" `defender-cloud` finding (mirrors Endpoint pattern).

Text rules: observation states *what was found*, recommendation states *what to do*, `linkUrl` points to learn.microsoft.com. Priority: `critical` for public data exposure, `high` for Defender/KV, `medium` for NSG/VM, `low` for informational. English strings only (bilingual renders downstream).

**Acceptance:** static class compiles, unit-callable with a synthetic `AzureInsights`, produces expected finding count + severities.

### B4 — Wire AzurePipeline into orchestrator + 5-area scoring

**Files:**
- `KryossApi/src/KryossApi/Services/CloudAssessment/CloudAssessmentService.cs`:
  - In `RunScanInternalAsync` (after existing `dataTask`/`productivityTask` declarations): acquire ARM HttpClient via `TryCreateAuthenticatedClient(credential, "https://management.azure.com/.default", "https://management.azure.com", log)`.
  - Load connected subscriptions: `var subs = await db.CloudAssessmentAzureSubscriptions.Where(s => s.OrganizationId == scanOrgId && s.ConsentState == "connected").Select(s => s.SubscriptionId).ToListAsync(ct);`
  - Persist the JSON array onto `scan.AzureSubscriptionIds`.
  - Dispatch `azureTask = TrackPipeline("azure", () => AzurePipeline.RunAsync(armHttp, subs, scanId, db, log, ct), scanId, db, log);` — include in `Task.WhenAll`.
  - Collect result, add findings with `AddFindings("azure", azureResult.Findings)`, add metrics with `AddMetrics(allMetrics, "azure", azureResult.Metrics)`.
  - New `ComputeAzureAreaScore(Dictionary<string,string> metrics, List<RecommendationResult> findings)` using same shape as `ComputeEndpointAreaScore`:
    - Start at 5.0, deduct 0.15 per `action_required` (cap 2.5), 0.07 per `warning` (cap 1.0).
    - Metric-driven penalties: public blob > 0 → -0.5, NSG Any-Any > 0 → -0.4, unhealthy ratio > 30% → -0.4, secure score < 50 → -0.3, KV no soft-delete > 0 → -0.3, unencrypted VM OS disk > 0 → -0.3. Clamp + round.
  - Extend `areaScores` JSON serialization to include `["azure"] = azureScore`.
  - Extend `pipelineStatus` JSON to include `["azure"] = azureResult.Status`.
  - Revise overall weighted average to 5-area:
    - identity 28%, data 25%, endpoint 22%, azure 15%, productivity 10%.
    - When `azureScore` is skipped (no connected subs), redistribute its weight proportionally across the other four (document this in a comment).
  - Extend `CompareScansAsync` `deltas` dict to include `"azure"`.
- `KryossApi/src/KryossApi/Services/CloudAssessment/ICloudAssessmentService.cs`: no signature change.

**Guardrails:**
- When 0 connected subs or ARM token acquisition fails → `azureTask` returns `status=skipped`, no findings, no metrics. The scan must still complete and the other four areas must still score correctly.
- Do not break the existing 4-area scan for orgs that have not gone through Subsession A yet.

**Acceptance:** end-to-end scan with a connected test subscription populates `area = 'azure'` rows in `cloud_assessment_findings`, non-empty `cloud_assessment_azure_resources`, and `areaScores` JSON contains `"azure"` key. Scan without connected subs behaves like today (no regression).

### B5 — Portal Azure Infrastructure UI

**Files:**
- `KryossPortal/src/components/cloud-assessment/AzureInfrastructureView.tsx` — new component rendered by `AzureTab` in `CloudAssessmentPage.tsx` when `subs.length > 0` AND `summary.areaScores?.azure` exists:
  - Header: subscription count KPI, area score badge, last-scan timestamp.
  - Resources card: donut chart (recharts) of resource type breakdown from `detail.metrics` (`azure_*_count` keys) — reuse style of existing Overview charts.
  - Defender for Cloud card: healthy / unhealthy / not-applicable bar split (from metrics `assessments_healthy`, etc).
  - Public exposure alert card: 3 red callouts if `storage_public_blob > 0`, `nsg_any_any_allow > 0`, or `keyvaults_no_soft_delete > 0`.
  - Findings table: filter `detail.findings` where `area === 'azure'`, reuse `AreaFindingsTab` styling (status + priority badges, observation/recommendation columns, link-out).
- `KryossPortal/src/components/cloud-assessment/CloudAssessmentPage.tsx` — `AzureTab` switches: if no subs → `ConnectAzureCard`; if subs but no scan yet → existing `AzureSubscriptionsList` + "Run scan" hint; if subs + scan → `AzureInfrastructureView` with subscriptions list collapsed below.
- `KryossPortal/src/components/cloud-assessment/OverviewTab.tsx`:
  - `AREAS` array: add `{ key: 'azure', label: 'Azure', icon: <pick Cloud icon from lucide> }`.
  - `AREA_LABELS`: add `azure: 'Azure'`.
  - `radarData`: already derived from `AREAS`, will pick up Azure automatically once the entry exists.
- `KryossPortal/src/api/cloudAssessment.ts`: extend the `areaScores` type to include optional `azure: number`. No new endpoint needed — data comes from `/v2/cloud-assessment/{scanId}` metrics + findings.

**Acceptance:** org with no Azure subs → existing flow unchanged; org with subs but no scan → instruction card; org with subs + completed scan → Azure area radar segment + Azure tab rich view with findings and exposure alerts. Radar remains functional for orgs without Azure (renders zero or hides segment — verify behaviour on first scan).

### B6 — Finishing work (radar polish + CLAUDE.md updates)

**Files:**
- `KryossPortal/src/components/cloud-assessment/OverviewTab.tsx` — verify 5-area radar renders without visual clipping; adjust grid columns on area-score card row if 4-to-5 break layout.
- `CLAUDE.md` (root) — add entry to the decision log: `2026-04-17 | CA-6 Subsession B shipped — AzurePipeline + AzureRecommendations + 5-area scoring + portal infra view`. One line.
- `KryossApi/CLAUDE.md` — if the backend map lists CA endpoints or pipelines, append a line for `AzurePipeline.cs` under `Services/CloudAssessment/Pipelines/`.

**Out of scope (explicit):** `CloudAssessmentReportBuilder` + `/v2/reports/org/{id}?type=cloud-assessment` route. There is no CA report builder yet; adding one requires a dedicated session and is not unlocked by Subsession B.

**Acceptance:** dev preview for portal Cloud Assessment page shows 5 radar spokes on a scan that includes Azure data, no layout regressions on 4-area scans.

## Per-task dispatch order

1. B1 (migration + entity) — must ship before B2 compiles cleanly.
2. B2 (pipeline + insights) — must ship before B3 types resolve.
3. B3 (recommendations).
4. B4 (orchestrator wiring + scoring) — depends on B1/B2/B3.
5. B5 (portal UI) — depends on B4 because it reads the `azure` area from API responses.
6. B6 (polish + docs).

Final code review after B6 covers the full Subsession B slice.

## Verification checklist (for reviewer agents)

- `dotnet build` succeeds on `KryossApi.csproj`.
- No new `Process.Start`, no reflection in new pipeline code.
- Migration file is idempotent and follows existing numbering (032).
- `AzureRecommendations.Generate(new AzureInsights())` returns a finite list without throwing on empty inputs.
- Orchestrator still completes scans for orgs with zero connected Azure subs (regression test in review).
- Portal renders cleanly with and without `azure` area score present in response (both code paths).
