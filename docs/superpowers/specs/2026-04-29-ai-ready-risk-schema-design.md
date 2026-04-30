# AI-Ready Risk Schema Design

**Date:** 2026-04-29
**Author:** Claude (Data Architect role)
**Status:** Draft — pending Federico review

---

## 1. Target AI Use Cases (Future)

These are the concrete questions the system should answer, ordered by implementation priority:

### Tier 1 — Rule-based (implementable now)
1. **"Which CVEs should be fixed first?"** — Risk-ranked list factoring CVSS, KEV status, exposure, asset criticality, and patch availability.
2. **"Is this machine safe to remediate right now?"** — Pre-flight check: is it a DC? Running critical services? Business hours? Pending reboot?
3. **"What's the blast radius if this CVE is exploited?"** — How many machines, which roles, which orgs, is it externally reachable?
4. **"Is this risk acceptable for this customer?"** — Compare org's risk tolerance profile against finding severity.

### Tier 2 — Statistical/ML (future)
5. **"Can this remediation be automated safely?"** — Based on historical success rate for this action type + machine profile.
6. **"Will this patch break anything?"** — Based on similar machine profiles where the patch was applied.
7. **"Which machines will likely fail next month's compliance?"** — Trend prediction from score drift.

### Tier 3 — LLM reasoning (later)
8. **"Summarize this org's risk posture for the C-level report."** — Natural language synthesis across all data.
9. **"Recommend a 90-day remediation roadmap for this org."** — Sequenced plan factoring dependencies, risk, and effort.
10. **"Explain why this remediation was auto-approved."** — Audit trail in natural language.

---

## 2. Core AI Feature Table Design

### Table: `risk_features`

One row = one risk instance (one CVE finding on one machine at one point in time).

Materialized on every CVE scan (`CveService.ScanMachineAsync`). Rebuilt per machine, not incremental — drop + recreate per machine on each scan (same pattern as `machine_cve_findings`).

