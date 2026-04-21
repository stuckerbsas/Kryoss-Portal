using KryossApi.Data;
using KryossApi.Infrastructure;
using KryossApi.Middleware;
using KryossApi.Services;
using KryossApi.Services.CloudAssessment;
using KryossApi.Services.Reports;
using KryossApi.Services.CloudAssessment.Helpers;
using KryossApi.Services.CopilotReadiness;
using KryossApi.Services.InfraAssessment;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// ── Middleware pipeline (order matters) ──
// ErrorSanitization is FIRST so it can wrap every other middleware. Any
// exception that escapes below is converted to a generic 500 with a trace
// id — no stack traces or framework types on the wire.
builder.UseMiddleware<ErrorSanitizationMiddleware>();
builder.UseMiddleware<ApiKeyAuthMiddleware>();   // Agent auth: API Key + HMAC
builder.UseMiddleware<BearerAuthMiddleware>();   // Portal auth: Entra ID / B2C
builder.UseMiddleware<RbacMiddleware>();         // Permission check
builder.UseMiddleware<RlsMiddleware>();          // SQL SESSION_CONTEXT for RLS
builder.UseMiddleware<ActlogMiddleware>();       // Request logging (outermost = last)

// ── EF Core + Azure SQL (Managed Identity only — no passwords) ──
// The connection string is validated at startup by SqlConnectionFactory.
// Any attempt to ship a SQL-auth password in config will fail fast before
// the Function host finishes booting. See KryossApi/docs/security-baseline.md.
var sqlConfig = SqlConnectionFactory.Build();
Console.WriteLine(
    $"[KryossApi] SQL auth method: {sqlConfig.AuthMethod} " +
    "(no secrets in connection string)");

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<KryossDbContext>((sp, options) =>
{
    options.UseSqlServer(sqlConfig.ConnectionString, sql =>
    {
        sql.CommandTimeout(30);
        sql.EnableRetryOnFailure(3);
    })
    .UseSnakeCaseNamingConvention();
    options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
});

// ── Services ──
// NonceCache is a SINGLETON — the whole point is in-process state that
// outlives individual requests. See NonceCache.cs for multi-instance notes.
builder.Services.AddSingleton<INonceCache, NonceCache>();
builder.Services.AddSingleton<IDnsLookup, DnsLookup>();
builder.Services.AddScoped<IHwidVerifier, HwidVerifier>();
builder.Services.AddScoped<IPlatformResolver, PlatformResolver>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IEvaluationService, EvaluationService>();
builder.Services.AddScoped<IActlogService, ActlogService>();
builder.Services.AddScoped<ICryptoService, CryptoService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportDataLoader, ReportDataLoader>();
builder.Services.AddScoped<IReportComposer, ReportComposer>();
builder.Services.AddScoped<ExternalScanService>();
builder.Services.AddScoped<IM365ScannerService, M365ScannerService>();
builder.Services.AddScoped<ICopilotReadinessService, CopilotReadinessService>();
builder.Services.AddScoped<ICloudAssessmentService, CloudAssessmentService>();
builder.Services.AddScoped<IBenchmarkService, BenchmarkService>();
builder.Services.AddScoped<IFindingStatusService, FindingStatusService>();
builder.Services.AddScoped<IConsentOrchestrator, ConsentOrchestrator>();
builder.Services.AddScoped<ICloudAssessmentReportService, CloudAssessmentReportService>();
builder.Services.AddScoped<IInfraAssessmentService, InfraAssessmentService>();
builder.Services.AddScoped<KryossApi.Services.InfraAssessment.Pipelines.IHypervisorPipeline, KryossApi.Services.InfraAssessment.Pipelines.HypervisorPipeline>();
builder.Services.AddSingleton<IGeoIpService, IpApiGeoIpService>();
builder.Services.AddSingleton<IFabricAdminService, FabricAdminService>();
builder.Services.AddScoped<IPublicIpTracker, PublicIpTracker>();
builder.Services.AddScoped<ISiteClusterService, SiteClusterService>();
builder.Services.AddScoped<IScanScheduleService, ScanScheduleService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddHttpClient();

// ── M365 multi-tenant admin consent config ──
builder.Services.AddSingleton(new M365Config
{
    ClientId = Environment.GetEnvironmentVariable("M365ScannerClientId") ?? "",
    ClientSecret = Environment.GetEnvironmentVariable("M365ScannerClientSecret") ?? "",
    PortalBaseUrl = Environment.GetEnvironmentVariable("PortalBaseUrl")
        ?? "https://zealous-dune-0ac672d10.6.azurestaticapps.net"
});

// ── Application Insights ──
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
