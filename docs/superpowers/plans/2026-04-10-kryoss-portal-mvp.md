# Kryoss Portal MVP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Kryoss Portal MVP — a React SPA that consumes the existing .NET 8 Azure Functions backend to let HQ technicians manage client organizations, enroll Windows machines, view audit results, and generate framework-filtered reports.

**Architecture:** Azure Static Web App (React frontend) linked to the existing Function App `func-kryoss` (.NET 8 backend). SWA Auth handles Entra ID login with httpOnly cookies — the frontend never touches tokens. Permission-driven UI at every layer via `/v2/me` endpoint + `<Can>` component pattern.

**Tech Stack:** React 18, Vite, TypeScript, React Router 6, TanStack Query v5, TanStack Table v8, shadcn/ui, Radix UI, Tailwind CSS, Recharts. Backend: .NET 8 Azure Functions, EF Core 8, Azure SQL.

**Spec:** `docs/superpowers/specs/2026-04-10-kryoss-portal-mvp-design.md`

**Correction from spec:** Migration numbering is 017/018 (not 016/017) — `016_machine_hwid.sql` already exists. `AssessmentRun` does NOT implement `IAuditable`, so cascade soft-delete skips runs — they become naturally unreachable when parent machines are soft-deleted, which is better for compliance (audit data never disappears even temporarily).

---

## File Structure

### Backend — New files

```
KryossApi/src/KryossApi/
  Data/Entities/
    Brand.cs                          -- NEW: Brand entity
    Organization.cs                   -- MODIFY: add BrandId, EntraTenantId, Brand nav
  Data/
    KryossDbContext.cs                -- MODIFY: add Brand DbSet + config, Org FK
  Functions/Portal/
    MeFunction.cs                     -- NEW: GET /v2/me
    OrganizationsFunction.cs          -- NEW: CRUD /v2/organizations
    RecycleBinFunction.cs             -- NEW: list + restore /v2/recycle-bin
  Middleware/
    BearerAuthMiddleware.cs           -- MODIFY: SWA adapter + bootstrap
    ActlogMiddleware.cs               -- MODIFY: module mapping
  Services/
    ReportService.cs                  -- MODIFY: framework filter + brand source

KryossApi/sql/
  017_brands_and_org_updates.sql      -- NEW: brands table, org columns, permissions
  018_prevent_hard_delete.sql         -- NEW: INSTEAD OF DELETE triggers, actlog DENY
  helpers/
    add_hq_user.sql                   -- NEW: onboarding script
```

### Frontend — New project

```
KryossPortal/
  index.html
  package.json
  tsconfig.json
  tsconfig.app.json
  tsconfig.node.json
  vite.config.ts
  tailwind.config.ts
  postcss.config.js
  components.json                     -- shadcn config
  src/
    main.tsx
    App.tsx
    index.css                         -- Tailwind + Montserrat + brand colors
    vite-env.d.ts
    api/
      client.ts                       -- fetch wrapper with credentials:include
      me.ts                           -- useMe hook
      organizations.ts                -- useOrganizations, useOrganization, mutations
      machines.ts                     -- useMachines, useMachine, useRunDetail
      enrollment.ts                   -- useEnrollmentCodes, createCode, deleteCode
      dashboard.ts                    -- useFleetDashboard, useTrend, useOrgComparison
      catalog.ts                      -- useCatalogControls
      recycle-bin.ts                  -- useRecycleBin, restoreItem
    hooks/
      usePermissions.ts               -- has(), hasAny(), role, isSuperAdmin
    components/
      auth/
        Can.tsx                       -- permission gate (renders children or null)
        RequirePermission.tsx          -- route guard (renders children or Navigate)
      layout/
        AppShell.tsx                   -- role-aware shell switcher
        HqLayout.tsx                  -- sidebar + topbar + breadcrumbs + Outlet
        Sidebar.tsx                   -- declarative NAV_ITEMS filtered by permission
        Topbar.tsx                    -- logo + user menu + logout
        Breadcrumbs.tsx               -- auto from React Router
      organizations/
        OrganizationsList.tsx         -- table with filters
        OrganizationForm.tsx          -- create/edit drawer form
        DeleteOrgDialog.tsx           -- cascade warning modal
      org-detail/
        OrgDetail.tsx                 -- tabbed layout
        OverviewTab.tsx               -- KPIs + grade distribution + top failing
        FleetTab.tsx                  -- paginated machine table
        EnrollmentTab.tsx             -- codes table + generate modal
        ReportsTab.tsx                -- framework/type selectors + buttons
      machines/
        MachineDetail.tsx             -- hardware + history + trend chart
        RunDetail.tsx                 -- control results table with filters
      reports/
        ReportGenerator.tsx           -- reusable: dropdowns + view/download buttons
      recycle-bin/
        RecycleBin.tsx                -- unified table + restore
      shared/
        EmptyState.tsx                -- reusable empty state
        LoadingSkeleton.tsx           -- reusable skeleton
        StatusBadge.tsx               -- prospect/current/disabled badge
        GradeBadge.tsx                -- A/B/C/D/F with color
    pages/
      OrganizationsPage.tsx           -- route wrapper
      OrgDetailPage.tsx               -- route wrapper with tabs
      MachineDetailPage.tsx           -- route wrapper
      RunDetailPage.tsx               -- route wrapper
      RecycleBinPage.tsx              -- route wrapper
      ForbiddenPage.tsx               -- 403
    router.tsx                        -- React Router config with guards
    types.ts                          -- shared TypeScript types
    lib/
      utils.ts                        -- shadcn cn() helper
```

### Infra / CI

```
.github/workflows/
  deploy-portal.yml                   -- NEW: GitHub Actions SWA deploy
```

---

## Slice 1 — Foundations

### Task 1.1: SQL Migration — Brands table + Organization updates + Permissions

**Files:**
- Create: `KryossApi/sql/017_brands_and_org_updates.sql`

- [ ] **Step 1: Write the migration**

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =============================================
-- 017_brands_and_org_updates.sql
-- Adds: brands table, org.brand_id, org.entra_tenant_id,
--       organizations module, recycle_bin module, restore action
-- Run AFTER 016_machine_hwid.sql
-- =============================================

-- ── BRANDS TABLE ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'brands')
BEGIN
    CREATE TABLE brands (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        code            VARCHAR(50) NOT NULL,
        name            NVARCHAR(100) NOT NULL,
        color_primary   VARCHAR(7) NOT NULL,
        color_accent    VARCHAR(7) NOT NULL,
        color_dark_bg   VARCHAR(7) NULL,
        logo_url        NVARCHAR(500) NULL,
        font_family     VARCHAR(50) NOT NULL DEFAULT 'Montserrat',
        is_active       BIT NOT NULL DEFAULT 1
    );
    CREATE UNIQUE INDEX UX_brands_code ON brands(code);
END
GO

-- Seed brands
IF NOT EXISTS (SELECT 1 FROM brands WHERE code = 'teamlogic')
BEGIN
    INSERT INTO brands (code, name, color_primary, color_accent, color_dark_bg) VALUES
        ('teamlogic', N'TeamLogic IT',     '#008852', '#A2C564', '#3D4043'),
        ('kryoss',    N'Kryoss',           '#1A73E8', '#4FC3F7', '#1E1E2E'),
        ('geminis',   N'Geminis Computer', '#2E3A87', '#5C6BC0', '#212121');
END
GO

-- ── ORGANIZATION COLUMNS ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'organizations' AND COLUMN_NAME = 'brand_id')
BEGIN
    DECLARE @defaultBrandId INT = (SELECT id FROM brands WHERE code = 'teamlogic');

    ALTER TABLE organizations ADD brand_id INT NOT NULL
        CONSTRAINT df_org_brand DEFAULT @defaultBrandId;

    ALTER TABLE organizations ADD CONSTRAINT fk_org_brand
        FOREIGN KEY (brand_id) REFERENCES brands(id);
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'organizations' AND COLUMN_NAME = 'entra_tenant_id')
BEGIN
    ALTER TABLE organizations ADD entra_tenant_id UNIQUEIDENTIFIER NULL;
END
GO

-- ── NEW MODULES ──
IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'organizations')
    INSERT INTO modules (code, name, sort_order) VALUES ('organizations', N'Organizations', 14);

IF NOT EXISTS (SELECT 1 FROM modules WHERE code = 'recycle_bin')
    INSERT INTO modules (code, name, sort_order) VALUES ('recycle_bin', N'Recycle Bin', 15);
GO

-- ── NEW ACTION: restore ──
IF NOT EXISTS (SELECT 1 FROM actions WHERE code = 'restore')
    INSERT INTO actions (code, name) VALUES ('restore', N'Restore');
GO