| Column | Type | Description | AI Relevance |
|--------|------|-------------|--------------|
| **Identity** | | | |
| `id` | `BIGINT IDENTITY` | PK | — |
| `machine_cve_finding_id` | `INT` | FK → `machine_cve_findings.id` | Links to source finding |
| `cve_id` | `VARCHAR(20)` | e.g. CVE-2025-1234 | Dedup + lookup |
| `machine_id` | `UNIQUEIDENTIFIER` | FK → `machines.id` | Asset identity |
| `organization_id` | `UNIQUEIDENTIFIER` | FK → `organizations.id` | Tenant scoping + RLS |
| `run_id` | `UNIQUEIDENTIFIER NULL` | Assessment run that produced this | Temporal anchoring |
| `computed_at` | `DATETIME2` | When features were calculated | Staleness detection |
| **Technical Risk** | | | |
| `cvss_score` | `DECIMAL(3,1)` | Raw CVSS 3.x score (0-10) | Primary severity signal |
| `severity` | `VARCHAR(10)` | critical/high/medium/low | Categorical severity |
| `is_known_exploited` | `BIT` | CISA KEV list member | Massive risk multiplier — actively exploited in the wild |
| `kev_due_date` | `DATE NULL` | CISA mandated remediation deadline | Regulatory urgency |
| `has_public_exploit` | `BIT` | Known exploit code available | Exploitability signal |
| `product_class` | `VARCHAR(20)` | OS/PLATFORM/APPLICATION/LIBRARY | Attack surface category |
| `cwe_id` | `VARCHAR(20) NULL` | Weakness type (e.g. CWE-79) | Vulnerability class clustering |
| **Environmental Context** | | | |
| `asset_role` | `VARCHAR(20)` | workstation/server/domain_controller | From `machines.product_type`: 1=WS, 2=DC, 3=Server |
| `is_domain_controller` | `BIT` | Machine is a DC | DCs = crown jewels, highest blast radius |
| `is_internet_facing` | `BIT` | Has open ports visible externally | From `external_scan_results` — externally reachable = critical |
| `open_port_count` | `SMALLINT` | External ports open on this machine | Attack surface breadth |
| `domain_status` | `VARCHAR(20)` | domain-joined / workgroup / azure-ad | Lateral movement potential |
| `is_server_os` | `BIT` | Running Server OS | Server = higher impact than workstation |
| `has_local_admins_risk` | `BIT` | Non-default local admins present | Privilege escalation path |
| `local_admin_count` | `SMALLINT` | Number of local admin accounts | Excessive admins = higher risk |
| `compliance_score` | `DECIMAL(5,2) NULL` | Latest machine compliance score (0-100) | Overall security posture of this asset |
| `compliance_grade` | `CHAR(1) NULL` | A/B/C/D/F | Quick categorical posture |
| `org_machine_count` | `SMALLINT` | Total machines in this org | Blast radius denominator |
| `org_affected_count` | `SMALLINT` | Machines in org with same CVE | Blast radius numerator — widespread = systemic |
| **Software Context** | | | |
| `software_name` | `NVARCHAR(256)` | Affected software display name | Human-readable identification |
| `software_publisher` | `NVARCHAR(256) NULL` | Software publisher | Vendor trust signal |
| `installed_version` | `NVARCHAR(50) NULL` | Currently installed version | Version gap calculation |
| `fixed_version` | `NVARCHAR(50) NULL` | Version that fixes the CVE | Patch availability signal |
| `is_eol_software` | `BIT` | Software is end-of-life | EOL = no more patches, must uninstall/upgrade |
| `cpe_vendor` | `VARCHAR(100) NULL` | CPE vendor slug | Machine-matching key |
| `cpe_product` | `VARCHAR(100) NULL` | CPE product slug | Machine-matching key |
| **Remediation Capability** | | | |
| `has_patch_available` | `BIT` | `fixed_version IS NOT NULL` | Can we fix this with a patch? |
| `remediation_type` | `VARCHAR(20)` | patch/config/uninstall/upgrade/mitigate/none | What action is needed |
| `remediation_risk` | `VARCHAR(10)` | low/medium/high | Risk of the fix itself breaking things |
| `is_auto_remediable` | `BIT` | Can be fixed without human approval | Automation eligibility |
| `has_prior_remediation` | `BIT` | Remediation was attempted before | History signal |
| `prior_remediation_success` | `BIT NULL` | Did prior attempt succeed? | Confidence in automation |
| `prior_remediation_count` | `SMALLINT` | Total prior attempts | Repeated failure = needs human |
| **Temporal Risk** | | | |
| `first_seen_at` | `DATETIME2` | When CVE first detected on this machine | Dwell time start |
| `days_exposed` | `INT` | Days since first detection | Dwell time — longer = higher risk |
| `days_since_published` | `INT NULL` | Days since CVE was published | Public knowledge window |
| `days_until_kev_due` | `INT NULL` | Days until CISA KEV deadline | Regulatory countdown |
| `patch_lag_days` | `INT NULL` | Days since patch was available but not applied | Negligence signal |
| **Machine Health** | | | |
| `tpm_present` | `BIT NULL` | TPM chip present | Hardware security baseline |
| `secure_boot` | `BIT NULL` | Secure Boot enabled | Boot integrity |
| `bitlocker` | `BIT NULL` | BitLocker enabled | Data-at-rest protection |
| `last_boot_days` | `INT NULL` | Days since last reboot | Stale machines = unpatched |
| `system_age_days` | `INT NULL` | Machine age in days | Old machines = more risk |
| `wu_service_ok` | `BIT NULL` | Windows Update service running | Can patches be applied? |
| `patch_compliance_score` | `INT NULL` | Patch compliance score (0-100) | Overall patch health |
| **Derived Scores** | | | |
| `risk_score` | `DECIMAL(5,2)` | Composite risk score (0-100) | Primary ranking signal |
| `priority_rank` | `INT` | 1-based rank within org | "Fix this first" ordering |
| `confidence` | `DECIMAL(3,2)` | Score confidence (0.00-1.00) | How much data backed this score |
| `risk_factors` | `VARCHAR(500)` | Pipe-delimited factor list | Explainability: `KEV|DC|EXTERNAL|EOL` |

### Supporting Table: `risk_decisions`

Audit log for every automated or human decision. Future training labels.

| Column | Type | Description |
|--------|------|-------------|
| `id` | `BIGINT IDENTITY` | PK |
| `risk_feature_id` | `BIGINT` | FK → `risk_features.id` |
| `decision_type` | `VARCHAR(30)` | auto_remediate / approve / dismiss / defer / escalate |
| `decision_source` | `VARCHAR(20)` | rule_engine / human / ml_model |
| `decision_reason` | `VARCHAR(500)` | Human-readable explanation |
| `rule_id` | `VARCHAR(50) NULL` | Which rule triggered this (e.g. `RULE-KEV-DC-01`) |
| `decided_by` | `UNIQUEIDENTIFIER NULL` | User ID (if human) |
| `decided_at` | `DATETIME2` | When |
| `outcome` | `VARCHAR(20) NULL` | success / failed / rolled_back / pending |
| `outcome_at` | `DATETIME2 NULL` | When outcome was recorded |

### Supporting Table: `org_risk_profile`

