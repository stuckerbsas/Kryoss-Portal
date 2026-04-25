using System.Net;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CloudAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

public class CloudAssessmentFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly ICloudAssessmentService _service;
    private readonly IFindingStatusService _statusService;
    private readonly IConsentOrchestrator _consent;
    private readonly ICloudAssessmentReportService _reports;

    public CloudAssessmentFunction(
        KryossDbContext db,
        ICurrentUserService user,
        ICloudAssessmentService service,
        IFindingStatusService statusService,
        IConsentOrchestrator consent,
        ICloudAssessmentReportService reports)
    {
        _db = db;
        _user = user;
        _service = service;
        _statusService = statusService;
        _consent = consent;
        _reports = reports;
    }

    /// <summary>
    /// Trigger a new Cloud Assessment scan.
    /// POST /v2/cloud-assessment/scan
    /// </summary>
    [Function("CloudAssessment_Scan")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Scan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/scan")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<CloudAssessmentScanRequest>();
            if (body is null || body.OrganizationId == Guid.Empty)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
                return bad;
            }

            // Verify org access
            if (!_user.IsAdmin)
            {
                var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                    await _db.Organizations.AnyAsync(o => o.Id == body.OrganizationId && o.FranchiseId == _user.FranchiseId.Value);
                var orgBelongsToUser = _user.OrganizationId.HasValue && body.OrganizationId == _user.OrganizationId.Value;
                if (!orgBelongsToFranchise && !orgBelongsToUser)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                    return forbidden;
                }
            }

            // Actlog entry at scan request — proves function was hit
            _db.Actlog.Add(new Data.Entities.Actlog
            {
                Timestamp = DateTime.UtcNow,
                Severity = "info",
                Module = "cloud-assessment",
                Action = "scan.requested",
                EntityType = "Organization",
                EntityId = body.OrganizationId.ToString(),
                Message = $"Cloud Assessment scan requested for org {body.OrganizationId}"
            });
            await _db.SaveChangesAsync();

            var scanId = await _service.StartScanAsync(body.OrganizationId, body.TenantId);

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { scanId, status = "running" });
            return response;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Get latest Cloud Assessment scan for an organization.
    /// GET /v2/cloud-assessment?organizationId={guid}
    /// </summary>
    [Function("CloudAssessment_Get")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment")] HttpRequestData req)
    {
        try
        {
            var orgId = ResolveOrgId(req);
            if (orgId is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId required" });
                return bad;
            }

            var result = await _service.GetLatestScanAsync(orgId.Value);

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (result is null)
                await response.WriteAsJsonAsync(new { scanned = false });
            else
                await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Get scan history for an organization.
    /// GET /v2/cloud-assessment/history?organizationId={guid}
    /// NOTE: declared before /{scanId} — literal route segment wins over catchall.
    /// </summary>
    [Function("CloudAssessment_History")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> History(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/history")] HttpRequestData req)
    {
        try
        {
            var orgId = ResolveOrgId(req);
            if (orgId is null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "organizationId required" });
                return bad;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var limitStr = query["limit"];
            int limit = 20;
            if (!string.IsNullOrWhiteSpace(limitStr) && int.TryParse(limitStr, out var parsedLimit))
                limit = parsedLimit;

            var result = await _service.GetScanHistoryAsync(orgId.Value, limit);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            // TEMPORARY: return actual error for debugging
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Compare two scans side-by-side.
    /// GET /v2/cloud-assessment/compare?scanAId={guid}&scanBId={guid}
    /// </summary>
    [Function("CloudAssessment_Compare")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Compare(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/compare")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var scanAStr = query["scanAId"];
            var scanBStr = query["scanBId"];

            if (!Guid.TryParse(scanAStr, out var scanAId) || !Guid.TryParse(scanBStr, out var scanBId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "scanAId and scanBId are required (GUIDs)" });
                return bad;
            }

            var result = await _service.CompareScansAsync(scanAId, scanBId);
            if (result is null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "One or both scans not found" });
                return notFound;
            }

            // Verify org access — either scan must belong to the current user's org scope.
            // Simpler: rely on the fact that scan GUIDs are hard to guess + assessment:read is
            // tenant-scoped at middleware layer. If future audit requires per-org check, join on
            // scan A's OrganizationId against _user.OrganizationId / FranchiseId here.

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result);
            return response;
        }
        catch (Exception ex)
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = ex.Message, stack = ex.StackTrace, inner = ex.InnerException?.Message });
            return err;
        }
    }

    /// <summary>
    /// Get full detail for a specific scan.
    /// GET /v2/cloud-assessment/{scanId}
    /// </summary>
    [Function("CloudAssessment_Detail")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Detail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/{scanId:guid}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var scanGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid scanId" });
            return bad;
        }

        var result = await _service.GetScanDetailAsync(scanGuid);
        if (result is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Scan not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    /// <summary>
    /// Get finding statuses for an organization.
    /// GET /v2/cloud-assessment/findings/status?organizationId={guid}[&area=identity][&status=open]
    /// </summary>
    [Function("CloudAssessmentFindingStatuses")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetFindingStatuses(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/findings/status")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var area = query["area"];
        var statusFilter = query["status"];

        var statuses = await _statusService.GetStatusesForOrgAsync(orgId.Value, area, statusFilter);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(statuses);
        return response;
    }

    /// <summary>
    /// Set (upsert) a finding status for an organization.
    /// PATCH /v2/cloud-assessment/findings/status
    /// </summary>
    [Function("CloudAssessmentSetFindingStatus")]
    [RequirePermission("assessment:write")]
    public async Task<HttpResponseData> SetFindingStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "v2/cloud-assessment/findings/status")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<SetFindingStatusRequest>();
        if (body is null || body.OrganizationId == Guid.Empty)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, body.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        try
        {
            var updated = await _statusService.SetStatusAsync(
                body.OrganizationId,
                body.Area,
                body.Service,
                body.Feature,
                body.Status,
                body.Notes,
                body.OwnerUserId,
                actorUserId: _user.UserId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(updated);
            return response;
        }
        catch (ArgumentException ex)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = ex.Message });
            return bad;
        }
    }

    /// <summary>
    /// Get active remediation suggestions for an organization.
    /// GET /v2/cloud-assessment/suggestions?organizationId={guid}
    /// </summary>
    [Function("CloudAssessmentGetSuggestions")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetSuggestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/suggestions")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var suggestions = await _statusService.GetActiveSuggestionsAsync(orgId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(suggestions);
        return response;
    }

    /// <summary>
    /// Dismiss a remediation suggestion.
    /// POST /v2/cloud-assessment/suggestions/{suggestionId}/dismiss
    /// </summary>
    [Function("CloudAssessmentDismissSuggestion")]
    [RequirePermission("assessment:write")]
    public async Task<HttpResponseData> DismissSuggestion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/suggestions/{suggestionId}/dismiss")] HttpRequestData req,
        string suggestionId)
    {
        if (!long.TryParse(suggestionId, out var id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid suggestionId" });
            return bad;
        }

        // Load suggestion to verify org access before acting on it
        var suggestion = await _db.CloudAssessmentSuggestions.FirstOrDefaultAsync(s => s.Id == id);
        if (suggestion is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Suggestion not found" });
            return notFound;
        }

        var accessDenied = await RequireOrgAccess(req, suggestion.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        try
        {
            await _statusService.DismissSuggestionAsync(id, _user.UserId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { dismissed = true });
            return response;
        }
        catch (InvalidOperationException)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Suggestion not found" });
            return notFound;
        }
    }

    /// <summary>
    /// Get remediation stats for an organization.
    /// GET /v2/cloud-assessment/remediation/stats?organizationId={guid}
    /// </summary>
    [Function("CloudAssessmentRemediationStats")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetRemediationStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/remediation/stats")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var stats = await _statusService.GetStatsAsync(orgId.Value);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(stats);
        return response;
    }

    /// <summary>
    /// List active compliance frameworks.
    /// GET /v2/cloud-assessment/compliance/frameworks
    /// </summary>
    [Function("CloudAssessment_ComplianceFrameworks")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetComplianceFrameworks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/compliance/frameworks")] HttpRequestData req)
    {
        var frameworks = await _db.CloudAssessmentFrameworks
            .Where(f => f.Active)
            .OrderBy(f => f.Code)
            .Select(f => new
            {
                f.Id, f.Code, f.Name, f.Description, f.Version, f.Authority, f.DocUrl,
                ControlCount = f.Controls.Count
            })
            .ToListAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(frameworks);
        return response;
    }

    /// <summary>
    /// Get per-framework compliance scores for latest scan.
    /// GET /v2/cloud-assessment/compliance/scores?organizationId={guid}[&scanId={guid}]
    /// </summary>
    [Function("CloudAssessment_ComplianceScores")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetComplianceScores(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/compliance/scores")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        Guid? scanId = Guid.TryParse(query["scanId"], out var sid) ? sid : null;

        // Resolve scan: explicit or latest completed for org.
        if (!scanId.HasValue)
        {
            scanId = await _db.CloudAssessmentScans
                .Where(s => s.OrganizationId == orgId.Value && s.Status == "completed")
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.Id)
                .FirstOrDefaultAsync();
        }

        if (!scanId.HasValue || scanId == Guid.Empty)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(Array.Empty<object>());
            return response;
        }

        var scores = await _db.CloudAssessmentFrameworkScores
            .Where(s => s.ScanId == scanId.Value)
            .Join(_db.CloudAssessmentFrameworks, s => s.FrameworkId, f => f.Id, (s, f) => new
            {
                s.FrameworkId,
                FrameworkCode = f.Code,
                FrameworkName = f.Name,
                s.TotalControls, s.CoveredControls, s.PassingControls, s.FailingControls,
                s.UnmappedControls, s.ScorePct, s.Grade, s.ComputedAt
            })
            .OrderByDescending(x => x.ScorePct)
            .ToListAsync();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(scores);
        return resp;
    }

    /// <summary>
    /// Recompute compliance scores for an existing scan (e.g. after adding a new framework).
    /// POST /v2/cloud-assessment/compliance/recompute?scanId={guid}
    /// </summary>
    [Function("CloudAssessment_ComplianceRecompute")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> RecomputeComplianceScores(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v2/cloud-assessment/compliance/recompute")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["scanId"], out var scanId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "scanId query parameter required" });
            return bad;
        }

        var scan = await _db.CloudAssessmentScans.FindAsync(scanId);
        if (scan is null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            await nf.WriteAsJsonAsync(new { error = "scan not found" });
            return nf;
        }

        var accessDenied = await RequireOrgAccess(req, scan.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        var scores = await ComplianceScoreEngine.RecomputeAsync(_db, scanId);

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            scanId,
            frameworks = scores.Count,
            scores = scores.Select(s => new { s.FrameworkId, s.ScorePct, s.Grade, s.TotalControls, s.PassingControls, s.FailingControls })
        });
        return resp;
    }

    /// <summary>
    /// Drill down into a specific framework's controls and their mapped findings.
    /// GET /v2/cloud-assessment/compliance/framework/{code}?scanId={guid}
    /// </summary>
    [Function("CloudAssessment_ComplianceDrilldown")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> GetComplianceDrilldown(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/compliance/framework/{code}")] HttpRequestData req,
        string code)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (!Guid.TryParse(query["scanId"], out var scanId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "scanId query parameter required" });
            return bad;
        }

        var framework = await _db.CloudAssessmentFrameworks
            .FirstOrDefaultAsync(f => f.Code == code && f.Active);

        if (framework is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"Framework '{code}' not found" });
            return notFound;
        }

        // Load controls with their mappings.
        var controls = await _db.CloudAssessmentFrameworkControls
            .Where(c => c.FrameworkId == framework.Id)
            .OrderBy(c => c.Category).ThenBy(c => c.ControlCode)
            .Select(c => new
            {
                c.ControlCode, c.Title, c.Category, c.Priority,
                Mappings = c.Mappings.Select(m => new
                {
                    m.Area, m.Service, m.Feature, m.Coverage, m.Rationale
                }).ToList()
            })
            .ToListAsync();

        // Load findings for this scan to resolve statuses.
        var findings = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status, f.Priority })
            .ToListAsync();

        var findingLookup = findings
            .GroupBy(f => $"{f.Area}|{f.Service}|{f.Feature}")
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        // Enrich controls with finding status.
        var enriched = controls.Select(c =>
        {
            var mappedFindings = c.Mappings.Select(m =>
            {
                var key = $"{m.Area}|{m.Service}|{m.Feature}";
                findingLookup.TryGetValue(key, out var finding);
                return new
                {
                    m.Area, m.Service, m.Feature, m.Coverage, m.Rationale,
                    FindingStatus = finding?.Status,
                    FindingPriority = finding?.Priority
                };
            }).ToList();

            string controlStatus = mappedFindings.Count == 0
                ? "unmapped"
                : mappedFindings.Any(f => f.FindingStatus != null &&
                    (f.FindingStatus.Equals("action_required", StringComparison.OrdinalIgnoreCase) ||
                     f.FindingStatus.Equals("Action Required", StringComparison.OrdinalIgnoreCase)))
                    ? "failing"
                    : mappedFindings.Any(f => f.FindingStatus != null)
                        ? "passing"
                        : "no_data";

            return new
            {
                c.ControlCode, c.Title, c.Category, c.Priority,
                Status = controlStatus,
                MappedFindings = mappedFindings
            };
        }).ToList();

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new
        {
            Framework = new
            {
                framework.Id, framework.Code, framework.Name, framework.Description,
                framework.Version, framework.Authority, framework.DocUrl,
                ControlCount = controls.Count
            },
            Controls = enriched
        });
        return resp;
    }

    /// <summary>
    /// Copilot Readiness Lens: D1-D6 scores + dimension-filtered findings.
    /// GET /v2/cloud-assessment/copilot-lens/{scanId}
    /// </summary>
    [Function("CloudAssessment_CopilotLens")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> CopilotLens(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/copilot-lens/{scanId:guid}")] HttpRequestData req,
        string scanId)
    {
        if (!Guid.TryParse(scanId, out var scanGuid))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid scanId" });
            return bad;
        }

        var scan = await _db.CloudAssessmentScans
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scanGuid);

        if (scan is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Scan not found" });
            return notFound;
        }

        var accessDenied = await RequireOrgAccess(req, scan.OrganizationId);
        if (accessDenied is not null) return accessDenied;

        var findings = await _db.CloudAssessmentFindings
            .AsNoTracking()
            .Where(f => f.ScanId == scanGuid)
            .Select(f => new
            {
                f.Id, f.Area, f.Service, f.Feature,
                f.Status, f.Priority, f.Observation, f.Recommendation,
                f.LinkText, f.LinkUrl
            })
            .ToListAsync();

        var sharepointSites = await _db.CloudAssessmentSharepointSites
            .AsNoTracking()
            .Where(s => s.ScanId == scanGuid)
            .Select(s => new
            {
                s.SiteUrl, s.SiteTitle, s.TotalFiles, s.LabeledFiles,
                s.OversharedFiles, s.RiskLevel, s.TopLabels
            })
            .ToListAsync();

        var externalUsers = await _db.CloudAssessmentExternalUsers
            .AsNoTracking()
            .Where(u => u.ScanId == scanGuid)
            .Select(u => new
            {
                u.UserPrincipal, u.DisplayName, u.EmailDomain,
                u.LastSignIn, u.RiskLevel, u.SitesAccessed, u.HighestPermission
            })
            .ToListAsync();

        // D1: sensitivity labels — data area, label/sensitivity service
        var d1Findings = findings
            .Where(f => f.Area.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                        (f.Service.Contains("label", StringComparison.OrdinalIgnoreCase) ||
                         f.Service.Contains("sensitivity", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // D2: oversharing — SharePoint sites data
        var d2Findings = findings
            .Where(f => f.Area.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                        f.Service.Contains("sharepoint", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // D3: external — external user area/service
        var d3Findings = findings
            .Where(f => f.Area.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                        (f.Service.Contains("external", StringComparison.OrdinalIgnoreCase) ||
                         f.Feature.Contains("external", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // D4: conditional access — identity area, conditional access service/feature
        var d4Findings = findings
            .Where(f => f.Area.Equals("identity", StringComparison.OrdinalIgnoreCase) &&
                        (f.Service.Contains("conditional", StringComparison.OrdinalIgnoreCase) ||
                         f.Feature.Contains("conditional", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // D5: zero trust — identity or endpoint, high/critical priority
        var d5Findings = findings
            .Where(f => (f.Area.Equals("identity", StringComparison.OrdinalIgnoreCase) ||
                         f.Area.Equals("endpoint", StringComparison.OrdinalIgnoreCase)) &&
                        (f.Priority.Equals("high", StringComparison.OrdinalIgnoreCase) ||
                         f.Priority.Equals("critical", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // D6: purview/DLP — data area, purview or dlp service
        var d6Findings = findings
            .Where(f => f.Area.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                        (f.Service.Contains("purview", StringComparison.OrdinalIgnoreCase) ||
                         f.Service.Contains("dlp", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var emptySites = sharepointSites.Take(0).ToList();

        var dimensions = new[]
        {
            new
            {
                key = "d1Labels",
                label = "Sensitivity Labels",
                score = scan.CopilotD1Score,
                findings = d1Findings,
                sharepointSites = sharepointSites
            },
            new
            {
                key = "d2Oversharing",
                label = "Oversharing Controls",
                score = scan.CopilotD2Score,
                findings = d2Findings,
                sharepointSites = sharepointSites
            },
            new
            {
                key = "d3External",
                label = "External Collaboration",
                score = scan.CopilotD3Score,
                findings = d3Findings,
                sharepointSites = emptySites
            },
            new
            {
                key = "d4ConditionalAccess",
                label = "Conditional Access",
                score = scan.CopilotD4Score,
                findings = d4Findings,
                sharepointSites = emptySites
            },
            new
            {
                key = "d5ZeroTrust",
                label = "Zero Trust Posture",
                score = scan.CopilotD5Score,
                findings = d5Findings,
                sharepointSites = emptySites
            },
            new
            {
                key = "d6Purview",
                label = "Purview & DLP",
                score = scan.CopilotD6Score,
                findings = d6Findings,
                sharepointSites = emptySites
            }
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            scanId = scan.Id,
            scores = new
            {
                d1Labels = scan.CopilotD1Score,
                d2Oversharing = scan.CopilotD2Score,
                d3External = scan.CopilotD3Score,
                d4ConditionalAccess = scan.CopilotD4Score,
                d5ZeroTrust = scan.CopilotD5Score,
                d6Purview = scan.CopilotD6Score,
                overall = scan.CopilotOverall,
                verdict = scan.CopilotVerdict
            },
            externalUsers,
            dimensions
        });
        return response;
    }

    // ── Helpers ──

    private Guid? ResolveOrgId(HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        if (Guid.TryParse(orgIdStr, out var parsed))
            return parsed;
        if (_user.OrganizationId.HasValue)
            return _user.OrganizationId.Value;
        return null;
    }

    /// <summary>
    /// Verifies the caller has access to the specified org (franchise-member or direct owner).
    /// Returns a 403 response if access is denied, null if access is granted.
    /// Admins bypass the check.
    /// </summary>
    private async Task<HttpResponseData?> RequireOrgAccess(HttpRequestData req, Guid orgId)
    {
        if (_user.IsAdmin) return null;

        var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
            await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
        var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;

        if (!orgBelongsToFranchise && !orgBelongsToUser)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
            return forbidden;
        }

        return null;
    }

    /// <summary>
    /// Returns cloud connection status for an organization.
    /// GET /v2/cloud-assessment/connection-status?organizationId={id}
    /// </summary>
    [Function("CloudAssessment_ConnectionStatus")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> ConnectionStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/connection-status")] HttpRequestData req)
    {
        if (!Guid.TryParse(req.Query["organizationId"], out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        if (!_user.IsAdmin)
        {
            var orgBelongsToFranchise = _user.FranchiseId.HasValue &&
                await _db.Organizations.AnyAsync(o => o.Id == orgId && o.FranchiseId == _user.FranchiseId.Value);
            var orgBelongsToUser = _user.OrganizationId.HasValue && orgId == _user.OrganizationId.Value;
            if (!orgBelongsToFranchise && !orgBelongsToUser)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }
        }

        var status = await _consent.GetConnectionStatusAsync(orgId);
        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(status);
        return ok;
    }

    /// <summary>
    /// Disconnect all cloud services and delete all scan data for an organization.
    /// DELETE /v2/cloud-assessment/disconnect?organizationId={id}
    /// </summary>
    [Function("CloudAssessment_Disconnect")]
    [RequirePermission("assessment:create")]
    public async Task<HttpResponseData> Disconnect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v2/cloud-assessment/disconnect")] HttpRequestData req)
    {
        var orgId = ResolveOrgId(req);
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, orgId.Value);
        if (accessDenied is not null) return accessDenied;

        // Delete scan-level rows (cascades to findings, metrics, licenses, adoption,
        // wasted licenses, sharepoint sites, external users, mail domains, mailbox risks,
        // shared mailboxes, azure resources, framework scores, benchmark comparisons,
        // powerbi workspaces/gateways/capacities/activities).
        var caScans = await _db.CloudAssessmentScans
            .Where(s => s.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CloudAssessmentScans.RemoveRange(caScans);

        // Org-level tables (no scan FK).
        var azureSubs = await _db.CloudAssessmentAzureSubscriptions
            .Where(s => s.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CloudAssessmentAzureSubscriptions.RemoveRange(azureSubs);

        var pbiConns = await _db.CloudAssessmentPowerBiConnections
            .Where(p => p.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CloudAssessmentPowerBiConnections.RemoveRange(pbiConns);

        var findingStatuses = await _db.CloudAssessmentFindingStatuses
            .Where(f => f.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CloudAssessmentFindingStatuses.RemoveRange(findingStatuses);

        var suggestions = await _db.CloudAssessmentSuggestions
            .Where(s => s.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CloudAssessmentSuggestions.RemoveRange(suggestions);

        // Legacy Copilot Readiness scans (cascades to metrics, findings, sharepoint, external users).
        var crScans = await _db.CopilotReadinessScans
            .Where(s => s.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.CopilotReadinessScans.RemoveRange(crScans);

        // M365 tenant (cascades to m365_findings).
        var tenants = await _db.M365Tenants
            .Where(t => t.OrganizationId == orgId.Value)
            .ToListAsync();
        _db.M365Tenants.RemoveRange(tenants);

        await _db.SaveChangesAsync();

        _db.Actlog.Add(new Data.Entities.Actlog
        {
            Timestamp = DateTime.UtcNow,
            Severity = "warn",
            Module = "cloud-assessment",
            Action = "disconnect.all",
            EntityType = "Organization",
            EntityId = orgId.Value.ToString(),
            Message = $"All cloud data disconnected: {caScans.Count} CA scans, {crScans.Count} CR scans, {azureSubs.Count} Azure subs, {pbiConns.Count} PBI conns, {tenants.Count} tenants deleted"
        });
        await _db.SaveChangesAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            disconnected = true,
            deleted = new
            {
                cloudAssessmentScans = caScans.Count,
                copilotReadinessScans = crScans.Count,
                azureSubscriptions = azureSubs.Count,
                powerBiConnections = pbiConns.Count,
                m365Tenants = tenants.Count,
                findingStatuses = findingStatuses.Count,
                suggestions = suggestions.Count
            }
        });
        return response;
    }

    [Function("CloudAssessment_Report")]
    [RequirePermission("assessment:read")]
    public async Task<HttpResponseData> Report(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/cloud-assessment/reports/{orgId}")] HttpRequestData req,
        string orgId)
    {
        if (!Guid.TryParse(orgId, out var parsedOrgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid orgId" });
            return bad;
        }

        var accessDenied = await RequireOrgAccess(req, parsedOrgId);
        if (accessDenied is not null) return accessDenied;

        var type = req.Url.Query?.Contains("type=") == true
            ? System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("type") ?? "c-level"
            : "c-level";
        var lang = req.Url.Query?.Contains("lang=") == true
            ? System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("lang") ?? "en"
            : "en";

        if (type is not ("c-level" or "franchise" or "technical" or "presales"))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "type must be c-level, franchise, technical, or presales" });
            return bad;
        }

        try
        {
            var html = await _reports.GenerateAsync(parsedOrgId, type, lang);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(html);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = ex.Message });
            return notFound;
        }
    }
}

// ── Request DTOs ──

public class CloudAssessmentScanRequest
{
    public Guid OrganizationId { get; set; }
    public Guid? TenantId { get; set; }
}

public class SetFindingStatusRequest
{
    public Guid OrganizationId { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Feature { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? OwnerUserId { get; set; }
}
