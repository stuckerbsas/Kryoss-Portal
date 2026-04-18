namespace KryossApi.Services;

/// <summary>
/// Configuration for the shared (multi-tenant admin consent) M365 app registration.
/// Read from environment variables at startup.
/// </summary>
public class M365Config
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string PortalBaseUrl { get; set; } = "https://zealous-dune-0ac672d10.6.azurestaticapps.net";
    public string CallbackBaseUrl { get; set; } = "https://func-kryoss.azurewebsites.net";
}