-- ── AUTO-GENERATE PERMISSIONS for new modules + action ──
INSERT INTO permissions (module_id, action_id, slug, description)
SELECT m.id, a.id, m.code + ':' + a.code, m.name + N' — ' + a.name
FROM modules m
CROSS JOIN actions a
WHERE NOT EXISTS (
    SELECT 1 FROM permissions p
    WHERE p.module_id = m.id AND p.action_id = a.id
);
GO

-- ── ASSIGN NEW PERMISSIONS TO ROLES ──
-- super_admin: gets ALL (any new permissions)
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r CROSS JOIN permissions p
WHERE r.code = 'super_admin'
  AND NOT EXISTS (
    SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

-- franchise_owner: organizations:* + recycle_bin:read,restore
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_owner'
  AND (
    m.code = 'organizations'
    OR (m.code = 'recycle_bin' AND a.code IN ('read', 'restore'))
  )
  AND NOT EXISTS (
    SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );

-- franchise_tech: organizations:read + recycle_bin:read,restore
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r CROSS JOIN permissions p
JOIN modules m ON p.module_id = m.id
JOIN actions a ON p.action_id = a.id
WHERE r.code = 'franchise_tech'
  AND (
    (m.code = 'organizations' AND a.code = 'read')
    OR (m.code = 'recycle_bin' AND a.code IN ('read', 'restore'))
  )
  AND NOT EXISTS (
    SELECT 1 FROM role_permissions rp WHERE rp.role_id = r.id AND rp.permission_id = p.id
  );
GO
```

- [ ] **Step 2: Run the migration against Azure SQL**

Run: Connect to `sql-kryoss.database.windows.net` / `KryossDb` via SSMS or Azure Data Studio and execute `017_brands_and_org_updates.sql`.

Expected: 3 brands inserted, 2 columns added to organizations, 2 modules + 1 action added, permissions auto-generated, role assignments updated.

- [ ] **Step 3: Verify**

Run:
```sql
SELECT * FROM brands;
SELECT code, name FROM modules ORDER BY sort_order;
SELECT slug FROM permissions WHERE slug LIKE 'organizations:%' OR slug LIKE 'recycle_bin:%' ORDER BY slug;
SELECT column_name FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'organizations' AND column_name IN ('brand_id', 'entra_tenant_id');
```

Expected: 3 brands, modules include organizations + recycle_bin, permissions include organizations:read/create/edit/delete/export and recycle_bin:read/create/edit/delete/export/restore, both new columns exist.

- [ ] **Step 4: Commit**

```bash
git add KryossApi/sql/017_brands_and_org_updates.sql
git commit -m "feat(db): add brands table, org columns, organizations+recycle_bin modules and permissions"
```

---

### Task 1.2: SQL Migration — Prevent hard deletes + Actlog immutability

**Files:**
- Create: `KryossApi/sql/018_prevent_hard_delete.sql`

- [ ] **Step 1: Write the migration**

```sql
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =============================================
-- 018_prevent_hard_delete.sql
-- Creates INSTEAD OF DELETE triggers on all entity tables (except actlog).
-- Applies DENY DELETE/UPDATE on actlog for the app SQL role.
-- Run AFTER 017_brands_and_org_updates.sql
-- =============================================

-- ── INSTEAD OF DELETE triggers (entity tables) ──
-- Each trigger: logs the attempt to actlog, then throws error 50010.

-- Helper: generate triggers for all tables except actlog and system tables.
-- We use dynamic SQL to avoid repeating the trigger body for each table.

DECLARE @tableName NVARCHAR(128);
DECLARE @sql NVARCHAR(MAX);

DECLARE table_cursor CURSOR FOR
    SELECT TABLE_NAME
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_TYPE = 'BASE TABLE'
      AND TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME != 'actlog'
      AND TABLE_NAME NOT LIKE 'sys%'
      AND TABLE_NAME NOT LIKE '__EF%'
    ORDER BY TABLE_NAME;

OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @tableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'
    IF OBJECT_ID(''dbo.trg_' + @tableName + N'_prevent_delete'', ''TR'') IS NOT NULL
        DROP TRIGGER dbo.trg_' + @tableName + N'_prevent_delete;';
    EXEC sp_executesql @sql;

    SET @sql = N'
    CREATE TRIGGER dbo.trg_' + @tableName + N'_prevent_delete ON dbo.' + @tableName + N'
    INSTEAD OF DELETE AS
    BEGIN
        SET NOCOUNT ON;
        -- Best-effort log to actlog (may be rolled back if caller is in txn)
        BEGIN TRY
            INSERT INTO dbo.actlog (timestamp, severity, module, action, entity_type, message)
            SELECT SYSUTCDATETIME(), ''SEC'', ''security'', ''hard_delete_blocked'',
                   ''' + @tableName + N''',
                   CONCAT(''Blocked DELETE attempt on ' + @tableName + N', rows='', (SELECT COUNT(*) FROM deleted));
        END TRY
        BEGIN CATCH
            -- If actlog insert fails (e.g., permissions), still throw
        END CATCH;

        THROW 50010, ''Hard DELETE is forbidden. Use soft-delete via AuditInterceptor.'', 1;
    END';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM table_cursor INTO @tableName;
END;

CLOSE table_cursor;
DEALLOCATE table_cursor;
GO

-- ── ACTLOG IMMUTABILITY ──
-- These commands require running as a user with CONTROL permission on the DB.
-- Replace [func-kryoss-identity] with the actual Managed Identity name.
-- If using kryossadmin for dev, these may need to be run separately.

-- Uncomment and adjust for production deployment:
-- DENY DELETE, UPDATE ON OBJECT::dbo.actlog TO [func-kryoss-identity];
-- GRANT INSERT, SELECT ON OBJECT::dbo.actlog TO [func-kryoss-identity];

-- For dev environment with kryossadmin, document but don't restrict:
PRINT 'NOTE: In production, run DENY DELETE/UPDATE on actlog for the app Managed Identity.';
PRINT 'See KryossApi/docs/security-baseline.md for the runbook.';
GO
```

- [ ] **Step 2: Run the migration against Azure SQL**

Run: Execute `018_prevent_hard_delete.sql` via SSMS/Azure Data Studio.

Expected: INSTEAD OF DELETE triggers created on every table except actlog. Informational message about actlog DENY printed.

- [ ] **Step 3: Verify triggers exist**

Run:
```sql
SELECT t.name AS trigger_name, OBJECT_NAME(t.parent_id) AS table_name
FROM sys.triggers t
WHERE t.name LIKE 'trg_%_prevent_delete'
ORDER BY table_name;
```

Expected: One trigger per table (organizations, machines, users, roles, brands, etc.). No trigger on actlog.

- [ ] **Step 4: Test that DELETE is blocked**

Run:
```sql
BEGIN TRY
    DELETE FROM brands WHERE code = 'teamlogic';
END TRY
BEGIN CATCH
    SELECT ERROR_MESSAGE() AS blocked_message;
END CATCH;
-- Verify brand still exists
SELECT * FROM brands WHERE code = 'teamlogic';
```

Expected: Error message "Hard DELETE is forbidden..." and the brand row still exists.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/sql/018_prevent_hard_delete.sql
git commit -m "feat(db): add INSTEAD OF DELETE triggers on all tables for hard-delete prevention"
```

---

### Task 1.3: SQL Helper — Add HQ User script

**Files:**
- Create: `KryossApi/sql/helpers/add_hq_user.sql`

- [ ] **Step 1: Write the helper script**

```sql
-- =============================================
-- add_hq_user.sql
-- Helper to manually add an HQ user to the portal.
-- Replace the variables below with actual values.
-- Run via SSMS or Azure Data Studio.
-- =============================================

DECLARE @entraOid UNIQUEIDENTIFIER = 'REPLACE-WITH-ENTRA-OBJECT-ID';
DECLARE @email NVARCHAR(200) = 'tecnico@yourtenant.com';
DECLARE @displayName NVARCHAR(200) = 'Tecnico Name';
DECLARE @roleCode VARCHAR(50) = 'super_admin'; -- or 'franchise_tech'
DECLARE @franchiseName NVARCHAR(200) = 'TeamLogic IT'; -- must exist in franchises

-- Resolve IDs
DECLARE @roleId INT = (SELECT id FROM roles WHERE code = @roleCode);
DECLARE @franchiseId UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM franchises WHERE name LIKE '%' + @franchiseName + '%');
DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF @roleId IS NULL BEGIN PRINT 'ERROR: Role not found: ' + @roleCode; RETURN; END
IF @franchiseId IS NULL BEGIN PRINT 'ERROR: Franchise not found: ' + @franchiseName; RETURN; END
IF EXISTS (SELECT 1 FROM users WHERE entra_oid = @entraOid) BEGIN PRINT 'User already exists'; RETURN; END

INSERT INTO users (id, entra_oid, email, display_name, role_id, franchise_id, auth_source, created_by, created_at)
VALUES (NEWID(), @entraOid, @email, @displayName, @roleId, @franchiseId, 'entra', @systemUserId, SYSUTCDATETIME());

PRINT 'User created: ' + @email + ' with role ' + @roleCode;
```

- [ ] **Step 2: Commit**

```bash
mkdir -p KryossApi/sql/helpers
git add KryossApi/sql/helpers/add_hq_user.sql
git commit -m "feat(db): add HQ user onboarding helper script"
```

---

### Task 1.4: Backend — Brand entity + Organization model updates

**Files:**
- Create: `KryossApi/src/KryossApi/Data/Entities/Brand.cs`
- Modify: `KryossApi/src/KryossApi/Data/Entities/Organization.cs`
- Modify: `KryossApi/src/KryossApi/Data/KryossDbContext.cs`

- [ ] **Step 1: Create Brand entity**

Create file `KryossApi/src/KryossApi/Data/Entities/Brand.cs`:

```csharp
namespace KryossApi.Data.Entities;

public class Brand
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string ColorPrimary { get; set; } = null!;
    public string ColorAccent { get; set; } = null!;
    public string? ColorDarkBg { get; set; }
    public string? LogoUrl { get; set; }
    public string FontFamily { get; set; } = "Montserrat";
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Add fields + nav property to Organization**

In `Organization.cs`, add after the `ApiSecret` property:

```csharp
    public int BrandId { get; set; }
    public Guid? EntraTenantId { get; set; }

    // Navigation
    public Brand Brand { get; set; } = null!;
```

The existing `Franchise` nav property and collections stay unchanged.

- [ ] **Step 3: Register Brand in KryossDbContext**

In `KryossDbContext.cs`:

Add DbSet after Franchises:
```csharp
    public DbSet<Brand> Brands => Set<Brand>();
```

Add entity config in `OnModelCreating` after Organization config:
```csharp
        mb.Entity<Brand>(e =>
        {
            e.ToTable("brands");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
        });
```

In the existing Organization config, add FK:
```csharp
            e.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`

Expected: Build succeeds with no errors.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Data/Entities/Brand.cs
git add KryossApi/src/KryossApi/Data/Entities/Organization.cs
git add KryossApi/src/KryossApi/Data/KryossDbContext.cs
git commit -m "feat(model): add Brand entity, Organization.BrandId + EntraTenantId"
```

---

### Task 1.5: Backend — MeFunction endpoint

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/MeFunction.cs`

- [ ] **Step 1: Create MeFunction**

```csharp
using System.Net;
using KryossApi.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// GET /api/v2/me — Returns the current authenticated user's profile and permissions.
/// No [RequirePermission] — any authenticated user can read their own profile.
/// </summary>
public class MeFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;

    public MeFunction(KryossDbContext db, ICurrentUserService user)
    {
        _db = db;
        _user = user;
    }

    [Function("Me_Get")]
    public async Task<HttpResponseData> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/me")] HttpRequestData req)
    {
        if (_user.UserId == Guid.Empty)
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var user = await _db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(u => u.Franchise)
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == _user.UserId);

        if (user is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "User not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            authSource = user.AuthSource,
            lastLoginAt = user.LastLoginAt,
            role = new
            {
                id = user.Role.Id,
                code = user.Role.Code,
                name = user.Role.Name,
                isSystem = user.Role.IsSystem
            },
            franchise = user.Franchise != null ? new
            {
                id = user.Franchise.Id,
                name = user.Franchise.Name
            } : null,
            organization = user.Organization != null ? new
            {
                id = user.Organization.Id,
                name = user.Organization.Name
            } : null,
            permissions = user.Role.RolePermissions
                .Select(rp => rp.Permission.Slug)
                .OrderBy(s => s)
                .ToArray()
        });
        return response;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/MeFunction.cs
