using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Functions.Portal;

/// <summary>
/// GET /api/v2/catalog/controls?platform=W11&amp;framework=HIPAA&amp;activeOnly=true
///
/// Read-only catalog view for the portal. Returns the list of control
/// definitions filtered by platform and/or framework, with category +
/// all framework tags + all platform mappings attached. Used by the
/// portal to render framework coverage reports and "what gets evaluated
/// on a Win11 machine" dashboards — the stuff that's purely static
/// reference data, not tied to any specific assessment run.
///
/// <para>
/// Distinct from <c>/v2/controls</c> (admin CRUD, paginated, returns
/// <c>check_json</c>) and <c>/v1/controls</c> (agent-only, filtered by
/// the caller's enrolled machine platform, returns only the fields the
/// agent needs to execute the check). This endpoint is the "catalog
/// browser" — no pagination (the whole catalog is ~650 rows and ~150 KB,
/// small enough for the portal to cache in memory), no <c>check_json</c>
/// (the portal never evaluates, it only displays).
/// </para>
///
/// <para>
/// Auth: portal RBAC via <see cref="RequirePermissionAttribute"/>
/// <c>controls:read</c>. The route lives under <c>/v2/</c> deliberately —
/// the <see cref="RbacMiddleware"/> skips RBAC on <c>/v1/</c> because
/// those are agent routes. The phase-roadmap deliverable was originally
/// written as <c>/api/v1/catalog/controls</c> but that predates the
/// v1/v2 convention; v2 is the correct home.
/// </para>
/// </summary>
[RequirePermission("controls:read")]
public class CatalogControlsFunction
{
    private readonly KryossDbContext _db;
    private readonly ILogger<CatalogControlsFunction> _logger;

    public CatalogControlsFunction(KryossDbContext db, ILogger<CatalogControlsFunction> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Function("Catalog_Controls_List")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/catalog/controls")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var platformCode = query["platform"]?.Trim();
        var frameworkCode = query["framework"]?.Trim();
        var activeOnlyStr = query["activeOnly"];

        // Default: only active controls. Admins can pass activeOnly=false
        // to include soft-deleted legacy rows for audit/history views.
        var activeOnly = !string.Equals(activeOnlyStr, "false", StringComparison.OrdinalIgnoreCase);

        // Base query — joined with category because the portal always
        // shows the category name next to the control id.
        var baseQuery = _db.ControlDefs
            .Include(c => c.Category)
            .AsQueryable();

        if (activeOnly)
            baseQuery = baseQuery.Where(c => c.IsActive);

        // Platform filter: restrict to controls mapped to the given platform
        // via control_platforms. Empty string / unknown code => no rows
        // (explicitly: we don't silently return everything on a typo).
        if (!string.IsNullOrEmpty(platformCode))
        {
            var platformId = await _db.Platforms
                .Where(p => p.Code == platformCode && p.IsActive)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync();

            if (platformId is null)
            {
                _logger.LogInformation("Catalog: unknown platform code '{Code}' — returning empty list", platformCode);
                var empty = req.CreateResponse(HttpStatusCode.OK);
                await empty.WriteAsJsonAsync(new
                {
                    platform = platformCode,
                    framework = frameworkCode,
                    total = 0,
                    items = Array.Empty<object>()
                });
                return empty;
            }

            baseQuery = baseQuery.Where(c =>
                _db.ControlPlatforms.Any(cp => cp.ControlDefId == c.Id && cp.PlatformId == platformId));
        }

        // Framework filter: restrict to controls tagged with the given
        // framework via control_frameworks. Same semantics as platform —
        // unknown code => empty list, not "everything".
        if (!string.IsNullOrEmpty(frameworkCode))
        {
            var frameworkId = await _db.Frameworks
                .Where(f => f.Code == frameworkCode && f.IsActive)
                .Select(f => (int?)f.Id)
                .FirstOrDefaultAsync();

            if (frameworkId is null)
            {
                _logger.LogInformation("Catalog: unknown framework code '{Code}' — returning empty list", frameworkCode);
                var empty = req.CreateResponse(HttpStatusCode.OK);
                await empty.WriteAsJsonAsync(new
                {
                    platform = platformCode,
                    framework = frameworkCode,
                    total = 0,
                    items = Array.Empty<object>()
                });
                return empty;
            }

            baseQuery = baseQuery.Where(c =>
                _db.ControlFrameworks.Any(cf => cf.ControlDefId == c.Id && cf.FrameworkId == frameworkId));
        }

        // Project to the portal-facing shape. We deliberately do NOT
        // include CheckJson or Remediation here — the portal renders
        // those from /v2/controls/{id} when the user drills into a single
        // control. Keeping this endpoint lean means the 650-row payload
        // stays under 200 KB and the portal can cache it aggressively.
        var items = await baseQuery
            .OrderBy(c => c.ControlId)
            .Select(c => new
            {
                c.Id,
                c.ControlId,
                c.Name,
                c.Type,
                c.Severity,
                c.IsActive,
                c.Version,
                categoryId = c.CategoryId,
                categoryName = c.Category.Name,
                platforms = _db.ControlPlatforms
                    .Where(cp => cp.ControlDefId == c.Id)
                    .Join(_db.Platforms, cp => cp.PlatformId, p => p.Id,
                        (cp, p) => new { p.Code, p.Name })
                    .ToList(),
                frameworks = _db.ControlFrameworks
                    .Where(cf => cf.ControlDefId == c.Id)
                    .Join(_db.Frameworks, cf => cf.FrameworkId, f => f.Id,
                        (cf, f) => new { f.Code, cf.FrameworkRef })
                    .ToList()
            })
            .ToListAsync();

        _logger.LogInformation(
            "Catalog: returning {Count} controls (platform={Platform}, framework={Framework}, activeOnly={Active})",
            items.Count, platformCode ?? "any", frameworkCode ?? "any", activeOnly);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            platform = platformCode,
            framework = frameworkCode,
            activeOnly,
            total = items.Count,
            items
        });
        return response;
    }
}
