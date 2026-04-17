namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Pre-computed Microsoft 365 productivity and licensing metrics for the
/// Cloud Assessment (CA-4) productivity pipeline.
///
/// Covers user counts, Copilot adoption, email / Teams / SharePoint /
/// OneDrive / Office activity, wasted license detection, and Graph Connectors.
///
/// Built once per assessment run by <c>ProductivityPipeline</c> and consumed
/// by <c>ProductivityRecommendations</c> (Task 3) to emit findings.
/// </summary>
public sealed class ProductivityInsights
{
    // ============================================================
    // Users
    // ============================================================
    public int TotalUsers { get; set; }
    public int EnabledUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int GuestUserCount { get; set; }

    // ============================================================
    // Copilot
    // ============================================================
    public int CopilotLicensesPurchased { get; set; }
    public int CopilotLicensesAssigned { get; set; }
    public double CopilotAdoptionPct { get; set; }

    // ============================================================
    // Email
    // ============================================================
    public int EmailLicensedCount { get; set; }
    public int EmailActive30d { get; set; }
    public double EmailAdoptionRate { get; set; }
    public double EmailSendAvg { get; set; }
    public double EmailReceiveAvg { get; set; }
    public bool EmailReportAvailable { get; set; }

    // ============================================================
    // Teams
    // ============================================================
    public int TeamsLicensedCount { get; set; }
    public int TeamsActive30d { get; set; }
    public double TeamsAdoptionRate { get; set; }
    public double TeamsChatAvg { get; set; }
    public double TeamsMeetingAvg { get; set; }
    public double TeamsCallAvg { get; set; }
    public bool TeamsReportAvailable { get; set; }

    // ============================================================
    // SharePoint
    // ============================================================
    public int SharePointLicensedCount { get; set; }
    public int SharePointActive30d { get; set; }
    public double SharePointAdoptionRate { get; set; }
    public int SharePointSiteCount { get; set; }
    public int SharePointAvgFiles { get; set; }
    public bool SharePointReportAvailable { get; set; }

    // ============================================================
    // OneDrive
    // ============================================================
    public int OneDriveLicensedCount { get; set; }
    public int OneDriveActive30d { get; set; }
    public double OneDriveAdoptionRate { get; set; }
    public double OneDriveTotalGB { get; set; }
    public bool OneDriveReportAvailable { get; set; }

    // ============================================================
    // Office
    // ============================================================
    public int OfficeWindowsActivations { get; set; }
    public int OfficeMacActivations { get; set; }
    public int OfficeMobileActivations { get; set; }
    public double OfficeDesktopAdoptionRate { get; set; }
    public bool OfficeActivationsReportAvailable { get; set; }

    // ============================================================
    // Wasted licenses
    // ============================================================
    public int WastedLicenseCount { get; set; }
    public int WastedLicenseTotalSeats { get; set; }
    public decimal? EstimatedAnnualWaste { get; set; } // null — no cost table yet

    // ============================================================
    // Graph Connectors
    // ============================================================
    public int GraphConnectorsCount { get; set; }
    public bool GraphConnectorsAvailable { get; set; }

    // ============================================================
    // Raw persistence targets — caller reads these after RunAsync
    // ============================================================
    public List<ProductivityLicenseRow> Licenses { get; } = new();
    public List<ProductivityAdoptionRow> Adoptions { get; } = new();
    public List<ProductivityWastedLicenseRow> WastedLicenses { get; } = new();

    // ============================================================
    // Service plan status per SKU — fed into ProductivityRecommendations (T3)
    // ============================================================
    public Dictionary<string, string> SkuPlans { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // Internal: SkuId (Guid) -> SkuPartNumber; populated by CollectSubscribedSkus
    // so CollectUsers can resolve friendly names in the post-processing step.
    public Dictionary<Guid, string> SkuIdToPartNumber { get; } = new();

    public bool Available =>
        EmailReportAvailable || TeamsReportAvailable ||
        SharePointReportAvailable || OneDriveReportAvailable ||
        OfficeActivationsReportAvailable || SkuPlans.Count > 0;
}

public sealed class ProductivityLicenseRow
{
    public string SkuPartNumber { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public int Purchased { get; init; }
    public int Assigned { get; init; }
    public int Available { get; init; }
}

public sealed class ProductivityAdoptionRow
{
    public string ServiceName { get; init; } = string.Empty; // email|teams|sharepoint|onedrive|office|copilot
    public int LicensedCount { get; init; }
    public int Active30d { get; init; }
    public decimal AdoptionRate { get; init; } // 0-100 rounded to 2 decimals
}

public sealed class ProductivityWastedLicenseRow
{
    public string UserPrincipal { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Sku { get; set; } // settable for post-processing SKU resolution
    public DateTime? LastSignIn { get; init; }
    public int? DaysInactive { get; init; }
    public decimal? EstimatedCostYear { get; init; } // always null for now
}