Per-org risk tolerance and automation preferences. Feeds environmental context.

| Column | Type | Description |
|--------|------|-------------|
| `organization_id` | `UNIQUEIDENTIFIER` | PK, FK → `organizations.id` |
| `risk_tolerance` | `VARCHAR(10)` | conservative / moderate / aggressive |
| `auto_remediate_low` | `BIT` | Auto-fix low-risk findings |
| `auto_remediate_medium` | `BIT` | Auto-fix medium-risk findings |
| `auto_remediate_high` | `BIT` | Auto-fix high-risk (never auto for critical) |
| `maintenance_window_start` | `TIME NULL` | Preferred maintenance window start (UTC) |
| `maintenance_window_end` | `TIME NULL` | Preferred maintenance window end (UTC) |
| `maintenance_window_days` | `VARCHAR(20) NULL` | e.g. "1,2,3,4,5" (Mon-Fri) |
| `max_concurrent_remediations` | `INT` | Max parallel remediations per org |
| `modified_by` | `UNIQUEIDENTIFIER NULL` | |
| `modified_at` | `DATETIME2 NULL` | |

---

## 3. Feature Categorization

### Group A — Technical Risk
**Columns:** `cvss_score`, `severity`, `is_known_exploited`, `has_public_exploit`, `product_class`, `cwe_id`, `kev_due_date`

**Contribution:** Intrinsic severity of the vulnerability. Independent of where it lives. A CVSS 9.8 KEV-listed RCE is dangerous regardless of context.

**Weight in scoring:** Base score. Multiplied by environmental factors.

### Group B — Environmental / Contextual Risk
**Columns:** `asset_role`, `is_domain_controller`, `is_internet_facing`, `open_port_count`, `domain_status`, `has_local_admins_risk`, `compliance_score`, `org_machine_count`, `org_affected_count`

**Contribution:** "Where does this vulnerability live?" A medium CVSS on an internet-facing DC is worse than a critical CVSS on an airgapped workstation. Blast radius estimation.

**Weight in scoring:** Multiplier on base score. DC + external = 2x. Workgroup workstation = 0.7x.

### Group C — Operational / Remediation Risk
**Columns:** `has_patch_available`, `remediation_type`, `remediation_risk`, `is_auto_remediable`, `has_prior_remediation`, `prior_remediation_success`, `prior_remediation_count`, `is_eol_software`, `wu_service_ok`

**Contribution:** "Can we fix this safely?" A vuln with no patch is higher priority for mitigation. A vuln with 3 failed remediations needs a human. EOL software needs uninstall, not patch.

**Weight in scoring:** Affects priority ranking and automation eligibility. Does NOT increase risk score (risk exists regardless of fixability) but increases urgency when fixable.

### Group D — Temporal Risk
**Columns:** `first_seen_at`, `days_exposed`, `days_since_published`, `days_until_kev_due`, `patch_lag_days`, `last_boot_days`

**Contribution:** "How long has this been a problem?" Dwell time correlates with exploitation probability. KEV deadlines create hard urgency. Patch lag = negligence signal for compliance.

**Weight in scoring:** Time multiplier. Each 30 days of exposure increases risk score by ~10% (configurable).

---

## 4. Data Flow & Ownership

### Agent-sourced (arrive via `/v1/results` or `/v1/heartbeat`)
| Feature | Source Table | Ingest Path |
|---------|-------------|-------------|
| `installed_version` | `machine_software` | `EvaluationService.EvaluateAsync` |
| `asset_role` / `is_domain_controller` | `machines.product_type` | `EvaluationService.EvaluateAsync` |
| `tpm_present`, `secure_boot`, `bitlocker` | `machines` | `EvaluationService.EvaluateAsync` |
| `last_boot_days` | `machines.last_boot_at` | `EvaluationService.EvaluateAsync` |
| `domain_status` | `machines.domain_status` | `EvaluationService.EvaluateAsync` |
| `local_admin_count` | `machine_local_admins` | `EvaluationService.EvaluateAsync` |
| `compliance_score` | `machines.latest_score` | `EvaluationService.EvaluateAsync` |
| `wu_service_ok` | `machine_patch_status` | `EvaluationService.EvaluateAsync` |
| `patch_compliance_score` | `machine_patch_status.compliance_score` | `EvaluationService.EvaluateAsync` |

