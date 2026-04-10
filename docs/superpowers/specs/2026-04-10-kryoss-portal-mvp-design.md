# Kryoss Portal MVP — Design Spec

**Owner:** Federico / Geminis Computer S.A. / TeamLogic IT
**Date:** 2026-04-10
**Status:** Approved
**Phase:** Phase 1 (final deliverable)

---

## 1. Purpose

Close the last Phase 1 deliverable: a portal for HQ technicians to create client organizations, enroll Windows 10/11 machines, view audit results, and generate framework-filtered reports (NIST/CIS/HIPAA/ISO27001/PCI-DSS) without re-scanning.

The backend is the product. The portal is a frontend. Any UX decision lives in the frontend; any business decision lives in the backend. The same API can serve a mobile app, CLI, PSA integration, or Teams bot in the future.

---

## 2. Audience and Scope

### MVP users

Only HQ Geminis/TeamLogic IT: Federico + technicians, authenticated against an internal Entra ID tenant (placeholder `<TENANT_ID_MVP>` during development).

### Explicitly out of MVP (approved backlog)

- Franchisees, external clients with BYOI
- Roles Admin UI (CRUD roles + permission picker)
- Self-registration + tenant matching + approval flow
- Azure AD B2C for external clients
- Audit Log viewer (screen with filters)
- ClientLayout (no sidebar, single-org for clients)
- Assessment Profiles CRUD
- HMAC credential migration from org-level to device-level
- Azure SQL Ledger Tables for actlog tamper-evidence

---

## 3. Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18 + Vite + TypeScript + React Router 6 + TanStack Query v5 + TanStack Table v8 |
| UI | shadcn/ui + Radix UI + Tailwind CSS with TLIT brand (`#008852`, `#A2C564`, `#3D4043`, Montserrat) |
| Auth | Azure Static Web Apps linked with Function App + SWA Auth (Entra ID) -- no MSAL, no token handling in frontend |
| Backend | Existing -- .NET 8 Azure Functions isolated + EF Core 8 + Azure SQL (extended, not replaced) |
| Deploy | GitHub Actions single workflow `.github/workflows/deploy-portal.yml`, push to `main` deploys SWA + Functions atomically |

---

## 4. Architecture Principles (non-negotiable)

1. **Agent is dumb, server evaluates.** The agent sends a full scan of all active controls for its platform; the portal filters by framework/type at report generation time. One scan = N possible reports without re-scanning.

2. **Single enrollment code per organization, all controls always.** No profile selection in MVP. The code carries no `assessmentId` (or a sentinel `full-scope`).

3. **Nothing is ever physically deleted.** Universal soft-delete via `AuditInterceptor` (EF Core), reinforced at SQL level with `INSTEAD OF DELETE` triggers that reject + log to actlog, `DENY DELETE/UPDATE` on the `actlog` table, and Azure SQL Database Audit as a third out-of-transaction layer.

4. **Permission-driven UI at every layer.** Sidebar, routes, tabs, buttons -- all filtered by the `permissions[]` array returned by `/v2/me`. The backend is the source of truth; the frontend only hides dead UI.

5. **Role-aware layout shell.** MVP only activates `HqLayout` (sidebar + multi-org). The shell is written with indirection so `ClientLayout` (no sidebar, single-org) is a flag flip post-MVP.

6. **Every action audits.** Automatic actlog via `ActlogMiddleware` (every HTTP request); explicit `LogAsync(entityType, entityId, oldValues, newValues)` on every mutation; new modules (`organizations`, `auth`, `recycle_bin`) added to the middleware's switch.

7. **DTOs separate from EF entities.** `ApiKey/ApiSecret` of `Organization` never appear in portal responses. Documented debt: migrate HMAC credentials from `Organization` to `Machine` before opening the portal to external clients (Phase 4+).

---

## 5. Authentication

### MVP flow (SWA Auth + Entra ID)

