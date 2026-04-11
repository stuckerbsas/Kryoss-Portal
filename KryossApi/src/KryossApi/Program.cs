using KryossApi.Data;
using KryossApi.Infrastructure;
using KryossApi.Middleware;
using KryossApi.Services;
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
builder.Services.AddScoped<IHwidVerifier, HwidVerifier>();
builder.Services.AddScoped<IPlatformResolver, PlatformResolver>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IEvaluationService, EvaluationService>();
builder.Services.AddScoped<IActlogService, ActlogService>();
builder.Services.AddScoped<ICryptoService, CryptoService>();
builder.Services.AddScoped<IReportService, ReportService>();

// ── Application Insights ──
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
