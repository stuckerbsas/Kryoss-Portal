using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Cloud Assessment (CA-2) endpoint pipeline.
///
/// Aggregates endpoint-management and Defender-for-Endpoint signals via
/// parallel collectors:
///
///   * Intune (Graph SDK): compliance policies, configuration profiles,
///     managed devices (totals + compliance state + owner type + OS + BitLocker),
///     managed-device overview fallback, iOS/Android app protection policies,
///     managed apps catalog, enrollment restrictions and Autopilot profiles.
///
///   * Defender for Endpoint (REST): machine risk distribution,
///     vulnerabilities, exposure score, security recommendations, software
///     inventory plus three endpoint-posture KQL queries (USB usage,
///     unsigned binaries, lateral-movement attempts).
///
/// Follows the same resiliency contract as <see cref="IdentityPipeline"/>:
/// each collector catches its own exceptions (403 = permission gap, non-fatal)
/// and marks the shared <c>CollectorErrorTracker</c>. Findings generation is
/// deferred to <c>EndpointRecommendations</c> (Task 2).
/// </summary>
public static class EndpointPipeline
{
    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient? defenderHttp,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "endpoint", Status = "ok" };
        var ins = new EndpointInsights();
        var err = new CollectorErrorTracker();

        try
        {
            var tasks = new List<Task>
            {
                // --- Intune ---
                CollectCompliancePolicies(graph, ins, err, log, ct),
                CollectConfigProfiles(graph, ins, err, log, ct),
                CollectManagedDevices(graph, ins, err, log, ct),
                CollectDeviceOverview(graph, ins, err, log, ct),
                CollectAppProtectionIos(graph, ins, err, log, ct),
                CollectAppProtectionAndroid(graph, ins, err, log, ct),
                CollectManagedApps(graph, ins, err, log, ct),
                CollectEnrollmentAndAutopilot(graph, ins, err, log, ct),
            };

            // --- Defender for Endpoint (only when HTTP client is configured) ---
            if (defenderHttp is not null)
            {
                tasks.Add(CollectDefenderMachines(defenderHttp, ins, err, log, ct));
                tasks.Add(CollectDefenderVulnerabilities(defenderHttp, ins, err, log, ct));
                tasks.Add(CollectDefenderExposureScore(defenderHttp, ins, err, log, ct));
                tasks.Add(CollectDefenderRecommendations(defenderHttp, ins, err, log, ct));
                tasks.Add(CollectDefenderSoftware(defenderHttp, ins, err, log, ct));
                tasks.Add(CollectAdvancedHunting(defenderHttp, ins, err, log, ct));
            }

            await Task.WhenAll(tasks);

            result.Findings.AddRange(EndpointRecommendations.Generate(ins));

            // Metrics (string-valued, snake_case).
            var m = result.Metrics;
            var inv = CultureInfo.InvariantCulture;

            // Intune
            m["device_compliance_policies"] = ins.DeviceCompliancePolicyCount.ToString(inv);
            m["device_config_profiles"] = ins.DeviceConfigProfileCount.ToString(inv);
            m["devices_total"] = ins.DevicesTotal.ToString(inv);
            m["devices_compliant"] = ins.DevicesCompliant.ToString(inv);
            m["devices_non_compliant"] = ins.DevicesNonCompliant.ToString(inv);
            m["devices_in_grace"] = ins.DevicesInGracePeriod.ToString(inv);
            m["devices_byod"] = ins.DevicesBYOD.ToString(inv);
            m["devices_corporate"] = ins.DevicesCorporate.ToString(inv);
            m["devices_windows"] = ins.DevicesWindows.ToString(inv);
            m["devices_ios"] = ins.DevicesIOS.ToString(inv);
            m["devices_android"] = ins.DevicesAndroid.ToString(inv);
            m["devices_macos"] = ins.DevicesMacOS.ToString(inv);
            m["devices_encrypted"] = ins.DevicesEncrypted.ToString(inv);
            m["app_protection_ios"] = ins.AppProtectionPoliciesIOS.ToString(inv);
            m["app_protection_android"] = ins.AppProtectionPoliciesAndroid.ToString(inv);
            m["managed_apps"] = ins.ManagedAppCount.ToString(inv);
            m["enrollment_restrictions"] = ins.EnrollmentRestrictionCount.ToString(inv);
            m["autopilot_profiles"] = ins.AutopilotProfileCount.ToString(inv);

            // Defender for Endpoint
            m["machines_total"] = ins.MachinesTotal.ToString(inv);
            m["machines_high_risk"] = ins.MachinesHighRisk.ToString(inv);
            m["machines_medium_risk"] = ins.MachinesMediumRisk.ToString(inv);
            m["vuln_critical"] = ins.VulnCritical.ToString(inv);
            m["vuln_high"] = ins.VulnHigh.ToString(inv);
            m["vuln_medium"] = ins.VulnMedium.ToString(inv);
            m["vuln_low"] = ins.VulnLow.ToString(inv);
            m["exposure_score"] = ins.ExposureScore.ToString("F1", inv);
            m["exposure_risk"] = ins.ExposureRisk;
            m["unpatched_software"] = ins.UnpatchedSoftwareCount.ToString(inv);
            m["recommendations_total"] = ins.RecommendationsTotal.ToString(inv);
            m["recommendations_critical"] = ins.RecommendationsCritical.ToString(inv);
            m["software_total"] = ins.SoftwareTotal.ToString(inv);
            m["software_vulnerable"] = ins.SoftwareVulnerable.ToString(inv);

            // Advanced Hunting
            m["usb_usage_30d"] = ins.UsbUsageEvents.ToString(inv);
            m["unsigned_binaries_30d"] = ins.UnsignedBinariesLast30d.ToString(inv);
            m["lateral_movement_30d"] = ins.LateralMovementAttempts30d.ToString(inv);

            // Availability
            m["intune_available"] = ins.IntuneAvailable ? "true" : "false";
            m["defender_endpoint_available"] = ins.DefenderEndpointAvailable ? "true" : "false";

            // Final status: partial when any collector errored or neither surface responded.
            if (err.HadError || !ins.Available)
            {
                return new PipelineResult
                {
                    PipelineName = result.PipelineName,
                    Status = "partial",
                    Findings = result.Findings,
                    Metrics = result.Metrics,
                    SharepointSites = result.SharepointSites,
                    ExternalUsers = result.ExternalUsers,
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Endpoint pipeline top-level failure");
            return new PipelineResult
            {
                PipelineName = "endpoint",
                Status = "failed",
                Error = ex.Message,
            };
        }
    }

    // ================================================================
    // Helper: per-collector error tracking
    // ================================================================
    private sealed class CollectorErrorTracker
    {
        private int _errorCount;
        public bool HadError => Volatile.Read(ref _errorCount) > 0;
        public void MarkError() => Interlocked.Increment(ref _errorCount);
    }

    // ================================================================
    // Intune collectors
    // ================================================================

    private static async Task CollectCompliancePolicies(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceManagement.DeviceCompliancePolicies
                .GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.DeviceCompliancePolicyCount = resp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint compliance policies: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint compliance policies collection failed");
        }
    }

    private static async Task CollectConfigProfiles(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceManagement.DeviceConfigurations
                .GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.DeviceConfigProfileCount = resp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint config profiles: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint config profiles collection failed");
        }
    }

    private static async Task CollectManagedDevices(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceManagement.ManagedDevices.GetAsync(rc =>
            {
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.DevicesTotal = resp.Value.Count;

            foreach (var d in resp.Value)
            {
                var compliance = d.ComplianceState?.ToString()?.ToLowerInvariant() ?? "";
                if (compliance.Contains("compliant") && !compliance.Contains("non"))
                    ins.DevicesCompliant++;
                else if (compliance.Contains("noncompliant"))
                    ins.DevicesNonCompliant++;
                else if (compliance.Contains("ingraceperiod"))
                    ins.DevicesInGracePeriod++;

                var ownership = d.ManagedDeviceOwnerType?.ToString()?.ToLowerInvariant() ?? "";
                if (ownership.Contains("company")) ins.DevicesCorporate++;
                else if (ownership.Contains("personal")) ins.DevicesBYOD++;

                var os = d.OperatingSystem?.ToLowerInvariant() ?? "";
                if (os.Contains("windows"))
                    ins.DevicesWindows++;
                else if (os.Contains("ios") || os.Contains("ipados"))
                    ins.DevicesIOS++;
                else if (os.Contains("android"))
                    ins.DevicesAndroid++;
                else if (os.Contains("macos") || os.Contains("mac os"))
                    ins.DevicesMacOS++;

                if (d.IsEncrypted == true) ins.DevicesEncrypted++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint managed devices: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint managed devices collection failed");
        }
    }

    private static async Task CollectDeviceOverview(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        // Optional fallback — managed-device overview is not always populated
        // on fresh tenants; failure here is informational and non-fatal.
        try
        {
            var overview = await graph.DeviceManagement.ManagedDeviceOverview
                .GetAsync(cancellationToken: ct);
            if (overview is not null)
                ins.IntuneAvailable = true;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogInformation("Endpoint device overview: 403 - insufficient permissions (non-fatal)");
        }
        catch (Exception ex)
        {
            log.LogInformation(ex, "Endpoint device overview collection skipped");
        }
    }

    private static async Task CollectAppProtectionIos(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceAppManagement.IosManagedAppProtections
                .GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.AppProtectionPoliciesIOS = resp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint iOS app protection: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint iOS app protection collection failed");
        }
    }

    private static async Task CollectAppProtectionAndroid(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceAppManagement.AndroidManagedAppProtections
                .GetAsync(cancellationToken: ct);
            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.AppProtectionPoliciesAndroid = resp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint Android app protection: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint Android app protection collection failed");
        }
    }

    private static async Task CollectManagedApps(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.DeviceAppManagement.MobileApps.GetAsync(rc =>
            {
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;
            ins.IntuneAvailable = true;
            ins.ManagedAppCount = resp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint managed apps: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint managed apps collection failed");
        }
    }

    private static async Task CollectEnrollmentAndAutopilot(
        GraphServiceClient graph, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var enrollResp = await graph.DeviceManagement.DeviceEnrollmentConfigurations
                .GetAsync(cancellationToken: ct);
            if (enrollResp?.Value is not null)
            {
                ins.IntuneAvailable = true;
                ins.EnrollmentRestrictionCount = enrollResp.Value.Count;
            }

            // TODO CA-2: WindowsAutopilotDeploymentProfiles lives under the Graph
            // beta endpoint only; the v1 SDK (Microsoft.Graph 5.x) doesn't expose
            // DeviceManagement.WindowsAutopilotDeploymentProfiles directly. Switch
            // to the beta HTTP client (same pattern as IdentityPipeline.CollectGsa)
            // or upgrade SDK to pick this up. Leaving AutopilotProfileCount at 0.
            log.LogInformation(
                "Endpoint Autopilot profile count skipped: requires Graph beta endpoint");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            err.MarkError();
            log.LogWarning("Endpoint enrollment/Autopilot: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint enrollment/Autopilot collection failed");
        }
    }

    // ================================================================
    // Defender for Endpoint collectors
    // ================================================================

    private static async Task CollectDefenderMachines(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/api/machines", ct);
            if (!HandleDefenderResponse(resp, "machines", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderEndpointAvailable = true;
            ins.MachinesTotal = items.GetArrayLength();

            foreach (var m in items.EnumerateArray())
            {
                var risk = m.TryGetProperty("riskScore", out var r)
                    ? r.GetString()?.ToLowerInvariant() ?? "" : "";
                switch (risk)
                {
                    case "high": ins.MachinesHighRisk++; break;
                    case "medium": ins.MachinesMediumRisk++; break;
                    case "low": ins.MachinesLowRisk++; break;
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Defender machines collection failed");
        }
    }

    private static async Task CollectDefenderVulnerabilities(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/api/vulnerabilities", ct);
            if (!HandleDefenderResponse(resp, "vulnerabilities", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderEndpointAvailable = true;

            foreach (var v in items.EnumerateArray())
            {
                var severity = v.TryGetProperty("severity", out var s)
                    ? s.GetString()?.ToLowerInvariant() ?? "" : "";
                switch (severity)
                {
                    case "critical": ins.VulnCritical++; break;
                    case "high": ins.VulnHigh++; break;
                    case "medium": ins.VulnMedium++; break;
                    case "low": ins.VulnLow++; break;
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Defender vulnerabilities collection failed");
        }
    }

    private static async Task CollectDefenderExposureScore(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/api/exposureScore", ct);
            if (!HandleDefenderResponse(resp, "exposure_score", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            ins.DefenderEndpointAvailable = true;
            ins.ExposureScore = root.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
            ins.ExposureRisk = ins.ExposureScore switch
            {
                <= 30 => "Low",
                <= 60 => "Medium",
                _ => "High",
            };
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Defender exposure score collection failed");
        }
    }

    private static async Task CollectDefenderRecommendations(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/api/recommendations", ct);
            if (!HandleDefenderResponse(resp, "recommendations", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderEndpointAvailable = true;
            ins.RecommendationsTotal = items.GetArrayLength();

            foreach (var r in items.EnumerateArray())
            {
                var severity = r.TryGetProperty("severity", out var s)
                    ? s.GetString()?.ToLowerInvariant() ?? "" : "";
                if (severity == "critical") ins.RecommendationsCritical++;
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Defender recommendations collection failed");
        }
    }

    private static async Task CollectDefenderSoftware(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/api/Software", ct);
            if (!HandleDefenderResponse(resp, "software", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("value", out var items)) return;

            ins.DefenderEndpointAvailable = true;
            ins.SoftwareTotal = items.GetArrayLength();

            foreach (var app in items.EnumerateArray())
            {
                int weaknesses = 0;
                if (app.TryGetProperty("numberOfWeaknesses", out var w) && w.ValueKind == JsonValueKind.Number)
                    w.TryGetInt32(out weaknesses);
                if (weaknesses > 0)
                {
                    ins.SoftwareVulnerable++;
                    ins.UnpatchedSoftwareCount++;
                }
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Defender software collection failed");
        }
    }

    private static async Task CollectAdvancedHunting(
        HttpClient http, EndpointInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        const string usbQuery = """
            DeviceEvents
            | where Timestamp > ago(30d)
            | where ActionType in ('UsbDriveMounted', 'UsbDriveMount', 'PnpDeviceConnected')
            | summarize TotalEvents=count(), UniqueDevices=dcount(DeviceName)
            """;

        const string unsignedQuery = """
            DeviceProcessEvents
            | where Timestamp > ago(30d)
            | where InitiatingProcessSignatureStatus in ('Unsigned','Invalid','Unknown')
            | summarize TotalEvents=count()
            """;

        const string lateralQuery = """
            DeviceLogonEvents
            | where Timestamp > ago(30d)
            | where LogonType in ('Network','RemoteInteractive','NetworkCleartext')
            | where ActionType == 'LogonFailed'
            | summarize TotalEvents=count()
            """;

        var queries = new (string Name, string Kql, Action<EndpointInsights, JsonElement> Apply)[]
        {
            ("usb_usage", usbQuery,
                (i, results) => i.UsbUsageEvents += ExtractTotalEvents(results)),
            ("unsigned_binaries", unsignedQuery,
                (i, results) => i.UnsignedBinariesLast30d += ExtractTotalEvents(results)),
            ("lateral_movement", lateralQuery,
                (i, results) => i.LateralMovementAttempts30d += ExtractTotalEvents(results)),
        };

        var huntingTasks = queries.Select(q =>
            RunHuntingQuery(http, q.Name, q.Kql, q.Apply, ins, err, log, ct));
        await Task.WhenAll(huntingTasks);
    }

    private static async Task RunHuntingQuery(
        HttpClient http, string name, string kql,
        Action<EndpointInsights, JsonElement> apply,
        EndpointInsights ins, CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var payload = new { Query = kql };
            using var resp = await http.PostAsJsonAsync("/api/advancedhunting/run", payload, ct);
            if (!HandleDefenderResponse(resp, $"hunting_{name}", ins, log)) return;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("Results", out var results))
            {
                ins.DefenderEndpointAvailable = true;
                apply(ins, results);
            }
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Endpoint advanced-hunting query '{Name}' failed", name);
        }
    }

    /// <summary>
    /// Sums the <c>TotalEvents</c> column across all result rows emitted by a
    /// Defender advanced-hunting query. Missing / non-numeric values are
    /// treated as zero so a malformed row can't poison the metric.
    /// </summary>
    private static int ExtractTotalEvents(JsonElement results)
    {
        int total = 0;
        foreach (var r in results.EnumerateArray())
        {
            if (r.TryGetProperty("TotalEvents", out var te)
                && te.ValueKind == JsonValueKind.Number
                && te.TryGetInt32(out var value))
            {
                total += value;
            }
        }
        return total;
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Translates Defender-for-Endpoint REST responses into a boolean
    /// "should process body" signal. 403/404 flip <c>ActivationNeeded</c>
    /// but do NOT mark the pipeline errored — Defender for Endpoint may
    /// simply not be licensed or have no onboarded devices yet.
    /// </summary>
    private static bool HandleDefenderResponse(
        HttpResponseMessage resp, string endpoint, EndpointInsights ins, ILogger log)
    {
        if (resp.StatusCode == HttpStatusCode.OK)
            return true;

        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            ins.ActivationNeeded = true;
            log.LogWarning(
                "Defender API ({Endpoint}): 403 - insufficient permissions or no devices onboarded",
                endpoint);
            return false;
        }

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            ins.ActivationNeeded = true;
            log.LogWarning("Defender API ({Endpoint}): 404 - not available in current SKU", endpoint);
            return false;
        }

        log.LogWarning("Defender API ({Endpoint}): HTTP {StatusCode}",
            endpoint, (int)resp.StatusCode);
        return false;
    }
}