git commit -m "feat(api): add GET /v2/me endpoint for current user profile + permissions"
```

---

### Task 1.6: Backend — BearerAuthMiddleware SWA adapter + bootstrap

**Files:**
- Modify: `KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs`

- [ ] **Step 1: Add SWA format detection and bootstrap logic**

Replace the `BearerAuthMiddleware.Invoke` method. The key changes are:

1. Detect SWA vs EasyAuth format by presence of `identityProvider` field in the decoded principal JSON.
2. Extract `oid` from either format (same claim type path).
3. Extract `tid` (tenant ID) from claims for future use.
4. If user not found AND zero users exist in DB, auto-create as `super_admin` (bootstrap).

The full updated file:

```csharp
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryossApi.Middleware;

public class BearerAuthMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            await next(context);
            return;
        }

        var path = httpReq.Url.AbsolutePath;
        if (!path.Contains("/v2/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var principalHeader = httpReq.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var principalValues)
            ? principalValues.FirstOrDefault() : null;

        if (string.IsNullOrEmpty(principalHeader))
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Authentication required" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        var principalJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
        var principal = JsonSerializer.Deserialize<EasyAuthPrincipal>(principalJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (principal is null)
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Invalid principal" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Extract OID — same claim type in both SWA and EasyAuth formats
        var oid = principal.Claims?.FirstOrDefault(c =>
                c.Typ == "http://schemas.microsoft.com/identity/claims/objectidentifier" || c.Typ == "oid")?.Val;

        if (string.IsNullOrEmpty(oid) || !Guid.TryParse(oid, out var objectId))
        {
            var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await resp.WriteAsJsonAsync(new { error = "Missing object identifier claim" });
            context.GetInvocationResult().Value = resp;
            return;
        }

        // Extract TID (tenant ID) for future use
        var tid = principal.Claims?.FirstOrDefault(c =>
                c.Typ == "http://schemas.microsoft.com/identity/claims/tenantid" || c.Typ == "tid")?.Val;

        // Extract email and name for bootstrap
        var email = principal.Claims?.FirstOrDefault(c =>
                c.Typ == "preferred_username" || c.Typ == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")?.Val
            ?? principal.UserDetails;
        var displayName = principal.Claims?.FirstOrDefault(c =>
                c.Typ == "name" || c.Typ == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Val
            ?? email ?? "Unknown";

        var logger = context.InstanceServices.GetRequiredService<ILogger<BearerAuthMiddleware>>();
        var db = context.InstanceServices.GetRequiredService<KryossDbContext>();

        var user = await db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.EntraOid == objectId || u.B2cOid == objectId);

        // Bootstrap: first user ever becomes super_admin
        if (user is null)
        {
            var totalUsers = await db.Users.IgnoreQueryFilters().CountAsync();
            if (totalUsers == 0)
            {
                logger.LogWarning("Bootstrap: creating first user as super_admin. OID={Oid}, Email={Email}", objectId, email);
                var superAdminRole = await db.Roles.Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
                    .FirstOrDefaultAsync(r => r.Code == "super_admin");
                var franchise = await db.Franchises.FirstOrDefaultAsync();

                user = new User
                {
                    Id = Guid.NewGuid(),
                    EntraOid = objectId,
                    Email = email ?? "admin@kryoss.local",
                    DisplayName = displayName,
                    RoleId = superAdminRole?.Id ?? 1,
                    FranchiseId = franchise?.Id,
                    AuthSource = "entra",
                    Role = superAdminRole!
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();

                logger.LogWarning("Bootstrap complete: {Email} is now super_admin", email);
            }
            else
            {
                logger.LogWarning("Unknown user OID: {Oid}", objectId);
                var resp = httpReq.CreateResponse(System.Net.HttpStatusCode.Forbidden);
                await resp.WriteAsJsonAsync(new { error = "User not registered in Kryoss" });
                context.GetInvocationResult().Value = resp;
                return;
            }
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Populate current user context
        var currentUser = context.InstanceServices.GetRequiredService<ICurrentUserService>() as CurrentUserService;
        if (currentUser is not null)
        {
            currentUser.UserId = user.Id;
            currentUser.Email = user.Email;
            currentUser.FranchiseId = user.FranchiseId;
            currentUser.OrganizationId = user.OrganizationId;
            currentUser.IsAdmin = user.Role.Code == "super_admin";
            currentUser.Permissions = user.Role.RolePermissions
                .Select(rp => rp.Permission.Slug)
                .ToArray();
            currentUser.IpAddress = httpReq.Headers.TryGetValues("X-Forwarded-For", out var fwdValues)
                ? fwdValues.FirstOrDefault() : null;
            currentUser.SessionId = httpReq.Headers.TryGetValues("X-MS-TOKEN-AAD-ID-TOKEN", out var tokenValues)
                ? tokenValues.FirstOrDefault()?[..Math.Min(32, tokenValues.FirstOrDefault()?.Length ?? 0)] : null;
        }

        await next(context);
    }
}

internal class EasyAuthPrincipal
{
    public string? AuthTyp { get; set; }
    public string? IdentityProvider { get; set; } // present in SWA format
    public string? UserId { get; set; }            // present in SWA format
    public string? UserDetails { get; set; }       // present in SWA format (email)
    public List<string>? UserRoles { get; set; }   // present in SWA format
    public List<EasyAuthClaim>? Claims { get; set; }
    public string? NameTyp { get; set; }
    public string? RoleTyp { get; set; }
}

internal class EasyAuthClaim
{
    public string Typ { get; set; } = null!;
    public string Val { get; set; } = null!;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Middleware/BearerAuthMiddleware.cs
git commit -m "feat(auth): add SWA principal adapter + bootstrap first user as super_admin"
```

---

### Task 1.7: Backend — ActlogMiddleware module mapping update

**Files:**
- Modify: `KryossApi/src/KryossApi/Middleware/ActlogMiddleware.cs`

- [ ] **Step 1: Update the module switch**

In `ActlogMiddleware.cs`, replace the module resolution switch (around line 57) with:

```csharp
                var module = path switch
                {
                    _ when path.Contains("/v1/") => "agent",
                    _ when path.Contains("/v2/machines") => "machines",
                    _ when path.Contains("/v2/organizations") => "organizations",
                    _ when path.Contains("/v2/assessment") => "assessment",
                    _ when path.Contains("/v2/controls") || path.Contains("/v2/catalog") => "controls",
                    _ when path.Contains("/v2/reports") => "reports",
                    _ when path.Contains("/v2/enrollment") => "enrollment",
                    _ when path.Contains("/v2/dashboard") => "assessment",
                    _ when path.Contains("/v2/recycle-bin") => "recycle_bin",
                    _ when path.Contains("/v2/me") => "auth",
                    _ when path.Contains("/v2/roles") || path.Contains("/v2/users") => "admin",
                    _ => "api"
                };
```

- [ ] **Step 2: Build**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Middleware/ActlogMiddleware.cs
git commit -m "feat(actlog): add module mapping for organizations, recycle_bin, auth, admin"
```

---

### Task 1.8: Backend — ReportService branding source change

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`

- [ ] **Step 1: Change branding source from franchise to organization.brand**

In `ReportService.GenerateHtmlReportAsync`, change the Include to also load `Organization.Brand`:

Replace:
```csharp
        var run = await _db.AssessmentRuns
            .Include(r => r.Machine)
            .Include(r => r.Organization)
                .ThenInclude(o => o.Franchise)
```

With:
```csharp
        var run = await _db.AssessmentRuns
            .Include(r => r.Machine)
            .Include(r => r.Organization)
                .ThenInclude(o => o.Brand)
            .Include(r => r.Organization)
                .ThenInclude(o => o.Franchise)
```

Then change the branding resolution:

Replace:
```csharp
        var franchise = run.Organization.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = franchise.BrandName ?? franchise.Name,
            PrimaryColor = franchise.BrandColorPrimary ?? "#008852",
            AccentColor = franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = franchise.BrandLogoUrl
        };
```

With:
```csharp
        var brand = run.Organization.Brand;
        var franchise = run.Organization.Franchise;
        var branding = new ReportBranding
        {
            CompanyName = brand?.Name ?? franchise.BrandName ?? franchise.Name,
            PrimaryColor = brand?.ColorPrimary ?? franchise.BrandColorPrimary ?? "#008852",
            AccentColor = brand?.ColorAccent ?? franchise.BrandColorAccent ?? "#A2C564",
            LogoUrl = brand?.LogoUrl ?? franchise.BrandLogoUrl
        };
```

This falls back to franchise branding if the brand is null (backwards compatible).

Apply the same change to `GenerateOrgReportAsync` if it loads branding similarly.

- [ ] **Step 2: Build**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs
git commit -m "feat(reports): change branding source from franchise to organization.brand with fallback"
```

---

### Task 1.9: Frontend — Scaffold React project

**Files:**
- Create: entire `KryossPortal/` directory

- [ ] **Step 1: Scaffold with Vite**

```bash
cd "C:/Users/feder/OneDrive - Geminis Computer S.A/Projecto Kryoss"
npm create vite@latest KryossPortal -- --template react-ts
cd KryossPortal
npm install
```

- [ ] **Step 2: Install dependencies**

```bash
npm install @tanstack/react-query @tanstack/react-table react-router-dom recharts sonner
npm install -D tailwindcss @tailwindcss/vite
```

- [ ] **Step 3: Initialize Tailwind**

Create `KryossPortal/src/index.css`:

```css
@import "tailwindcss";

@theme {
  --color-primary: #008852;
  --color-primary-light: #A2C564;
  --color-dark-bg: #3D4043;
  --color-pass: #008852;
  --color-warn: #D97706;
  --color-fail: #C0392B;

  --font-sans: 'Montserrat', sans-serif;
}

@layer base {
  body {
    @apply font-sans antialiased bg-gray-50 text-gray-900;
  }
}
```

Update `KryossPortal/vite.config.ts`:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:7071',
        changeOrigin: true,
      },
    },
  },
})
```

- [ ] **Step 4: Initialize shadcn/ui**

```bash
npx shadcn@latest init
```

Select: TypeScript, path alias `@`, CSS variables, Zinc base color.

Then add initial components:

```bash
npx shadcn@latest add button card table badge dialog sheet select input label separator dropdown-menu sonner skeleton
```

- [ ] **Step 5: Verify dev server starts**

```bash
npm run dev
```

Expected: Vite dev server starts on localhost:5173. Default React page renders.

- [ ] **Step 6: Commit**

```bash
git add KryossPortal/
git commit -m "feat(portal): scaffold React + Vite + TS + Tailwind + shadcn with TLIT brand"
```

---

### Task 1.10: Frontend — API client + auth hooks + permission components

**Files:**
- Create: `KryossPortal/src/api/client.ts`
- Create: `KryossPortal/src/api/me.ts`
- Create: `KryossPortal/src/hooks/usePermissions.ts`
- Create: `KryossPortal/src/components/auth/Can.tsx`
- Create: `KryossPortal/src/components/auth/RequirePermission.tsx`
- Create: `KryossPortal/src/types.ts`

- [ ] **Step 1: Create shared types**

Create `KryossPortal/src/types.ts`:

```ts
export interface MeResponse {
  id: string;
  email: string;
  displayName: string;
  authSource: string;
  lastLoginAt: string | null;
  role: {
    id: number;
    code: string;
    name: string;
    isSystem: boolean;
  };
  franchise: { id: string; name: string } | null;
  organization: { id: string; name: string } | null;
  permissions: string[];
}