1. User navigates to portal URL.
2. SWA Auth redirects to `/.auth/login/aad` on Entra ID tenant `<TENANT_ID_MVP>`.
3. Login + MFA (tenant policy), callback to SWA, httpOnly session cookie set.
4. All frontend requests to `/api/v2/*` carry the cookie automatically (`credentials: "include"` default in SWA linked).
5. SWA injects `X-MS-CLIENT-PRINCIPAL` header before reaching Functions runtime.
6. `BearerAuthMiddleware` (with SWA adapter) parses principal, resolves user by `entra_oid` in `users` table.
7. Frontend never sees tokens. No MSAL. No localStorage/sessionStorage for auth.

### SWA principal adapter

The middleware detects SWA format (presence of `identityProvider` field) vs App Service EasyAuth format and routes to the appropriate parser. Both extract `oid` (objectidentifier) and `tid` (tenantid) claims.

### Bootstrap: first user = super_admin

When `users` table has zero rows, the first authenticated user is auto-created as `super_admin` with `status = 'active'`. This only fires when `COUNT(*) = 0` -- never again after the first user exists. Transactional to prevent race conditions.

### Subsequent user onboarding (MVP)

Manual: admin creates user in Entra ID, obtains their `objectId`, runs `sql/helpers/add_hq_user.sql` INSERT. Script helper provided.

### Future auth architecture (designed, deferred)

- **HQ + franchisees:** Entra ID (single `@kryoss.com` tenant)
- **External clients:** Azure AD B2C with email/password + MFA (SMS/email OTP) + "Sign in with Microsoft" + "Sign in with Google"
- **Self-registration:** User logs in, not found in DB, sees minimal form (name, surname). Backend extracts `tid` from JWT, matches against `organizations.entra_tenant_id`. Match = associate + notify org admin. No match = notify HQ admins.
- **Tenant matching field:** `Organization.EntraTenantId` (GUID) added to model in MVP, populated when creating orgs. Matching logic deferred.

---

## 6. Permission System

### Endpoint: `GET /v2/me`

New function `MeFunction.cs`. No `[RequirePermission]` -- any authenticated user can read their own profile.

Response:

```json
{
  "id": "guid",
  "email": "user@tenant.com",
  "displayName": "Name",
  "authSource": "entra",
  "lastLoginAt": "ISO8601",
  "role": { "id": 1, "code": "super_admin", "name": "Super Administrator", "isSystem": true },
  "franchise": { "id": "guid", "name": "TeamLogic IT Argentina" },
  "organization": null,
  "permissions": ["assessment:read", "organizations:write", ...]
}
```

Called once on app mount, cached with TanStack Query `staleTime: Infinity`.

### Frontend permission components

- `useMe()` -- TanStack Query hook for `/v2/me`
- `usePermissions()` -- derives `has(slug)`, `hasAny(slugs)`, `hasAll(slugs)`, `role`, `isSuperAdmin`
- `<Can permission="X">` -- renders children only if user has permission
- `<RequirePermission slug="X">` -- route guard, redirects to `/forbidden`
- Global 403 handler in QueryCache: toast "No permission". Global 401: redirect to SWA login.

### Permission map per MVP screen

| Screen / Action | Required permission |
|---|---|
| `/organizations` (list) | `organizations:read` |
| Create organization | `organizations:create` |
| Edit organization | `organizations:edit` |
| Delete organization | `organizations:delete` |
| Org detail / Overview tab | `organizations:read` |
| Fleet tab | `machines:read` |
| Enrollment tab | `enrollment:create` |
| Generate enrollment code | `enrollment:create` |
| Reports tab | `reports:read` |
| Generate/download report | `reports:export` |
| Machine detail | `machines:read` |
| Run detail / control results | `assessment:read` |
| Recycle bin (view) | `recycle_bin:read` |
| Recycle bin (restore) | `recycle_bin:restore` |
| Change org status | `super_admin` role check (not permission-based) |
| Change org brand | `super_admin` role check |

### Existing roles (seed_001)

| Role | Code | Scope |
|---|---|---|
| Super Administrator | `super_admin` | ALL permissions |
| Franchise Owner | `franchise_owner` | All except `admin:*` and `audit:delete` |
| Franchise Technician | `franchise_tech` | Operational modules (assessment, machines, network, vulns, tickets, enrollment, reports) with read/create/edit/export |
| Client Administrator | `client_admin` | Read/export on assessment/machines/network/vulns/reports + CRUD on tickets + read audit |
| Client Viewer | `client_viewer` | Read-only on assessment/machines/reports/tickets |

