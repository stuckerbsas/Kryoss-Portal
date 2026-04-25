using System.Diagnostics;
using System.Net;
using System.Reflection;
using KryossApi.Data;
using KryossApi.Services;
using KryossApi.Services.Reports;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KryossApi.Functions.Portal;

public class VersionFunction
{
    private static readonly string _version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    private static readonly string _buildTime =
        File.GetLastWriteTimeUtc(Assembly.GetExecutingAssembly().Location).ToString("o");

    [Function("Version")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/version")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            service = "KryossApi",
            version = _version,
            build = _buildTime,
            runtime = Environment.Version.ToString(),
            environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "unknown"
        });
        return response;
    }

    /// <summary>
    /// TEMPORARY public diagnostic — no auth. DELETE after debug.
    /// Tests each ReportDataLoader query individually with timing.
    /// </summary>
    [Function("Version_Diagnose")]
    public async Task<HttpResponseData> Diagnose(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/diagnose/{orgId:guid}")] HttpRequestData req,
        FunctionContext context,
        Guid orgId)
    {
        var sw = Stopwatch.StartNew();
        var steps = new List<object>();

        try
        {
            var db = context.InstanceServices.GetRequiredService<KryossDbContext>();

            // RLS bypass
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "EXEC sp_set_session_context @key = N'is_admin', @value = 1, @read_only = 1;";
                await cmd.ExecuteNonQueryAsync();
            }
            steps.Add(new { step = "rls_bypass", ms = sw.ElapsedMilliseconds });

            // 1. Org
            sw.Restart();
            var orgName = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync();
            steps.Add(new { step = "1_org", ms = sw.ElapsedMilliseconds, orgName });

            // 2. Latest run IDs
            sw.Restart();
            var latestRunIds = await db.AssessmentRuns.AsNoTracking()
                .Where(r => r.OrganizationId == orgId && r.CompletedAt != null)
                .GroupBy(r => r.MachineId)
                .Select(g => g.OrderByDescending(r => r.CompletedAt).First().Id)
                .ToListAsync();
            steps.Add(new { step = "2_latest_runs", ms = sw.ElapsedMilliseconds, count = latestRunIds.Count });

            // 3. Runs with machines
            sw.Restart();
            var runCount = latestRunIds.Count > 0
                ? await db.AssessmentRuns.AsNoTracking().Include(r => r.Machine).CountAsync(r => latestRunIds.Contains(r.Id))
                : 0;
            steps.Add(new { step = "3_runs_machines", ms = sw.ElapsedMilliseconds, runCount });

            // 4. Control results
            sw.Restart();
            var crCount = latestRunIds.Count > 0
                ? await db.ControlResults.AsNoTracking().CountAsync(cr => latestRunIds.Contains(cr.RunId))
                : 0;
            steps.Add(new { step = "4_control_results", ms = sw.ElapsedMilliseconds, crCount });

            // 5. Framework scores
            sw.Restart();
            try
            {
                var fwCount = latestRunIds.Count > 0
                    ? await db.RunFrameworkScores.AsNoTracking().CountAsync(fs => latestRunIds.Contains(fs.RunId))
                    : 0;
                steps.Add(new { step = "5_fw_scores", ms = sw.ElapsedMilliseconds, fwCount });
            }
            catch (Exception ex) { steps.Add(new { step = "5_fw_scores", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 6. MachineDisks
            sw.Restart();
            try
            {
                var diskCount = await db.MachineDisks.AsNoTracking().CountAsync();
                steps.Add(new { step = "6_disks", ms = sw.ElapsedMilliseconds, diskCount });
            }
            catch (Exception ex) { steps.Add(new { step = "6_disks", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 7. MachinePorts
            sw.Restart();
            try
            {
                var portCount = await db.MachinePorts.AsNoTracking().CountAsync();
                steps.Add(new { step = "7_ports", ms = sw.ElapsedMilliseconds, portCount });
            }
            catch (Exception ex) { steps.Add(new { step = "7_ports", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 8. MachineThreats
            sw.Restart();
            try
            {
                var threatCount = await db.MachineThreats.AsNoTracking().CountAsync();
                steps.Add(new { step = "8_threats", ms = sw.ElapsedMilliseconds, threatCount });
            }
            catch (Exception ex) { steps.Add(new { step = "8_threats", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 9. AdHygieneScans
            sw.Restart();
            try
            {
                var hygCount = await db.AdHygieneScans.AsNoTracking().CountAsync();
                steps.Add(new { step = "9_hygiene_scans", ms = sw.ElapsedMilliseconds, hygCount });
            }
            catch (Exception ex) { steps.Add(new { step = "9_hygiene_scans", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 10. M365Tenants
            sw.Restart();
            try
            {
                var m365Count = await db.M365Tenants.AsNoTracking().CountAsync();
                steps.Add(new { step = "10_m365", ms = sw.ElapsedMilliseconds, m365Count });
            }
            catch (Exception ex) { steps.Add(new { step = "10_m365", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 11. CloudAssessmentScans
            sw.Restart();
            try
            {
                var caCount = await db.CloudAssessmentScans.AsNoTracking().CountAsync();
                steps.Add(new { step = "11_cloud_scans", ms = sw.ElapsedMilliseconds, caCount });
            }
            catch (Exception ex) { steps.Add(new { step = "11_cloud_scans", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 12. NetworkDiags
            sw.Restart();
            try
            {
                var netCount = await db.MachineNetworkDiags.AsNoTracking().CountAsync();
                steps.Add(new { step = "12_network_diags", ms = sw.ElapsedMilliseconds, netCount });
            }
            catch (Exception ex) { steps.Add(new { step = "12_network_diags", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 13. ServiceCatalog
            sw.Restart();
            try
            {
                var scCount = await db.ServiceCatalog.AsNoTracking().CountAsync();
                steps.Add(new { step = "13_service_catalog", ms = sw.ElapsedMilliseconds, scCount });
            }
            catch (Exception ex) { steps.Add(new { step = "13_service_catalog", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            // 14. ExecutiveCtas
            sw.Restart();
            try
            {
                var ctaCount = await db.ExecutiveCtas.AsNoTracking().CountAsync();
                steps.Add(new { step = "14_ctas", ms = sw.ElapsedMilliseconds, ctaCount });
            }
            catch (Exception ex) { steps.Add(new { step = "14_ctas", ms = sw.ElapsedMilliseconds, error = ex.Message }); }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { steps });
            return response;
        }
        catch (Exception ex)
        {
            steps.Add(new { step = "crash", error = ex.Message, type = ex.GetType().Name, inner = ex.InnerException?.Message });
            var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await resp.WriteAsJsonAsync(new { steps });
            return resp;
        }
    }
}