export interface Organization {
  id: string;
  franchiseId: string;
  name: string;
  legalName: string | null;
  taxId: string | null;
  status: string;
  brandId: number;
  entraTenantId: string | null;
  brand: { id: number; code: string; name: string };
  machineCount: number;
  lastAssessmentAt: string | null;
  enrollmentCodeCount: number;
  createdAt: string;
}

export interface Machine {
  id: string;
  organizationId: string;
  hostname: string;
  osName: string | null;
  osVersion: string | null;
  cpuName: string | null;
  ramGb: number | null;
  diskType: string | null;
  isActive: boolean;
  lastSeenAt: string | null;
  firstSeenAt: string;
  latestScore: {
    globalScore: number | null;
    grade: string | null;
    startedAt: string;
  } | null;
}

export interface AssessmentRunSummary {
  id: string;
  globalScore: number | null;
  grade: string | null;
  passCount: number | null;
  warnCount: number | null;
  failCount: number | null;
  durationMs: number | null;
  startedAt: string;
}

export interface ControlResultItem {
  controlId: string;
  name: string;
  type: string;
  severity: string;
  categoryName: string;
  status: string;
  score: number;
  maxScore: number;
  finding: string | null;
  actualValue: string | null;
}

export interface RunDetail {
  id: string;
  globalScore: number | null;
  grade: string | null;
  passCount: number | null;
  warnCount: number | null;
  failCount: number | null;
  totalPoints: number | null;
  earnedPoints: number | null;
  agentVersion: string | null;
  durationMs: number | null;
  startedAt: string;
  completedAt: string | null;
  results: ControlResultItem[];
}

export interface EnrollmentCode {
  id: number;
  code: string;
  organizationId: string;
  label: string | null;
  assessmentName: string | null;
  usedBy: string | null;
  usedAt: string | null;
  expiresAt: string;
  createdAt: string;
  isExpired: boolean;
  isUsed: boolean;
}

