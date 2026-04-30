using KryossApi.Data;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface ICpeMappingService
{
    Task<(string? vendor, string? product)> ResolveCpeAsync(string softwareName, string? publisher);
    Task ApplyMappingsToNewSoftwareAsync();
}

public class CpeMappingService : ICpeMappingService
{
    private readonly IDbContextFactory<KryossDbContext> _dbFactory;

    private static readonly Dictionary<string, (string vendor, string product)> KnownMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google Chrome"] = ("google", "chrome"),
        ["Mozilla Firefox"] = ("mozilla", "firefox"),
        ["Microsoft Edge"] = ("microsoft", "edge_chromium"),
        ["Microsoft Office"] = ("microsoft", "office"),
        ["Adobe Acrobat"] = ("adobe", "acrobat_reader_dc"),
        ["Adobe Reader"] = ("adobe", "acrobat_reader_dc"),
        ["7-Zip"] = ("igor_pavlov", "7-zip"),
        ["WinRAR"] = ("rarlab", "winrar"),
        ["Zoom"] = ("zoom", "zoom"),
        ["TeamViewer"] = ("teamviewer", "teamviewer"),
        ["AnyDesk"] = ("anydesk", "anydesk"),
        ["PuTTY"] = ("putty", "putty"),
        ["VLC"] = ("videolan", "vlc_media_player"),
        ["Notepad++"] = ("notepad-plus-plus", "notepad++"),
        ["FileZilla"] = ("filezilla-project", "filezilla_client"),
        ["KeePass"] = ("keepass", "keepass"),
        ["Veeam"] = ("veeam", "backup_and_replication"),
        ["Node.js"] = ("nodejs", "node.js"),
        ["Python"] = ("python", "python"),
        ["Docker Desktop"] = ("docker", "docker_desktop"),
        ["VMware Workstation"] = ("vmware", "workstation"),
        ["Git for Windows"] = ("git-scm", "git"),
        ["Cisco AnyConnect"] = ("cisco", "anyconnect_secure_mobility_client"),
        ["Cisco Secure Client"] = ("cisco", "anyconnect_secure_mobility_client"),
        ["FortiClient"] = ("fortinet", "forticlient"),
        ["ScreenConnect"] = ("connectwise", "screenconnect"),
        ["ConnectWise Control"] = ("connectwise", "screenconnect"),
        ["SolarWinds"] = ("solarwinds", "orion_platform"),
        ["Acronis"] = ("acronis", "cyber_protect"),
        ["Slack"] = ("slack", "slack"),
        ["Dropbox"] = ("dropbox", "dropbox"),
        ["iTunes"] = ("apple", "itunes"),
    };

    public CpeMappingService(IDbContextFactory<KryossDbContext> dbFactory) => _dbFactory = dbFactory;

    public static (string? vendor, string? product) ResolveKnownCpe(string softwareName)
    {
        foreach (var (pattern, cpe) in KnownMappings)
        {
            if (softwareName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return (cpe.vendor, cpe.product);
        }
        return (null, null);
    }

    public Task<(string? vendor, string? product)> ResolveCpeAsync(string softwareName, string? publisher)
    {
        var result = ResolveKnownCpe(softwareName);
        return Task.FromResult<(string?, string?)>(result);
    }

    public async Task ApplyMappingsToNewSoftwareAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var unmapped = await db.Software
            .Where(s => s.CpeVendor == null)
            .ToListAsync();

        foreach (var sw in unmapped)
        {
            var (vendor, product) = await ResolveCpeAsync(sw.Name, sw.Publisher);
            if (vendor != null)
            {
                sw.CpeVendor = vendor;
                sw.CpeProduct = product;
            }
        }

        await db.SaveChangesAsync();
    }
}