All marked `is_system = true`. MVP only activates `super_admin`.

### New permissions to seed

New module `organizations` (auto-generates 5 slugs via CROSS JOIN with actions).
New module `recycle_bin` with new action `restore` (generates `recycle_bin:read`, `recycle_bin:create`, `recycle_bin:edit`, `recycle_bin:delete`, `recycle_bin:export`, `recycle_bin:restore`).

Assignment: `super_admin` gets all. `franchise_owner` gets `recycle_bin:read` + `recycle_bin:restore` + `organizations:*`. `franchise_tech` gets `recycle_bin:read` + `recycle_bin:restore` + `organizations:read`.

---

## 7. Data Model Changes

### New table: `brands`

```
brands (
  id              INT IDENTITY PK,
  code            VARCHAR(50) UNIQUE NOT NULL,
  name            NVARCHAR(100) NOT NULL,
  color_primary   VARCHAR(7) NOT NULL,
  color_accent    VARCHAR(7) NOT NULL,
  color_dark_bg   VARCHAR(7) NULL,
  logo_url        NVARCHAR(500) NULL,
  font_family     VARCHAR(50) DEFAULT 'Montserrat',
  is_active       BIT DEFAULT 1
)
```

Seeded with 3 records: `teamlogic` (#008852/#A2C564/#3D4043), `kryoss` (TBD colors), `geminis` (TBD colors).

### Modified table: `organizations`

New columns:
- `brand_id INT NOT NULL DEFAULT <teamlogic_id> FK -> brands` -- determines report branding
- `entra_tenant_id UNIQUEIDENTIFIER NULL` -- for future tenant matching

### Modified table: `users` (future, not MVP migration)

Future columns (documented for self-registration flow):
- `status VARCHAR(20) DEFAULT 'active'` -- `pending | active | disabled | rejected`
- `first_name` / `last_name` (or derive `DisplayName` from form)

### New SQL migrations

**`016_brands_and_org_updates.sql`:**
- CREATE TABLE `brands` + seed 3 rows
- ALTER TABLE `organizations` ADD `brand_id`, `entra_tenant_id`
- INSERT module `organizations` into `modules`
- INSERT module `recycle_bin` into `modules`
- INSERT action `restore` into `actions`
- Auto-generate permissions via CROSS JOIN
- Assign new permissions to existing roles

**`017_prevent_hard_delete.sql`:**
- INSTEAD OF DELETE triggers on ALL tables except `actlog` (log attempt to actlog + THROW 50010). App SQL role retains DELETE permission on entity tables (required for triggers to fire).
- DENY DELETE, UPDATE on `actlog` specifically for app SQL role (no trigger on actlog -- avoids recursion).
- GRANT only INSERT, SELECT on `actlog` for app SQL role.
- Enable Azure SQL Database Audit (documented as infra step, not SQL migration).

---

## 8. Backend Extensions

### New endpoints

| Endpoint | Function | Permission |
|---|---|---|
| `GET /v2/me` | `MeFunction.Me` | (any authenticated user) |
| `GET /v2/organizations` | `OrganizationsFunction.List` | `organizations:read` |
| `GET /v2/organizations/:id` | `OrganizationsFunction.Detail` | `organizations:read` |
| `POST /v2/organizations` | `OrganizationsFunction.Create` | `organizations:create` |
| `PATCH /v2/organizations/:id` | `OrganizationsFunction.Update` | `organizations:edit` |
| `DELETE /v2/organizations/:id` | `OrganizationsFunction.Delete` | `organizations:delete` |
| `GET /v2/recycle-bin` | `RecycleBinFunction.List` | `recycle_bin:read` |
| `POST /v2/recycle-bin/:type/:id/restore` | `RecycleBinFunction.Restore` | `recycle_bin:restore` |

### Modified endpoints

| Endpoint | Change |
|---|---|
| `ReportsFunction.Generate` | Add `&framework=` query param, pass to ReportService |
| `ReportsFunction.GenerateOrg` | Same framework param |
| `ReportService.GenerateHtmlReportAsync` | Filter `control_results` by framework via JOIN with `control_frameworks`. Add framework name to report header. |
| `ReportService` branding source | Change from `franchise.Brand*` to `organization.Brand.*` (new nav property) |
| `BearerAuthMiddleware` | SWA principal format adapter + bootstrap first user logic |
| `ActlogMiddleware` module mapping | Add `organizations`, `auth`, `admin`, `recycle_bin` to switch |

### Organization CRUD business rules

- **Create:** `status` defaults to `prospect`. `brand_id` defaults to TeamLogic IT. `franchise_id` derived from `ICurrentUserService.FranchiseId`.
- **Update:** `status` and `brand_id` only changeable by `super_admin` (`_user.IsAdmin` check). All updates log to actlog with `oldValues`/`newValues`.
- **Delete:** Cascade soft-delete in one transaction: org + machines + enrollment codes + assessment runs + org crypto keys. `ControlResults` and `RunFrameworkScores` untouched (children of runs, unreachable when parent is soft-deleted). Modal confirmation in frontend with cascade count warning.
- **Restore (via recycle bin):** Cascade restore in one transaction: org + all children. Individual machine restore blocked if parent org is soft-deleted.
- **DTO:** Never includes `ApiKey`, `ApiSecret`. Includes computed fields: `machineCount`, `lastAssessmentAt`, `enrollmentCodeCount`, `brand { id, code, name }`.

---

## 9. Security Hardening

### Triple-defense hard-delete prevention

1. **SQL triggers:** `INSTEAD OF DELETE` on every table except `actlog`. Logs attempt to actlog + throws error 50010. The app SQL role retains DELETE permission on entity tables (required for triggers to fire), but the trigger intercepts and blocks every attempt.
2. **SQL permissions on actlog:** `DENY DELETE, UPDATE` on `actlog` specifically for the app SQL role. No trigger on actlog (avoids recursion). The app role has only `INSERT, SELECT` on `actlog`.
3. **Azure SQL Database Audit:** Enabled on `sql-kryoss`, filtered to DELETE attempts, logs to Azure Storage with 90-day retention. Out-of-transaction -- immune to rollbacks.

### Actlog immutability

- Append-only: app role has only `INSERT, SELECT` on `actlog`.
- No UPDATE, no DELETE at SQL level.
- No soft-delete columns on `actlog` (it doesn't implement `IAuditable`).
- Every new table created in future migrations MUST include its `INSTEAD OF DELETE` trigger in the same migration (documented as migration checklist item).

### Existing security baseline (unchanged)

All items from `KryossApi/docs/security-baseline.md` remain in force. The portal does not weaken any existing security measure.

### Documented debt (P0 backlog before Phase 4)

- HMAC credential migration from `Organization.ApiKey/ApiSecret` to per-device `Machine.ApiKey/ApiSecret`. Blast radius of a compromised endpoint is currently org-wide, should be device-scoped.
- Azure Key Vault HSM promotion (blocked on Premium SKU spend approval).

---

## 10. Screens

### Route map

```
/                               -> redirect to /organizations
/organizations                  -> [1] Organizations List
/organizations/:orgId           -> [2] Org Detail (tabs: Overview, Fleet, Enrollment, Reports)
/organizations/:orgId/fleet     -> [2] Fleet tab
/organizations/:orgId/enrollment-> [2] Enrollment tab
/organizations/:orgId/reports   -> [2] Reports tab
/organizations/:orgId/machines/:id         -> [3] Machine Detail
/organizations/:orgId/machines/:id/runs/:runId -> [3] Run Detail
/recycle-bin                    -> [4] Recycle Bin
/forbidden                      -> [5] 403 Error
```

### [1] Organizations List

Data: `GET /v2/organizations` + `GET /v2/dashboard/org-comparison`
Features: TanStack Table with sorting, filter by status (prospect/current/disabled), search by name. Status badges with color. Score with grade letter. Actions: create (drawer), edit (drawer), delete (modal with cascade warning). All gated by permissions.

### [2] Org Detail (tabbed)

Breadcrumb: `Organizations > Estudio Lopez SRL`
Tabs filtered by permission.

**Overview tab** (`organizations:read`):
Data: `GET /v2/organizations/:id` + `GET /v2/dashboard/fleet?organizationId=:id`
4 stat cards (total machines, assessed, avg score, grade) + grade distribution bar chart + top 10 failing controls list.

**Fleet tab** (`machines:read`):
Data: `GET /v2/machines?organizationId=:id`
Paginated table (25/page, backend pagination). Columns: hostname, OS, CPU/RAM, score, grade, last seen. Search by hostname/OS. Click row navigates to machine detail.

**Enrollment tab** (`enrollment:create`):
Data: `GET /v2/enrollment-codes?organizationId=:id`
Table of existing codes with status (active/used/expired). Button "Generate code" opens modal (label + expiry dropdown 7/14/30 days). Generated code shown large with copy-to-clipboard + installation instructions. Delete button on unused codes.

**Reports tab** (`reports:read` + `reports:export`):
Panel: 2 dropdowns (framework: NIST/CIS/HIPAA/ISO27001/PCI-DSS, type: technical/executive/presales). 2 buttons: "Open in new tab" (`window.open`) and "Download HTML" (fetch + blob download). URL: `/api/v2/reports/org/:orgId?type=executive&framework=HIPAA`.

### [3] Machine Detail

Data: `GET /v2/machines/:id` (includes last 10 runs) + `GET /v2/dashboard/trend?machineId=:id`
Hardware info cards (OS, CPU, RAM, disk, TPM, SecureBoot, BitLocker). Assessment history table (date, score, grade, pass/warn/fail counts). Score trend mini line chart (Tremor/Recharts). Click on a run navigates to run detail.

**Run Detail** (sub-page):
Data: `GET /v2/machines/:mid/runs/:rid` (all 647 results pre-joined)
Stats bar: score, grade, pass/warn/fail counts, duration, agent version. TanStack Table with client-side filters: framework (cross-reference with `GET /v2/catalog/controls?framework=X` cached), severity, status, text search. Report buttons (same `<ReportGenerator>` component with `runId`).

### [4] Recycle Bin

Data: `GET /v2/recycle-bin`
Unified table of soft-deleted items. Filter by entity type (organization/machine/enrollment_code). Each row shows: entity type icon, name, description (child count), deleted date, deleted by email. Restore button with cascade confirmation modal. Permission: `recycle_bin:read` to view, `recycle_bin:restore` to restore.

### [5] Forbidden/Error

Static page: TLIT logo, "No access" message, "Back to home" link.

---

## 11. Layout Shell

### Role-aware shell

```tsx
<AppShell>
  {user.role === 'client' ? <ClientLayout /> : <HqLayout />}
</AppShell>
```

- **HqLayout** (MVP active): sidebar + topbar + breadcrumbs + content area.
- **ClientLayout** (post-MVP stub): no sidebar, topbar + breadcrumbs + full-width content. Home = `/organizations/{userOrgId}` auto-redirect.

### Sidebar (HqLayout)

Declarative `NAV_ITEMS` array with `requiredPermission` per item. Items that fail permission check are not rendered (not disabled).

```
NAV_ITEMS:
  Organizations    perm: organizations:read
  --------
  Recycle Bin      perm: recycle_bin:read
```

Post-MVP items (ready to uncomment): Tickets, Invoices, Settings, Users, Roles, Audit Log.

### Topbar

Left: TLIT logo + "Kryoss Portal" text.
Right: user email + dropdown (profile info, logout via `/.auth/logout`).

### Breadcrumbs

Auto-generated from React Router path. Example: `Organizations > Estudio Lopez > PC-LOPEZ-01 > Run 2026-04-09`.

---

## 12. Branding

### Brand table

Three brands seeded: TeamLogic IT (default), Kryoss, Geminis.

### Assignment

Each organization has a `brand_id` FK. Default: TeamLogic IT. Only `super_admin` can change it (dropdown in org edit form).

### Report rendering

`ReportService` reads `organization.Brand.*` (name, colors, logo, font) instead of `franchise.Brand*`. The franchise-level brand fields are deprecated but not removed.

### Portal UI

The portal itself uses TeamLogic IT branding (hardcoded in Tailwind config). Multi-brand portal theming is out of scope.

---

## 13. Delivery Slices

### Slice 1 -- Foundations (4-5 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 1.1 | Scaffold React + Vite + TS + Tailwind + shadcn with TLIT brand | FE | 3h |
| 1.2 | Configure SWA + linked Function App + Entra ID auth | Infra | 3h |
| 1.3 | Create `MeFunction.cs` (GET /v2/me) | BE | 2h |
| 1.4 | Adapt `BearerAuthMiddleware` for SWA format + bootstrap first user | BE | 3h |
| 1.5 | Implement `useMe`, `usePermissions`, `<Can>`, `<RequirePermission>` | FE | 3h |
| 1.6 | Create shell: `<AppShell>` + `<HqLayout>` (sidebar + topbar + breadcrumbs) + router | FE | 4h |
| 1.7 | Dynamic sidebar filtered by permissions | FE | 2h |
| 1.8 | `/forbidden` page and base empty states | FE | 1h |
| 1.9 | Migration `016_brands_and_org_updates.sql` | BE | 3h |
| 1.10 | Migration `017_prevent_hard_delete.sql` | BE | 3h |
| 1.11 | Enable Azure SQL Database Audit | Infra | 1h |
| 1.12 | Update `ActlogMiddleware` module mapping | BE | 30min |
| 1.13 | GitHub Actions `deploy-portal.yml` | CI/CD | 2h |
| 1.14 | Script `sql/helpers/add_hq_user.sql` | BE | 30min |

Done: User logs in, SWA redirects to Entra, returns authenticated, sees shell with "Organizations" sidebar (empty list). First user auto-created as super_admin.

### Slice 2 -- Organizations CRUD (2-3 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 2.1 | `GET /v2/organizations` (list, filterable) | BE | 2h |
| 2.2 | `GET /v2/organizations/:id` (detail with brand, counts) | BE | 1h |
| 2.3 | `POST /v2/organizations` (create + actlog) | BE | 2h |
| 2.4 | `PATCH /v2/organizations/:id` (update, status/brand super_admin only + actlog) | BE | 2h |
| 2.5 | `DELETE /v2/organizations/:id` (cascade soft-delete + actlog) | BE | 2h |
| 2.6 | Organizations List screen (table, filters, badges) | FE | 4h |
| 2.7 | "New Organization" drawer (form with validation) | FE | 3h |
| 2.8 | "Edit Organization" drawer (reuses form, status/brand gated) | FE | 1h |
| 2.9 | Delete confirmation modal with cascade warning | FE | 1h |
| 2.10 | Change ReportService branding source to organization.brand | BE | 1h |

Done: Admin creates "Estudio Lopez", sees it in list with "prospect" badge, edits to "current", badge updates.

### Slice 3 -- Enrollment (1.5-2 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 3.1 | Enrollment tab: table of existing codes with status | FE | 3h |
| 3.2 | "Generate code" modal (label + expiry dropdown) | FE | 2h |
| 3.3 | Generated code display (large code + copy button + instructions) | FE | 2h |
| 3.4 | Delete button on unused codes with confirmation | FE | 1h |
| 3.5 | Verify POST enrollment-codes works without assessmentId | BE | 1h |

Done: Technician generates code, copies it, installs agent on VM, agent enrolls, code shows as "used" with hostname.

### Slice 4 -- Fleet + Machine Detail (2-3 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 4.1 | Fleet tab: paginated table with score/grade/lastSeen | FE | 3h |
| 4.2 | Machine Detail screen: hardware info + assessment history | FE | 3h |
| 4.3 | Score trend mini chart (Tremor/Recharts) | FE | 2h |
| 4.4 | Run Detail: 647 control results table with client-side filters | FE | 4h |
| 4.5 | Framework filter in Run Detail via catalog controls cache | FE | 2h |

Done: Technician navigates to org, sees fleet, clicks machine, sees hardware + history, clicks run, sees all controls, filters by HIPAA + FAIL, sees problems.

### Slice 5 -- Reports (1.5-2 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 5.1 | Add `&framework=` param to ReportService | BE | 3h |
| 5.2 | Add framework name to report HTML header | BE | 1h |
| 5.3 | Reports tab: framework/type dropdowns + view/download buttons | FE | 3h |
| 5.4 | Report buttons in Run Detail (reuses `<ReportGenerator>`) | FE | 1h |
| 5.5 | Download HTML logic (fetch + blob + trigger download) | FE | 1h |

Done: Technician selects HIPAA + Executive, opens in new tab, sees branded report with only HIPAA controls. Downloads HTML file.

### Slice 6 -- Recycle Bin + Hardening (2 days)

| ID | Story | Type | Est. |
|---|---|---|---|
| 6.1 | `GET /v2/recycle-bin` (list soft-deleted with IgnoreQueryFilters) | BE | 3h |
| 6.2 | `POST /v2/recycle-bin/:type/:id/restore` (cascade restore) | BE | 3h |
| 6.3 | Recycle Bin screen (table + restore button + confirmation) | FE | 3h |
| 6.4 | Empty states for every screen | FE | 2h |
| 6.5 | Loading skeletons for tables and cards | FE | 1h |
| 6.6 | Global toast notifications (success, error, 403) | FE | 1h |
| 6.7 | Responsive minimum (collapsible sidebar, horizontal scroll tables) | FE | 2h |

Done: Admin deletes org, confirms cascade, goes to Recycle Bin, sees org + children, restores, everything comes back.

---

## 14. Timeline

| Slice | Days | Cumulative |
|---|---|---|
| 1. Foundations | 4-5 | 4-5 |
| 2. Organizations CRUD | 2-3 | 7-8 |
| 3. Enrollment | 1.5-2 | 9-10 |
| 4. Fleet + Machine | 2-3 | 11-13 |
| 5. Reports | 1.5-2 | 13-15 |
| 6. Recycle Bin + Hardening | 2 | 15-17 |
| **Total** | **15-17 working days** | **~3-4 weeks** |

---

## 15. Post-MVP Backlog (designed, deferred)

| Item | Estimate when activated |
|---|---|
| Roles Admin UI (CRUD roles + permission picker) | ~3 days |
| Self-registration + tenant matching + approval flow | ~3 days |
| Azure AD B2C for external clients | ~2 weeks |
| Audit Log viewer (screen with filters) | ~1 day |
| ClientLayout (no sidebar, single-org) | ~1 day |
| Assessment Profiles CRUD | ~2 days |
| HMAC credential migration to per-device | ~1 day |
| Azure SQL Ledger Tables for actlog | ~1 day |
| Email notifications (SendGrid/Azure Communication Services) | ~2 days |
| User management screen (invite, approve, change role) | ~2 days |

---

## 16. Files to Create/Modify

### New files (backend)

- `src/KryossApi/Functions/Portal/MeFunction.cs`
- `src/KryossApi/Functions/Portal/OrganizationsFunction.cs`
- `src/KryossApi/Functions/Portal/RecycleBinFunction.cs`
- `sql/016_brands_and_org_updates.sql`
- `sql/017_prevent_hard_delete.sql`
- `sql/helpers/add_hq_user.sql`

### Modified files (backend)

- `src/KryossApi/Middleware/BearerAuthMiddleware.cs` -- SWA adapter + bootstrap
- `src/KryossApi/Middleware/ActlogMiddleware.cs` -- module mapping
- `src/KryossApi/Services/ReportService.cs` -- framework filter + brand source change
- `src/KryossApi/Functions/Portal/ReportsFunction.cs` -- framework query param
- `src/KryossApi/Data/Entities/Organization.cs` -- BrandId, EntraTenantId, Brand nav property
- `src/KryossApi/Data/KryossDbContext.cs` -- Brand entity, Organization.Brand FK, Organization.Brand nav

### New files (frontend)

- `KryossPortal/` -- entire React project (scaffold)
- `.github/workflows/deploy-portal.yml`

### New files (infra)

- `.claude/launch.json` -- dev server config for preview

---

## 17. Reference Documents

| Document | When to read |
|---|---|
| `KryossApi/docs/security-baseline.md` | Before any auth/crypto/ingest change |
| `KryossApi/docs/agent-payload-schema.md` | Before modifying agent output or /v1/results |
| `KryossApi/docs/phase-roadmap.md` | Before adding scope |
| `KryossApi/CLAUDE.md` | Before touching backend |
| `KryossAgent/CLAUDE.md` | Before touching agent |
| `Scripts/CLAUDE.md` | When writing PowerShell for RMM/Intune |
| This document | Before touching the portal |