export interface FleetDashboard {
  totalMachines: number;
  assessedMachines: number;
  avgScore: number;
  gradeDistribution: Record<string, number>;
  totalPass: number;
  totalWarn: number;
  totalFail: number;
  topFailingControls: {
    controlId: string;
    name: string;
    severity: string;
    failCount: number;
  }[];
}

export interface RecycleBinItem {
  entityType: string;
  id: string;
  name: string;
  description: string;
  deletedAt: string;
  deletedByEmail: string | null;
  canRestore: boolean;
}
```

- [ ] **Step 2: Create API client**

Create `KryossPortal/src/api/client.ts`:

```ts
const API_BASE = '/api';

export async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...options?.headers },
    ...options,
  });

  if (res.status === 401) {
    window.location.href = '/.auth/login/aad';
    throw new Error('Unauthorized');
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    const err = new Error(body.error || res.statusText);
    (err as any).status = res.status;
    throw err;
  }

  return res.json();
}
```

- [ ] **Step 3: Create useMe hook**

Create `KryossPortal/src/api/me.ts`:

```ts
import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { MeResponse } from '../types';

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: () => apiFetch<MeResponse>('/v2/me'),
    staleTime: Infinity,
    retry: false,
  });
}
```

- [ ] **Step 4: Create usePermissions hook**

Create `KryossPortal/src/hooks/usePermissions.ts`:

```ts
import { useMe } from '../api/me';

export function usePermissions() {
  const { data: me } = useMe();
  return {
    has: (slug: string) => me?.permissions.includes(slug) ?? false,
    hasAny: (slugs: string[]) => slugs.some((s) => me?.permissions.includes(s)),
    hasAll: (slugs: string[]) => slugs.every((s) => me?.permissions.includes(s)),
    role: me?.role.code,
    isSuperAdmin: me?.role.code === 'super_admin',
  };
}
```

- [ ] **Step 5: Create Can component**

Create `KryossPortal/src/components/auth/Can.tsx`:

```tsx
import type { ReactNode } from 'react';
import { usePermissions } from '@/hooks/usePermissions';

interface CanProps {
  permission?: string;
  anyOf?: string[];
  allOf?: string[];
  fallback?: ReactNode;
  children: ReactNode;
}

export function Can({ permission, anyOf, allOf, fallback = null, children }: CanProps) {
  const { has, hasAny, hasAll } = usePermissions();
  const allowed =
    (permission && has(permission)) ||
    (anyOf && hasAny(anyOf)) ||
    (allOf && hasAll(allOf));
  return allowed ? <>{children}</> : <>{fallback}</>;
}
```

- [ ] **Step 6: Create RequirePermission route guard**

Create `KryossPortal/src/components/auth/RequirePermission.tsx`:

```tsx
import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';

export function RequirePermission({ slug, children }: { slug: string; children: ReactNode }) {
  const { has } = usePermissions();
  if (!has(slug)) return <Navigate to="/forbidden" replace />;
  return <>{children}</>;
}
```

- [ ] **Step 7: Commit**

```bash
git add KryossPortal/src/types.ts KryossPortal/src/api/ KryossPortal/src/hooks/ KryossPortal/src/components/auth/
git commit -m "feat(portal): add API client, useMe/usePermissions hooks, Can/RequirePermission components"
```

---

### Task 1.11: Frontend — Layout shell (AppShell + HqLayout + Sidebar + Topbar + Router)

**Files:**
- Create: `KryossPortal/src/components/layout/AppShell.tsx`
- Create: `KryossPortal/src/components/layout/HqLayout.tsx`
- Create: `KryossPortal/src/components/layout/Sidebar.tsx`
- Create: `KryossPortal/src/components/layout/Topbar.tsx`
- Create: `KryossPortal/src/components/layout/Breadcrumbs.tsx`
- Create: `KryossPortal/src/router.tsx`
- Create: `KryossPortal/src/pages/ForbiddenPage.tsx`
- Modify: `KryossPortal/src/App.tsx`
- Modify: `KryossPortal/src/main.tsx`

- [ ] **Step 1: Create Sidebar**

Create `KryossPortal/src/components/layout/Sidebar.tsx`:

```tsx
import { Link, useLocation } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';
import { Building2, Trash2 } from 'lucide-react';
import { cn } from '@/lib/utils';

const NAV_ITEMS = [
  { label: 'Organizations', path: '/organizations', perm: 'organizations:read', icon: Building2 },
  { type: 'separator' as const },
  { label: 'Recycle Bin', path: '/recycle-bin', perm: 'recycle_bin:read', icon: Trash2 },
] as const;

