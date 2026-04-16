using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// SharePoint Deep pipeline: scans SharePoint sites for sensitivity label coverage (D1),
/// oversharing patterns (D2), and enumerates external/guest users for risk classification (D3).
/// </summary>
public static class SharePointDeepPipeline
{
    private const int MaxSitesPerRun  = 50;
    private const int MaxFilesPerSite = 500;
    private const int FilesPageSize   = 200;
    private const int SiteConcurrency = 5;

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        ILogger log,
        CancellationToken ct)
    {
        var siteResults   = new List<SharepointSiteResult>();
        var externalUsers = new List<ExternalUserResult>();

        // Run site scan and guest enumeration in parallel.
        var sitesTask  = CollectSitesAsync(graph, siteResults, log, ct);
        var guestsTask = CollectGuestUsersAsync(graph, externalUsers, log, ct);

        await Task.WhenAll(sitesTask, guestsTask);

        var (sitesStatus, sampled) = sitesTask.Result;
        var guestsStatus           = guestsTask.Result;

        // Merge status: if either collector reported partial, mark the pipeline partial.
        string pipelineStatus = (sitesStatus == "partial" || guestsStatus == "partial")
            ? "partial" : "ok";

        // Aggregate label / oversharing metrics.
        long totalFiles    = siteResults.Sum(s => (long)s.TotalFiles);
        long labeledFiles  = siteResults.Sum(s => (long)s.LabeledFiles);
        long oversharedF   = siteResults.Sum(s => (long)s.OversharedFiles);

        double labelPct    = totalFiles > 0 ? labeledFiles  * 100.0 / totalFiles : 0;
        double overshPct   = totalFiles > 0 ? oversharedF   * 100.0 / totalFiles : 0;

        // Aggregate external user metrics.
        int highRiskExt  = externalUsers.Count(u => u.RiskLevel == "High");
        int pendingInv   = externalUsers.Count(u => u.LastSignIn is null
                                                    && u.RiskLevel == "Low"); // created recently, no sign-in
        int inactiveExt  = externalUsers.Count(u => u.RiskLevel == "Medium");
        int totalGuests  = externalUsers.Count;

        var result = new PipelineResult { PipelineName = "sharepoint_deep", Status = pipelineStatus };

        result.Metrics["label_coverage_pct"]        = labelPct.ToString("F1");
        result.Metrics["overshared_pct"]             = overshPct.ToString("F1");
        result.Metrics["total_documents_scanned"]    = totalFiles.ToString();
        result.Metrics["total_sites_scanned"]        = siteResults.Count.ToString();
        result.Metrics["high_risk_external_users"]   = highRiskExt.ToString();
        result.Metrics["pending_invitations"]        = pendingInv.ToString();
        result.Metrics["total_external_users"]       = totalGuests.ToString();
        result.Metrics["inactive_external_users"]    = inactiveExt.ToString();
        result.Metrics["sampled"]                    = sampled ? "true" : "false";

        result.SharepointSites.AddRange(siteResults);
        result.ExternalUsers.AddRange(externalUsers);

        return result;
    }

    // ================================================================
    // Collector 1: SharePoint Sites — label coverage + oversharing (D1 + D2)
    // ================================================================
    private static async Task<(string status, bool sampled)> CollectSitesAsync(
        GraphServiceClient graph,
        List<SharepointSiteResult> results,
        ILogger log,
        CancellationToken ct)
    {
        bool sampled = false;
        try
        {
            var sitesResp = await graph.Sites.GetAsync(rc =>
            {
                rc.QueryParameters.Top    = MaxSitesPerRun;
                rc.QueryParameters.Select = new[] { "id", "displayName", "webUrl" };
            }, cancellationToken: ct);

            var sites = sitesResp?.Value;
            if (sites is null || sites.Count == 0)
                return ("ok", false);

            if (sites.Count >= MaxSitesPerRun)
                sampled = true;

            var sem = new SemaphoreSlim(SiteConcurrency);
            var tasks = sites.Select(site => ProcessSiteAsync(graph, site, sem, log, ct)).ToList();

            var siteOutputs = await Task.WhenAll(tasks);

            foreach (var (siteResult, hitCap) in siteOutputs)
            {
                if (siteResult is not null)
                {
                    results.Add(siteResult);
                    if (hitCap) sampled = true;
                }
            }

            return ("ok", sampled);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("SharePoint sites: 403 - insufficient permissions. Skipping site scan.");
            return ("partial", false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "SharePoint site enumeration failed");
            return ("partial", false);
        }
    }

    private static async Task<(SharepointSiteResult? result, bool hitCap)> ProcessSiteAsync(
        GraphServiceClient graph,
        Site site,
        SemaphoreSlim sem,
        ILogger log,
        CancellationToken ct)
    {
        await sem.WaitAsync(ct);
        try
        {
            var siteId    = site.Id ?? "";
            var siteUrl   = site.WebUrl ?? "";
            var siteTitle = site.DisplayName;

            if (string.IsNullOrEmpty(siteId))
                return (null, false);

            // Find the default document library drive.
            string? driveId = null;
            try
            {
                var drivesResp = await graph.Sites[siteId].Drives.GetAsync(cancellationToken: ct);
                var docLib = drivesResp?.Value?.FirstOrDefault(d =>
                    string.Equals(d.DriveType, "documentLibrary", StringComparison.OrdinalIgnoreCase));
                driveId = docLib?.Id;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
            {
                log.LogWarning("Site {SiteId}: cannot access drives ({Code})", siteId, ex.ResponseStatusCode);
                return (null, false);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Site {SiteId}: drives enumeration failed", siteId);
                return (null, false);
            }

            if (driveId is null)
                return (null, false);

            // Scan files for sensitivity labels and sharing state.
            int  totalFiles    = 0;
            int  labeledFiles  = 0;
            int  oversharedFiles = 0;
            bool hitCap        = false;
            var  topLabels     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string? nextLink = null;
                bool firstPage   = true;

                do
                {
                    DriveItemCollectionResponse? pageResp;

                    if (firstPage)
                    {
                        pageResp = await graph.Drives[driveId].Items["root"].Children.GetAsync(rc =>
                        {
                            rc.QueryParameters.Top    = FilesPageSize;
                            rc.QueryParameters.Select = new[]
                            {
                                "id", "name", "file", "sensitivityLabel", "shared"
                            };
                        }, cancellationToken: ct);
                        firstPage = false;
                    }
                    else
                    {
                        // Follow the next page link manually via a raw request.
                        var reqInfo = new Microsoft.Kiota.Abstractions.RequestInformation
                        {
                            HttpMethod = Microsoft.Kiota.Abstractions.Method.GET,
                            URI        = new Uri(nextLink!)
                        };
                        pageResp = await graph.RequestAdapter
                            .SendAsync(reqInfo,
                                DriveItemCollectionResponse.CreateFromDiscriminatorValue,
                                cancellationToken: ct);
                    }

                    nextLink = pageResp?.OdataNextLink;
                    var items = pageResp?.Value;
                    if (items is null) break;

                    foreach (var item in items)
                    {
                        // Only process files (not folders).
                        if (item.File is null) continue;

                        totalFiles++;

                        // Sensitivity label — present on the item's AdditionalData when the
                        // tenant has Purview labels applied.
                        bool hasLabel = false;
                        if (item.AdditionalData?.TryGetValue("sensitivityLabel", out var labelObj) == true
                            && labelObj is not null)
                        {
                            hasLabel = true;
                            // Attempt to extract label name from the nested object.
                            if (labelObj is System.Text.Json.JsonElement je
                                && je.TryGetProperty("displayName", out var dnProp))
                            {
                                var dn = dnProp.GetString();
                                if (!string.IsNullOrEmpty(dn))
                                    topLabels.Add(dn);
                            }
                        }
                        if (hasLabel) labeledFiles++;

                        // Oversharing — check the shared facet.
                        bool isOvershared = false;
                        if (item.Shared is not null)
                        {
                            // Scope "organization" means shared with everyone in the org.
                            var scope = item.Shared.Scope?.ToLowerInvariant() ?? "";
                            if (scope == "organization" || scope == "public")
                                isOvershared = true;
                        }
                        if (isOvershared) oversharedFiles++;
                    }

                    if (totalFiles >= MaxFilesPerSite)
                    {
                        hitCap = true;
                        break;
                    }
                }
                while (!string.IsNullOrEmpty(nextLink));
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
            {
                log.LogWarning("Site {SiteId}: cannot list files ({Code})", siteId, ex.ResponseStatusCode);
                // Return what we have (may be zero).
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Site {SiteId}: file listing failed", siteId);
            }

            // Check site-level permissions for org-wide grants.
            bool siteIsOvershared = false;
            try
            {
                var permsResp = await graph.Sites[siteId].Permissions.GetAsync(cancellationToken: ct);
                if (permsResp?.Value is not null)
                {
                    foreach (var perm in permsResp.Value)
                    {
                        // GrantedToIdentitiesV2 or GrantedTo may reference the "Everyone except external" group.
                        if (perm.GrantedToV2?.User?.DisplayName is { } name
                            && (name.Contains("Everyone", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("EveryoneExceptExternalUsers", StringComparison.OrdinalIgnoreCase)))
                        {
                            siteIsOvershared = true;
                            break;
                        }

                        // Sharing link scopes
                        if (perm.Link?.Scope is { } scope
                            && (scope.ToLowerInvariant() == "organization"
                                || scope.ToLowerInvariant() == "anonymous"))
                        {
                            siteIsOvershared = true;
                            break;
                        }
                    }
                }
            }
            catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 404)
            {
                log.LogWarning("Site {SiteId}: cannot read permissions ({Code})", siteId, ex.ResponseStatusCode);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Site {SiteId}: permissions check failed", siteId);
            }

            // If the site itself has org-wide permissions, count all its files as overshared.
            if (siteIsOvershared && totalFiles > 0)
                oversharedFiles = Math.Max(oversharedFiles, totalFiles);

            // Compute risk level.
            double oversharedPct = totalFiles > 0 ? oversharedFiles * 100.0 / totalFiles : 0;
            string riskLevel = oversharedFiles > 50 || oversharedPct > 30 ? "High"
                             : oversharedFiles > 10                        ? "Medium"
                             :                                               "Low";

            var siteResult = new SharepointSiteResult
            {
                SiteUrl       = siteUrl,
                SiteTitle     = siteTitle,
                TotalFiles    = totalFiles,
                LabeledFiles  = labeledFiles,
                OversharedFiles = oversharedFiles,
                RiskLevel     = riskLevel,
                TopLabels     = topLabels.Take(10).ToList(),
            };

            return (siteResult, hitCap);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Site {SiteId}: unexpected error during processing", site.Id);
            return (null, false);
        }
        finally
        {
            sem.Release();
        }
    }

    // ================================================================
    // Collector 2: External / Guest users (D3)
    // ================================================================
    private static async Task<string> CollectGuestUsersAsync(
        GraphServiceClient graph,
        List<ExternalUserResult> results,
        ILogger log,
        CancellationToken ct)
    {
        try
        {
            var guestResp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = "userType eq 'Guest'";
                rc.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName",
                    "createdDateTime", "signInActivity"
                };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            var guests = guestResp?.Value;
            if (guests is null) return ("ok");

            var now = DateTimeOffset.UtcNow;

            foreach (var g in guests)
            {
                var upn = g.UserPrincipalName ?? "";

                // Extract email domain from guest UPN.
                // Guest UPNs look like: user_contoso.com#EXT#@tenant.onmicrosoft.com
                // or: user@domain.com (when synced directly)
                string? emailDomain = null;
                var extIdx = upn.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase);
                if (extIdx > 0)
                {
                    // The local part before #EXT# encodes the original address with _ replacing @
                    var localPart = upn[..extIdx];
                    var atIdx     = localPart.LastIndexOf('_');
                    if (atIdx > 0)
                        emailDomain = localPart[(atIdx + 1)..];
                }
                else
                {
                    var atIdx = upn.LastIndexOf('@');
                    if (atIdx > 0)
                        emailDomain = upn[(atIdx + 1)..];
                }

                // Sign-in activity — Graph returns this as a complex type.
                DateTimeOffset? lastSignIn = null;
                if (g.SignInActivity?.LastSignInDateTime is { } lsdt)
                    lastSignIn = lsdt;

                DateTimeOffset? createdAt = g.CreatedDateTime;

                // Risk classification:
                //   High   — signed in within last 90 days (active external user)
                //   Medium — created > 180 days ago, never (or not recently) signed in (stale)
                //   Low    — recently created, no sign-in yet (pending invitation)
                string riskLevel;
                if (lastSignIn.HasValue && (now - lastSignIn.Value).TotalDays <= 90)
                {
                    riskLevel = "High";
                }
                else if (!lastSignIn.HasValue && createdAt.HasValue && (now - createdAt.Value).TotalDays > 30)
                {
                    // Invite sent > 30 days ago but never accepted
                    riskLevel = "Low";
                }
                else if (createdAt.HasValue && (now - createdAt.Value).TotalDays > 180)
                {
                    // Old account, no recent sign-in
                    riskLevel = "Medium";
                }
                else
                {
                    riskLevel = "Low";
                }

                results.Add(new ExternalUserResult
                {
                    UserPrincipal     = upn,
                    DisplayName       = g.DisplayName,
                    EmailDomain       = emailDomain,
                    LastSignIn        = lastSignIn,
                    RiskLevel         = riskLevel,
                    SitesAccessed     = 0,     // Per-user site enumeration is too expensive.
                    HighestPermission = null,  // Requires per-user site scan.
                });
            }

            return "ok";
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("SharePoint guest users: 403 - insufficient permissions. D3 metrics will be zero.");
            return "partial";
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "SharePoint guest user collection failed");
            return "partial";
        }
    }
}
