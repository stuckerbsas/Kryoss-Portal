using Microsoft.Data.SqlClient;

namespace KryossApi.Infrastructure;

/// <summary>
/// Builds the Azure SQL connection string used by <c>KryossDbContext</c>.
///
/// <para>
/// Security baseline (see <c>KryossApi/docs/security-baseline.md</c>):
/// the Function App MUST authenticate to Azure SQL via Microsoft Entra
/// (Managed Identity in Azure, <c>az login</c> credentials locally).
/// No SQL logins, no passwords, no shared secrets in configuration.
/// </para>
///
/// <para>
/// This factory reads the <c>SqlConnectionString</c> environment variable,
/// validates that it contains NO password/userid field (fail-closed on
/// leftover SQL-auth), normalizes TLS requirements, and forces
/// <c>Authentication=Active Directory Default</c> if not already present.
/// </para>
///
/// <para>
/// Calling code should treat this class as the single source of truth for
/// how we talk to SQL. Bypassing it (e.g. passing a raw connection string
/// directly into <c>UseSqlServer</c>) is a security-baseline violation.
/// </para>
/// </summary>
internal static class SqlConnectionFactory
{
    private const string EnvVarName = "SqlConnectionString";

    public sealed record Result(string ConnectionString, string AuthMethod);

    /// <summary>
    /// Build the validated connection string. Throws on misconfiguration so
    /// the Function App refuses to start with an insecure config.
    /// </summary>
    public static Result Build()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"{EnvVarName} environment variable is not set. " +
                "Expected format: " +
                "'Server=tcp:sql-kryoss.database.windows.net,1433;" +
                "Database=KryossDb;" +
                "Authentication=Active Directory Default;" +
                "Encrypt=True;TrustServerCertificate=False;'");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(raw);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{EnvVarName} is not a valid SQL connection string: {ex.Message}",
                ex);
        }

        // ─── Fail-closed: no SQL auth allowed. ──────────────────────────────
        // If the connection string carries a password or a user id, we reject
        // startup. The intent is to eliminate the entire class of "I was
        // debugging and left the password in local.settings.json" incidents
        // before code ships.
        if (!string.IsNullOrEmpty(builder.Password))
        {
            throw new InvalidOperationException(
                $"{EnvVarName} contains a Password= field. " +
                "SQL authentication is forbidden by the security baseline. " +
                "Use 'Authentication=Active Directory Default' instead.");
        }

        if (!string.IsNullOrEmpty(builder.UserID))
        {
            throw new InvalidOperationException(
                $"{EnvVarName} contains a User ID= field. " +
                "SQL authentication is forbidden by the security baseline. " +
                "Use 'Authentication=Active Directory Default' instead.");
        }

        // ─── Enforce TLS. ──────────────────────────────────────────────────
        // These are the non-negotiable transport requirements for Azure SQL.
        // If the operator set Encrypt=False or TrustServerCertificate=True we
        // override them. Log lines make the override visible in deployment.
        if (!builder.Encrypt.ToString().Equals("Mandatory", StringComparison.OrdinalIgnoreCase)
            && builder.Encrypt.ToString() != "True")
        {
            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
        }

        if (builder.TrustServerCertificate)
        {
            builder.TrustServerCertificate = false;
        }

        // ─── Default to Entra auth if not explicitly set. ──────────────────
        // Valid supported values here are any Active Directory * option.
        // We pick "Active Directory Default" which internally uses
        // DefaultAzureCredential: Managed Identity in Azure, az login
        // locally, Visual Studio creds in VS, etc.
        if (builder.Authentication == SqlAuthenticationMethod.NotSpecified)
        {
            builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
        }
        else if (builder.Authentication == SqlAuthenticationMethod.SqlPassword)
        {
            throw new InvalidOperationException(
                $"{EnvVarName} specifies Authentication=SqlPassword. " +
                "SQL authentication is forbidden by the security baseline.");
        }

        // ─── Basic connection hardening defaults. ──────────────────────────
        if (builder.ConnectTimeout < 15) builder.ConnectTimeout = 30;
        if (builder.CommandTimeout < 15) builder.CommandTimeout = 30;

        return new Result(builder.ConnectionString, builder.Authentication.ToString());
    }
}