export function Sidebar() {
  const { has } = usePermissions();
  const location = useLocation();

  return (
    <aside className="w-56 border-r bg-white flex flex-col h-full">
      <nav className="flex-1 p-3 space-y-1">
        {NAV_ITEMS.map((item, i) => {
          if ('type' in item && item.type === 'separator')
            return <hr key={i} className="my-2 border-gray-200" />;
          if (!has(item.perm)) return null;
          const Icon = item.icon;
          const active = location.pathname.startsWith(item.path);
          return (
            <Link
              key={item.path}
              to={item.path}
              className={cn(
                'flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors',
                active ? 'bg-primary/10 text-primary' : 'text-gray-600 hover:bg-gray-100'
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
```

- [ ] **Step 2: Create Topbar**

Create `KryossPortal/src/components/layout/Topbar.tsx`:

```tsx
import { useMe } from '@/api/me';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { LogOut, User } from 'lucide-react';

export function Topbar() {
  const { data: me } = useMe();

  return (
    <header className="h-14 border-b bg-white flex items-center justify-between px-4">
      <div className="flex items-center gap-2">
        <div className="h-8 w-8 rounded bg-primary flex items-center justify-center text-white font-bold text-sm">K</div>
        <span className="font-semibold text-lg">Kryoss Portal</span>
      </div>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" className="gap-2">
            <User className="h-4 w-4" />
            {me?.email ?? '...'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem disabled className="text-xs text-muted-foreground">
            {me?.role.name}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => { window.location.href = '/.auth/logout'; }}>
            <LogOut className="h-4 w-4 mr-2" />
            Logout
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}
```

- [ ] **Step 3: Create Breadcrumbs**

Create `KryossPortal/src/components/layout/Breadcrumbs.tsx`:

```tsx
import { Link, useMatches } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';

export function Breadcrumbs() {
  const matches = useMatches();
  const crumbs = matches
    .filter((m) => (m.handle as any)?.crumb)
    .map((m) => ({ label: (m.handle as any).crumb(m.data, m.params), path: m.pathname }));

  if (crumbs.length === 0) return null;

  return (
    <nav className="flex items-center gap-1 text-sm text-muted-foreground mb-4">
      {crumbs.map((c, i) => (
        <span key={c.path} className="flex items-center gap-1">
          {i > 0 && <ChevronRight className="h-3 w-3" />}
          {i < crumbs.length - 1 ? (
            <Link to={c.path} className="hover:text-primary">{c.label}</Link>
          ) : (
            <span className="text-foreground font-medium">{c.label}</span>
          )}
        </span>
      ))}
    </nav>
  );
}
```

- [ ] **Step 4: Create HqLayout**

Create `KryossPortal/src/components/layout/HqLayout.tsx`:

```tsx
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { Topbar } from './Topbar';
import { Breadcrumbs } from './Breadcrumbs';

export function HqLayout() {
  return (
    <div className="h-screen flex flex-col">
      <Topbar />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar />
        <main className="flex-1 overflow-y-auto p-6">
          <Breadcrumbs />
          <Outlet />
        </main>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Create AppShell**

Create `KryossPortal/src/components/layout/AppShell.tsx`:

```tsx
import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { Skeleton } from '@/components/ui/skeleton';

export function AppShell() {
  const { data: me, isLoading, isError } = useMe();

  if (isLoading) {
    return (
      <div className="h-screen flex items-center justify-center">
        <Skeleton className="h-8 w-48" />
      </div>
    );
  }

  if (isError || !me) {
    window.location.href = '/.auth/login/aad';
    return null;
  }

  // MVP: only HqLayout. Post-MVP: switch based on role
  return <HqLayout />;
}
```

- [ ] **Step 6: Create ForbiddenPage**

Create `KryossPortal/src/pages/ForbiddenPage.tsx`:

```tsx
import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { ShieldX } from 'lucide-react';

export function ForbiddenPage() {
  return (
    <div className="h-full flex flex-col items-center justify-center gap-4 text-center">
      <ShieldX className="h-16 w-16 text-muted-foreground" />
      <h1 className="text-2xl font-semibold">Access Denied</h1>
      <p className="text-muted-foreground">You don't have permission to view this page.</p>
      <Button asChild variant="outline">
        <Link to="/">Back to Home</Link>
      </Button>
    </div>
  );
}
```

- [ ] **Step 7: Create Router**

Create `KryossPortal/src/router.tsx`:

```tsx
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { RequirePermission } from './components/auth/RequirePermission';
import { ForbiddenPage } from './pages/ForbiddenPage';

// Lazy load pages — will be created in later slices
// For now, create placeholder components
function PlaceholderPage({ title }: { title: string }) {
  return <div className="text-muted-foreground">Coming soon: {title}</div>;
}

export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { index: true, element: <Navigate to="/organizations" replace /> },
      {
        path: 'organizations',
        handle: { crumb: () => 'Organizations' },
        children: [
          {
            index: true,
            element: (
              <RequirePermission slug="organizations:read">
                <PlaceholderPage title="Organizations" />
              </RequirePermission>
            ),
          },
          {
            path: ':orgId',
            handle: { crumb: (_: any, p: any) => p.orgId.slice(0, 8) + '...' },
            children: [
              { index: true, element: <PlaceholderPage title="Org Detail" /> },
              { path: 'fleet', element: <PlaceholderPage title="Fleet" /> },
              { path: 'enrollment', element: <PlaceholderPage title="Enrollment" /> },
              { path: 'reports', element: <PlaceholderPage title="Reports" /> },
              {
                path: 'machines/:machineId',
                handle: { crumb: (_: any, p: any) => p.machineId.slice(0, 8) + '...' },
                children: [
                  { index: true, element: <PlaceholderPage title="Machine Detail" /> },
                  { path: 'runs/:runId', element: <PlaceholderPage title="Run Detail" /> },
                ],
              },
            ],
          },
        ],
      },
      {
        path: 'recycle-bin',
        handle: { crumb: () => 'Recycle Bin' },
        element: (
          <RequirePermission slug="recycle_bin:read">
            <PlaceholderPage title="Recycle Bin" />
          </RequirePermission>
        ),
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <Navigate to="/organizations" replace /> },
    ],
  },
]);
```

- [ ] **Step 8: Update App.tsx and main.tsx**

Replace `KryossPortal/src/App.tsx`:

```tsx
import { RouterProvider } from 'react-router-dom';
import { QueryClient, QueryClientProvider, QueryCache } from '@tanstack/react-query';
import { Toaster, toast } from 'sonner';
import { router } from './router';

const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error: any) => {
      if (error.status === 403) toast.error('You don\'t have permission for this action');
    },
  }),
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <Toaster position="top-right" richColors />
    </QueryClientProvider>
  );
}
```

Replace `KryossPortal/src/main.tsx`:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
```

- [ ] **Step 9: Verify dev server renders shell**

```bash
cd KryossPortal && npm run dev
```

Expected: Opens in browser, shows loading skeleton briefly, then redirects to SWA login (or shows shell if mocking `/v2/me`). The sidebar shows "Organizations" and "Recycle Bin". Topbar shows "Kryoss Portal".

- [ ] **Step 10: Commit**

```bash
git add KryossPortal/src/
git commit -m "feat(portal): add layout shell (AppShell, HqLayout, Sidebar, Topbar, Breadcrumbs, Router)"
```

---

### Task 1.12: CI/CD — GitHub Actions deploy workflow

**Files:**
- Create: `.github/workflows/deploy-portal.yml`

- [ ] **Step 1: Create workflow file**

```yaml
name: Deploy Kryoss Portal

on:
  push:
    branches: [main]
    paths:
      - 'KryossPortal/**'
      - '.github/workflows/deploy-portal.yml'
  pull_request:
    branches: [main]
    paths:
      - 'KryossPortal/**'

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    name: Build and Deploy
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: 'npm'
          cache-dependency-path: KryossPortal/package-lock.json

      - name: Install and Build
        working-directory: KryossPortal
        run: |
          npm ci
          npm run build

      - name: Deploy to SWA
        if: github.event_name == 'push'
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.SWA_DEPLOY_TOKEN }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: KryossPortal
          output_location: dist
          skip_api_build: true
```

- [ ] **Step 2: Commit**

```bash
mkdir -p .github/workflows
git add .github/workflows/deploy-portal.yml
git commit -m "ci: add GitHub Actions workflow for SWA portal deploy"
```

---

**Slice 1 complete.** At this point: SQL migrations applied, backend has `/v2/me` + bootstrap + updated middleware, frontend has shell with sidebar/topbar/routing/auth hooks. Deploy pipeline ready.

---

## Slice 2 — Organizations CRUD

### Task 2.1: Backend — OrganizationsFunction

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/OrganizationsFunction.cs`

- [ ] **Step 1: Create the function**

Create `KryossApi/src/KryossApi/Functions/Portal/OrganizationsFunction.cs`. This is a large file (~250 lines) implementing List, Detail, Create, Update, Delete with cascade soft-delete. Key implementation details:

- `List`: filterable by `franchiseId`, `status`, `search`. Includes computed `machineCount`, `lastAssessmentAt` via subqueries. Joins with `Dashboard_OrgComparison` data pattern.
- `Detail`: single org with brand, machineCount, enrollmentCodeCount, lastAssessmentAt.
- `Create`: defaults status=prospect, brandId=teamlogic. franchiseId from `_user.FranchiseId`. Logs to actlog.
- `Update`: status + brandId only changeable if `_user.IsAdmin`. Logs with oldValues/newValues.
- `Delete`: cascade soft-delete org + machines + enrollment codes + crypto keys in one transaction. Logs with counts. Does NOT cascade to AssessmentRun (no IAuditable).

The DTO never includes `ApiKey` or `ApiSecret`.

Write the full implementation following the same patterns as `MachinesFunction.cs` and `EnrollmentCodesFunction.cs` for consistency (inline DTOs, actlog logging, ICurrentUserService scoping).

- [ ] **Step 2: Build**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/OrganizationsFunction.cs
git commit -m "feat(api): add Organizations CRUD with cascade soft-delete"
```

---

### Task 2.2: Frontend — Organizations API hooks

**Files:**
- Create: `KryossPortal/src/api/organizations.ts`
- Create: `KryossPortal/src/api/dashboard.ts`

- [ ] **Step 1: Create organizations API**

Create `KryossPortal/src/api/organizations.ts`:

```ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { Organization } from '../types';

interface OrgListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: Organization[];
}

export function useOrganizations(params?: { status?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  const query = qs.toString() ? `?${qs}` : '';

  return useQuery({
    queryKey: ['organizations', params],
    queryFn: () => apiFetch<OrgListResponse>(`/v2/organizations${query}`),
  });
}

export function useOrganization(id: string | undefined) {
  return useQuery({
    queryKey: ['organization', id],
    queryFn: () => apiFetch<Organization>(`/v2/organizations/${id}`),
    enabled: !!id,
  });
}

export function useCreateOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: { name: string; legalName?: string; taxId?: string; status?: string; brandId?: number; entraTenantId?: string }) =>
      apiFetch('/v2/organizations', { method: 'POST', body: JSON.stringify(data) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['organizations'] }),
  });
}

export function useUpdateOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...data }: { id: string; name?: string; legalName?: string; taxId?: string; status?: string; brandId?: number }) =>
      apiFetch(`/v2/organizations/${id}`, { method: 'PATCH', body: JSON.stringify(data) }),
    onSuccess: (_, vars) => {
      qc.invalidateQueries({ queryKey: ['organizations'] });
      qc.invalidateQueries({ queryKey: ['organization', vars.id] });
    },
  });
}

export function useDeleteOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiFetch(`/v2/organizations/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['organizations'] }),
  });
}
```

- [ ] **Step 2: Create dashboard API**

Create `KryossPortal/src/api/dashboard.ts`:

```ts
import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { FleetDashboard } from '../types';

export function useFleetDashboard(organizationId?: string) {
  const qs = organizationId ? `?organizationId=${organizationId}` : '';
  return useQuery({
    queryKey: ['dashboard', 'fleet', organizationId],
    queryFn: () => apiFetch<FleetDashboard>(`/v2/dashboard/fleet${qs}`),
    enabled: !!organizationId,
  });
}

export function useTrend(params: { machineId?: string; organizationId?: string; months?: number }) {
  const qs = new URLSearchParams();
  if (params.machineId) qs.set('machineId', params.machineId);
  if (params.organizationId) qs.set('organizationId', params.organizationId);
  if (params.months) qs.set('months', String(params.months));
  return useQuery({
    queryKey: ['dashboard', 'trend', params],
    queryFn: () => apiFetch<{ months: number; dataPoints: any[] }>(`/v2/dashboard/trend?${qs}`),
    enabled: !!(params.machineId || params.organizationId),
  });
}
```

- [ ] **Step 3: Commit**

```bash
git add KryossPortal/src/api/organizations.ts KryossPortal/src/api/dashboard.ts
git commit -m "feat(portal): add organizations + dashboard API hooks"
```

---

### Task 2.3: Frontend — Organizations List page + Form drawer

**Files:**
- Create: `KryossPortal/src/components/organizations/OrganizationsList.tsx`
- Create: `KryossPortal/src/components/organizations/OrganizationForm.tsx`
- Create: `KryossPortal/src/components/organizations/DeleteOrgDialog.tsx`
- Create: `KryossPortal/src/components/shared/StatusBadge.tsx`
- Create: `KryossPortal/src/components/shared/GradeBadge.tsx`
- Create: `KryossPortal/src/pages/OrganizationsPage.tsx`
- Modify: `KryossPortal/src/router.tsx`

- [ ] **Step 1: Create StatusBadge and GradeBadge shared components**

Write reusable badge components:
- `StatusBadge`: shows prospect (yellow), current (green), disabled (gray) with colored dot.
- `GradeBadge`: shows A+/A/B/C/D/F with color scale (A=#008852, B=#A2C564, C=#D97706, D=#C0392B, F=#7F1D1D).

- [ ] **Step 2: Create OrganizationForm drawer**

Implements a `Sheet` (shadcn drawer) with form fields: Name (required), LegalName, TaxId, Status (dropdown, super_admin only), Brand (dropdown, super_admin only). Reused for both create and edit. Uses `useCreateOrganization` and `useUpdateOrganization` mutations.

- [ ] **Step 3: Create DeleteOrgDialog**

`AlertDialog` with cascade warning text: "This will move {org.name} and all its data ({machineCount} machines, etc.) to the Recycle Bin. This can be undone from the Recycle Bin."

- [ ] **Step 4: Create OrganizationsList**

TanStack Table with columns: Name, Status (StatusBadge), Machines (count), Score (GradeBadge), Last Scan (relative time). Filter bar: search input + status dropdown. Row click navigates to `/organizations/:id`. Action buttons gated by `<Can>`.

- [ ] **Step 5: Create OrganizationsPage route wrapper**

```tsx
import { OrganizationsList } from '@/components/organizations/OrganizationsList';
export function OrganizationsPage() {
  return <OrganizationsList />;
}
```

- [ ] **Step 6: Update router to use real page**

Replace the placeholder in `router.tsx` for the organizations index route with `<OrganizationsPage />`.

- [ ] **Step 7: Verify in dev server**

Expected: Organizations page renders with empty state "No organizations yet. Create your first organization." Create button opens drawer, form submits, new org appears in list.

- [ ] **Step 8: Commit**

```bash
git add KryossPortal/src/components/organizations/ KryossPortal/src/components/shared/ KryossPortal/src/pages/OrganizationsPage.tsx KryossPortal/src/router.tsx
git commit -m "feat(portal): add Organizations list page with create/edit/delete"
```

---

## Slice 3 — Enrollment

### Task 3.1: Frontend — Org Detail page with tabs + Enrollment tab

**Files:**
- Create: `KryossPortal/src/components/org-detail/OrgDetail.tsx`
- Create: `KryossPortal/src/components/org-detail/OverviewTab.tsx`
- Create: `KryossPortal/src/components/org-detail/EnrollmentTab.tsx`
- Create: `KryossPortal/src/api/enrollment.ts`
- Create: `KryossPortal/src/pages/OrgDetailPage.tsx`
- Modify: `KryossPortal/src/router.tsx`

- [ ] **Step 1: Create enrollment API hooks**

Create `KryossPortal/src/api/enrollment.ts` with `useEnrollmentCodes(orgId)`, `useCreateCode()`, `useDeleteCode()`.

- [ ] **Step 2: Create OrgDetail with tab navigation**

Tabbed layout using React Router nested routes or a tab component. Tabs: Overview, Fleet, Enrollment, Reports. Each tab is permission-gated. Header shows org name + edit/delete buttons.

- [ ] **Step 3: Create OverviewTab (placeholder with KPIs)**

Uses `useFleetDashboard(orgId)` to show 4 stat cards (total machines, assessed, avg score, grade) + top failing controls list. Grade distribution bar chart using Recharts.

- [ ] **Step 4: Create EnrollmentTab**

Table of codes with status badges (active/used/expired). "Generate code" button opens dialog with label input + expiry dropdown (7/14/30 days). After generation, shows code in large monospace font with copy-to-clipboard button + installation instructions.

- [ ] **Step 5: Update router**

Replace org detail placeholder with `<OrgDetailPage />` and nest tab routes.

- [ ] **Step 6: Verify**

Expected: Click org in list -> tabbed detail page with Overview (KPIs) and Enrollment tab. Generate code, copy, see in list.

- [ ] **Step 7: Commit**

```bash
git add KryossPortal/src/components/org-detail/ KryossPortal/src/api/enrollment.ts KryossPortal/src/pages/OrgDetailPage.tsx KryossPortal/src/router.tsx
git commit -m "feat(portal): add Org Detail page with Overview + Enrollment tabs"
```

---

## Slice 4 — Fleet + Machine Detail

### Task 4.1: Frontend — Fleet tab + Machine Detail + Run Detail

**Files:**
- Create: `KryossPortal/src/components/org-detail/FleetTab.tsx`
- Create: `KryossPortal/src/components/machines/MachineDetail.tsx`
- Create: `KryossPortal/src/components/machines/RunDetail.tsx`
- Create: `KryossPortal/src/api/machines.ts`
- Create: `KryossPortal/src/api/catalog.ts`
- Create: `KryossPortal/src/pages/MachineDetailPage.tsx`
- Create: `KryossPortal/src/pages/RunDetailPage.tsx`
- Modify: `KryossPortal/src/router.tsx`

- [ ] **Step 1: Create machines API hooks**

Create `KryossPortal/src/api/machines.ts` with `useMachines(orgId, page, search)`, `useMachine(id)`, `useRunDetail(machineId, runId)`.

- [ ] **Step 2: Create catalog API hook**

Create `KryossPortal/src/api/catalog.ts` with `useCatalogControls(framework?)` — cached with `staleTime: 5 * 60 * 1000` (5 min). Used to filter run results by framework client-side.

- [ ] **Step 3: Create FleetTab**

Paginated TanStack Table using backend pagination (`useMachines`). Columns: Hostname, OS, CPU/RAM, Score (GradeBadge), Last Seen (relative). Search input. Row click -> `/organizations/:orgId/machines/:id`.

- [ ] **Step 4: Create MachineDetail**

Hardware info cards (OS, CPU, RAM, Disk, TPM, SecureBoot, BitLocker). Assessment history table (last 10 runs from `useMachine`). Score trend mini line chart using Recharts + `useTrend`. Click on a run -> RunDetail.

- [ ] **Step 5: Create RunDetail**

Stats bar: score, grade, pass/warn/fail, duration, agent version. TanStack Table with ALL results (client-side filtering). Filters: framework dropdown (cross-ref catalog), severity dropdown, status dropdown (pass/warn/fail), text search. This is the most complex table in the portal — ~647 rows with multi-filter.

- [ ] **Step 6: Update router**

Wire up machine and run detail routes with real pages.

- [ ] **Step 7: Verify**

Expected: Fleet tab shows machines with scores. Click machine -> hardware + history + trend chart. Click run -> 647 results with working filters.

- [ ] **Step 8: Commit**

```bash
git add KryossPortal/src/components/org-detail/FleetTab.tsx KryossPortal/src/components/machines/ KryossPortal/src/api/machines.ts KryossPortal/src/api/catalog.ts KryossPortal/src/pages/MachineDetailPage.tsx KryossPortal/src/pages/RunDetailPage.tsx KryossPortal/src/router.tsx
git commit -m "feat(portal): add Fleet tab, Machine Detail, Run Detail with control results"
```

---

## Slice 5 — Reports

### Task 5.1: Backend — ReportService framework filter

**Files:**
- Modify: `KryossApi/src/KryossApi/Services/ReportService.cs`
- Modify: `KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs`

- [ ] **Step 1: Add framework parameter to ReportService interface**

In `IReportService`, change signatures:
```csharp
Task<string> GenerateHtmlReportAsync(Guid runId, string reportType = "technical", string? frameworkCode = null);
Task<string> GenerateOrgReportAsync(Guid orgId, string reportType = "executive", string? frameworkCode = null);
```

- [ ] **Step 2: Add framework filter to control results query**

In `GenerateHtmlReportAsync`, after the main results query, add:
```csharp
if (!string.IsNullOrEmpty(frameworkCode))
{
    var frameworkId = await _db.Frameworks
        .Where(f => f.Code == frameworkCode && f.IsActive)
        .Select(f => (int?)f.Id)
        .FirstOrDefaultAsync();

    if (frameworkId.HasValue)
    {
        var controlIdsInFramework = await _db.ControlFrameworks
            .Where(cf => cf.FrameworkId == frameworkId.Value)
            .Select(cf => cf.ControlDefId)
            .ToHashSetAsync();

        results = results.Where(r => controlIdsInFramework.Contains(/* controlDefId */)).ToList();
    }
}
```

Note: the existing `ReportControlResult` doesn't have `ControlDefId`. You'll need to add it to the select projection or filter via controlId lookup.

Also add the framework name to the report title:
```csharp
var reportTitle = frameworkCode != null
    ? $"{frameworkCode} Compliance Report"
    : "Security Assessment Report";
```

Apply same changes to `GenerateOrgReportAsync`.

- [ ] **Step 3: Add framework query param to ReportsFunction**

In both `Generate` and `GenerateOrg` methods, add:
```csharp
var frameworkCode = query["framework"]; // NIST, CIS, HIPAA, ISO27001, PCI-DSS
var html = await _reports.GenerateHtmlReportAsync(runId, reportType, frameworkCode);
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build KryossApi/src/KryossApi/KryossApi.csproj`
Expected: Builds. Test with: `GET /v2/reports/{runId}?type=executive&framework=HIPAA`

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Services/ReportService.cs KryossApi/src/KryossApi/Functions/Portal/ReportsFunction.cs
git commit -m "feat(reports): add framework filter param to report generation"
```

---

### Task 5.2: Frontend — Reports tab + ReportGenerator component

**Files:**
- Create: `KryossPortal/src/components/reports/ReportGenerator.tsx`
- Create: `KryossPortal/src/components/org-detail/ReportsTab.tsx`
- Modify: `KryossPortal/src/components/machines/RunDetail.tsx`

- [ ] **Step 1: Create ReportGenerator component**

Reusable component with two dropdowns (framework, report type) and two buttons (open in tab, download). Props: `targetType: 'run' | 'org'`, `targetId: string`.

```tsx
// Constructs URL: /api/v2/reports/{runId}?type=X&framework=Y
// or: /api/v2/reports/org/{orgId}?type=X&framework=Y
// "Open in tab": window.open(url)
// "Download": fetch(url) -> blob -> trigger download with descriptive filename
```

- [ ] **Step 2: Create ReportsTab**

Simple panel using `<ReportGenerator targetType="org" targetId={orgId} />`.

- [ ] **Step 3: Add ReportGenerator to RunDetail**

Add `<ReportGenerator targetType="run" targetId={runId} />` at the top of RunDetail page.

- [ ] **Step 4: Verify**

Expected: Reports tab -> select HIPAA + Executive -> Open in tab -> branded HTML report with only HIPAA controls. Download -> file saves.

- [ ] **Step 5: Commit**

```bash
git add KryossPortal/src/components/reports/ KryossPortal/src/components/org-detail/ReportsTab.tsx
git commit -m "feat(portal): add ReportGenerator component + Reports tab + Run Detail reports"
```

---

## Slice 6 — Recycle Bin + Hardening

### Task 6.1: Backend — RecycleBinFunction

**Files:**
- Create: `KryossApi/src/KryossApi/Functions/Portal/RecycleBinFunction.cs`

- [ ] **Step 1: Create the function**

Implements:
- `GET /v2/recycle-bin?type=organization|machine|enrollment_code` — queries with `IgnoreQueryFilters()` where `DeletedAt != null`.
- `POST /v2/recycle-bin/{entityType}/{id}/restore` — cascade restore: sets `DeletedAt = null, DeletedBy = null` on entity + all children.

Key: for organization restore, also restore machines + enrollment codes + crypto keys. For machine restore, check parent org is not soft-deleted (error if so).

Permission: `[RequirePermission("recycle_bin:read")]` on List, `[RequirePermission("recycle_bin:restore")]` on Restore.

- [ ] **Step 2: Build and commit**

```bash
git add KryossApi/src/KryossApi/Functions/Portal/RecycleBinFunction.cs
git commit -m "feat(api): add Recycle Bin list + cascade restore endpoints"
```

---

### Task 6.2: Frontend — Recycle Bin page

**Files:**
- Create: `KryossPortal/src/api/recycle-bin.ts`
- Create: `KryossPortal/src/components/recycle-bin/RecycleBin.tsx`
- Create: `KryossPortal/src/pages/RecycleBinPage.tsx`
- Modify: `KryossPortal/src/router.tsx`

- [ ] **Step 1: Create recycle bin API hooks**

```ts
// useRecycleBin(type?) -> GET /v2/recycle-bin?type=X
// useRestoreItem() -> POST /v2/recycle-bin/{type}/{id}/restore
```

- [ ] **Step 2: Create RecycleBin component**

Table with columns: Type (icon), Name, Description (child counts), Deleted date, Deleted by. Filter by entity type. Restore button with confirmation dialog. Empty state: "Recycle Bin is empty."

- [ ] **Step 3: Wire up router + verify**

- [ ] **Step 4: Commit**

```bash
git add KryossPortal/src/api/recycle-bin.ts KryossPortal/src/components/recycle-bin/ KryossPortal/src/pages/RecycleBinPage.tsx KryossPortal/src/router.tsx
git commit -m "feat(portal): add Recycle Bin page with restore"
```

---

### Task 6.3: Frontend — Empty states + Loading skeletons + Toasts + Responsive

**Files:**
- Create: `KryossPortal/src/components/shared/EmptyState.tsx`
- Create: `KryossPortal/src/components/shared/LoadingSkeleton.tsx`
- Modify: various page components to add empty states and loading skeletons

- [ ] **Step 1: Create EmptyState component**

```tsx
interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode; // e.g., a "Create" button
}
```

- [ ] **Step 2: Create LoadingSkeleton for tables**

A reusable table skeleton that shows N rows of animated placeholder cells.

- [ ] **Step 3: Add empty states to all pages**

- OrganizationsList: "No organizations yet. Create your first organization."
- FleetTab: "No machines enrolled yet. Generate an enrollment code to get started."
- EnrollmentTab: "No enrollment codes. Generate one to start enrolling machines."
- RunDetail empty results: "No control results for this run."

- [ ] **Step 4: Add loading skeletons to all data tables**

Replace `isLoading` conditional renders with `<LoadingSkeleton rows={5} />`.

- [ ] **Step 5: Verify toasts work**

The global 403 handler in QueryCache already shows toasts via Sonner. Verify mutations show success toasts:
- "Organization created" on create
- "Organization updated" on edit
- "Moved to Recycle Bin" on delete
- "Restored successfully" on recycle bin restore

- [ ] **Step 6: Basic responsive**

- Sidebar: add `lg:block hidden` + hamburger menu for mobile
- Tables: wrap in `overflow-x-auto`
- Stat cards: `grid grid-cols-2 lg:grid-cols-4`

- [ ] **Step 7: Commit**

```bash
git add KryossPortal/src/components/shared/ KryossPortal/src/
git commit -m "feat(portal): add empty states, loading skeletons, toasts, responsive basics"
```

---

## Final: Update CLAUDE.md

### Task 7.1: Update root CLAUDE.md with portal info

**Files:**
- Modify: `CLAUDE.md` (root)
- Modify: `KryossApi/CLAUDE.md`

- [ ] **Step 1: Add portal section to root CLAUDE.md**

Add after the "Repository layout" section:

```markdown
### KryossPortal (NEW — Phase 1 MVP)

React 18 + Vite + TypeScript SPA. Deployed as Azure Static Web App linked to `func-kryoss`.
Auth: SWA Auth (Entra ID, httpOnly cookies). See spec: `docs/superpowers/specs/2026-04-10-kryoss-portal-mvp-design.md`.
```

- [ ] **Step 2: Update KryossApi/CLAUDE.md endpoint table**

Add the new endpoints: `/v2/me`, `/v2/organizations`, `/v2/recycle-bin`.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md KryossApi/CLAUDE.md
git commit -m "docs: update CLAUDE.md with portal MVP info and new endpoints"
```

---

## Summary

| Slice | Tasks | Estimated Days |
|---|---|---|
| 1. Foundations | 1.1-1.12 | 4-5 |
| 2. Organizations CRUD | 2.1-2.3 | 2-3 |
| 3. Enrollment | 3.1 | 1.5-2 |
| 4. Fleet + Machine | 4.1 | 2-3 |
| 5. Reports | 5.1-5.2 | 1.5-2 |
| 6. Recycle Bin + Hardening | 6.1-6.3 | 2 |
| 7. Docs | 7.1 | 0.5 |
| **Total** | | **~15-17 days** |