### Backend-derived (calculated at scan time)
| Feature | Derived From | Calculation Point |
|---------|-------------|-------------------|
| `is_internet_facing`, `open_port_count` | `external_scan_results` | `CveService.ScanMachineAsync` (query latest external scan) |
| `has_local_admins_risk` | `machine_local_admins` count > 1 | `CveService.ScanMachineAsync` |
| `is_eol_software` | `software.is_eol` | `CveService.ScanMachineAsync` (from Software entity) |
| `has_patch_available` | `fixed_version IS NOT NULL` | `CveService.ScanMachineAsync` |
| `remediation_type` | Heuristic from product_class + patch availability | `CveService.ScanMachineAsync` |
| `org_affected_count` | Count machines in org with same CVE | `CveService.ScanMachineAsync` |

### Periodically recalculated (background job)
| Feature | Frequency | Why |
|---------|-----------|-----|
| `days_exposed` | Every CVE scan (derived from `first_seen_at`) | Changes daily |
| `days_since_published` | Every CVE scan (from `cve_entries.published_at`) | Changes daily |
| `days_until_kev_due` | Every CVE scan (from `cve_entries.kev_due_date`) | Changes daily |
| `patch_lag_days` | Every CVE scan | Changes daily |
| `risk_score`, `priority_rank` | Every CVE scan | Recomputed per machine |
| `org_machine_count` | Every CVE scan | Org fleet can change |

### Data flow diagram
```
Agent scan
  → /v1/results
    → EvaluationService.EvaluateAsync
      → Normalizes: software, patches, hardware, local admins
      → Saves machines, machine_software, etc.
      → Calls CveService.ScanMachineAsync
        → Rebuilds machine_cve_findings
        → Builds risk_features (one row per finding)
          → Queries: machines, machine_local_admins, external_scans,
                     machine_patch_status, software
          → Calculates: derived features, risk_score, priority_rank
          → INSERT into risk_features
```

---

## 5. Scoring Strategy (Rule-Based, No ML)

### Risk Score Formula (0-100)

```
risk_score = base_score × environment_multiplier × time_multiplier
```

**Step 1 — Base Score (0-40):**
```
base = cvss_score × 4                          # CVSS 10.0 → 40 points
if is_known_exploited:     base += 25           # KEV = massive boost
if has_public_exploit:     base += 10           # Known exploit code
if product_class = 'OS':   base += 5            # OS vulns = system-wide
base = MIN(base, 60)                            # Cap base component
```

**Step 2 — Environment Multiplier (0.5 - 2.0):**
```
env = 1.0
if is_domain_controller:   env += 0.5           # Crown jewel
if is_internet_facing:     env += 0.3           # Externally reachable
if is_server_os:           env += 0.1           # Server > workstation
if has_local_admins_risk:  env += 0.1           # Priv esc path
if compliance_score < 50:  env += 0.1           # Weak overall posture
if asset_role = 'workstation' AND NOT is_internet_facing:
                           env -= 0.3           # Internal WS = lower
env = CLAMP(env, 0.5, 2.0)
```

**Step 3 — Time Multiplier (1.0 - 1.5):**
```
time = 1.0
time += MIN(days_exposed / 180, 0.3)            # Max +0.3 at 6 months
if days_until_kev_due IS NOT NULL AND days_until_kev_due <= 7:
                           time += 0.2          # KEV deadline imminent
if patch_lag_days > 30:    time += 0.1          # Patch available, not applied
time = CLAMP(time, 1.0, 1.5)
```

**Final:** `risk_score = CLAMP(base × env × time, 0, 100)` rounded to 2 decimals.

### Confidence Score (0.00 - 1.00)

How much data we have to support the risk score:

```
confidence = 0.30                               # Base: we know the CVE exists
if cvss_score IS NOT NULL:     confidence += 0.15
if installed_version IS NOT NULL: confidence += 0.15
if compliance_score IS NOT NULL:  confidence += 0.10
if last_boot_at IS NOT NULL:      confidence += 0.05
if external scan exists:          confidence += 0.10
if patch_status exists:           confidence += 0.10
if remediation history exists:    confidence += 0.05
```

### What to Store vs. Compute

| Metric | Store? | Reason |
|--------|--------|--------|
| `risk_score` | **Store** | Used for sorting/filtering in portal, avoids recompute on every list query |
| `priority_rank` | **Store** | `ROW_NUMBER() OVER (PARTITION BY organization_id ORDER BY risk_score DESC)` |
| `confidence` | **Store** | Rarely changes between scans |
| `risk_factors` | **Store** | Pipe-delimited string for instant explainability without re-deriving |
| `days_exposed` | **Store** | Cheap to compute but used in multiple places |
| Environment multiplier | **Don't store** | Intermediate calculation, derivable from stored features |

### Explainability

`risk_factors` column stores the active factors, e.g.:
```
KEV|DC|EXTERNAL|CVSS_9.8|EXPOSED_45D|NO_PATCH|EOL
```

Portal can render this as badges:
- 🔴 `KEV` — Actively exploited (CISA)
- 🏰 `DC` — Domain Controller
- 🌐 `EXTERNAL` — Internet-facing
- ⏰ `EXPOSED_45D` — Exposed for 45 days

Every score is traceable to its input features. No black boxes.

---

## 6. Future-Proofing for ML / LLM

### Training Labels (future supervised learning)
| Column | Label Type | Use |
|--------|-----------|-----|
| `risk_decisions.decision_type` | Classification target | "What should we do with this finding?" |
| `risk_decisions.outcome` | Outcome label | "Did the decision work?" |
| `risk_decisions.decision_source` = `'human'` | Gold labels | Human decisions are ground truth for training |
| `risk_score` (rule-based) | Weak label | Bootstrap model before enough human labels exist |

### Input Features (what the model sees)
All columns in `risk_features` except `risk_score`, `priority_rank`, `confidence` (those are current model outputs). When ML replaces rule-based scoring, the same feature columns feed the model — only the scoring function changes.

### Schema Stability
- **Feature columns are additive.** Adding a column never breaks existing models — old models ignore new columns.
- **Scoring logic lives in code, not schema.** Changing the formula doesn't require migration. The formula version can be stored in `risk_features.scoring_version` (VARCHAR(10)) if needed.
- **`risk_decisions` captures every decision regardless of source.** Switching from rules → ML → LLM just changes `decision_source`. The audit trail is continuous.

### Decision Logging for Supervised Learning
```
1. Rule engine decides: "auto-remediate CVE-2025-1234 on MACHINE-A"
2. INSERT risk_decisions: decision_type=auto_remediate, source=rule_engine, rule_id=RULE-KEV-PATCH-01
3. Agent executes remediation
4. TaskResult reports: success
5. UPDATE risk_decisions: outcome=success, outcome_at=now
```

After 6-12 months of logging, you have labeled training data:
- Feature vector (from `risk_features`)
- Decision (from `risk_decisions.decision_type`)
- Outcome (from `risk_decisions.outcome`)

This is a supervised classification dataset. No schema changes needed to start training.

### LLM Integration Path
- `risk_features` row → structured context for LLM prompt
- `risk_factors` → natural language seed ("This is a KEV-listed vulnerability on a domain controller exposed to the internet")
- `risk_decisions` history → few-shot examples for the LLM
- The LLM doesn't need to query the DB — the feature row IS the context window

---

## 7. Explicit Non-Goals

| Out of scope | Why |
|-------------|-----|
| Model training | This spec is schema only. Training requires labeled data that doesn't exist yet. |
| LLM prompt design | Prompts depend on the feature schema, which this defines. Prompts come later. |
| Automation execution | `risk_decisions` logs decisions. Execution stays in `RemediationTask` pipeline. |
| UI changes | Portal can consume `risk_features` via existing patterns. No new UI in this spec. |
| Real-time scoring | Features are materialized per scan, not streaming. Good enough for MSP 24h scan cycles. |
| Multi-tenant ML | Each org has different risk profiles. No cross-org model training in v1. |
| CVE prediction | We score known CVEs, not predict unknown ones. |

---

## 8. Implementation Notes

### Migration
- `risk_features`: ~50 columns, indexed on `(organization_id, risk_score DESC)` and `(machine_id)`
- `risk_decisions`: append-only audit log, indexed on `(risk_feature_id)` and `(decided_at DESC)`
- `org_risk_profile`: one row per org, no index needed beyond PK
- Estimated storage: ~500 bytes/row × 100 CVE findings/machine × 1000 machines = ~50 MB. Negligible.

### Integration Point
`CveService.ScanMachineAsync` already rebuilds `machine_cve_findings` per machine. The `risk_features` materialization hooks into the same method — after findings are created, compute features and insert.

### RLS
`risk_features` includes `organization_id` — same `SESSION_CONTEXT` RLS pattern as all other tables.

---

## 9. Dependency on Existing Data

| Feature | Depends On | Status |
|---------|-----------|--------|
| CVE findings | `machine_cve_findings` | ✅ Working (1.38.0) |
| Software catalog | `software` + `machine_software` | ✅ Working (1.38.0) |
| Machine hardware | `machines` columns | ✅ Working |
| Local admins | `machine_local_admins` | ✅ Working |
| External exposure | `external_scan_findings` | ✅ Working |
| Patch compliance | `machine_patch_status` | ✅ Working |
| Remediation history | `remediation_tasks` | ✅ Working |
| Org risk profile | `org_risk_profile` | 🆕 New table |
| Decision log | `risk_decisions` | 🆕 New table |
