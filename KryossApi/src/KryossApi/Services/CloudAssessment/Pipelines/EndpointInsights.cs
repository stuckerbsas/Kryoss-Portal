namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Pre-computed endpoint management and Defender-for-Endpoint metrics for
/// the Cloud Assessment (CA-2) endpoint pipeline.
///
/// Covers Intune managed-device posture (compliance, config profiles, app
/// protection, enrollment restrictions, Autopilot) and Defender-for-Endpoint
/// signals (machine risk distribution, vulnerabilities, exposure score,
/// recommendations, software inventory) plus three advanced-hunting KQL
/// queries focused on endpoint security hygiene (USB usage, unsigned
/// binaries, lateral-movement attempts).
///
/// Built once per assessment run and consumed by
/// <c>EndpointRecommendations</c> (added in Task 2) to emit findings.
/// </summary>
public class EndpointInsights
{
    // ============================================================
    // Intune — device management & configuration
    // ============================================================
    public int DeviceCompliancePolicyCount { get; set; }
    public int DeviceConfigProfileCount { get; set; }

    public int DevicesTotal { get; set; }
    public int DevicesCompliant { get; set; }
    public int DevicesNonCompliant { get; set; }
    public int DevicesInGracePeriod { get; set; }

    public int DevicesBYOD { get; set; }
    public int DevicesCorporate { get; set; }

    public int DevicesWindows { get; set; }
    public int DevicesIOS { get; set; }
    public int DevicesAndroid { get; set; }
    public int DevicesMacOS { get; set; }

    public int DevicesEncrypted { get; set; }

    public int AppProtectionPoliciesIOS { get; set; }
    public int AppProtectionPoliciesAndroid { get; set; }
    public int ManagedAppCount { get; set; }

    public int EnrollmentRestrictionCount { get; set; }
    public int AutopilotProfileCount { get; set; }

    // Config profile drift
    public int ConfigProfilesAssigned { get; set; }
    public int ConfigProfilesSucceeded { get; set; }
    public int ConfigProfilesFailed { get; set; }
    public int ConfigProfilesPending { get; set; }
    public int ConfigProfilesConflict { get; set; }

    // ============================================================
    // Defender for Endpoint
    // ============================================================
    public int MachinesTotal { get; set; }
    public int MachinesHighRisk { get; set; }
    public int MachinesMediumRisk { get; set; }
    public int MachinesLowRisk { get; set; }

    public int VulnCritical { get; set; }
    public int VulnHigh { get; set; }
    public int VulnMedium { get; set; }
    public int VulnLow { get; set; }

    public double ExposureScore { get; set; }
    public string ExposureRisk { get; set; } = "Unknown";

    public int UnpatchedSoftwareCount { get; set; }

    public int RecommendationsTotal { get; set; }
    public int RecommendationsCritical { get; set; }

    public int SoftwareTotal { get; set; }
    public int SoftwareVulnerable { get; set; }

    // ============================================================
    // Advanced Hunting — endpoint posture KQL queries
    // ============================================================
    public int UsbUsageEvents { get; set; }
    public int UnsignedBinariesLast30d { get; set; }
    public int LateralMovementAttempts30d { get; set; }

    // ============================================================
    // Availability flags
    // ============================================================
    public bool IntuneAvailable { get; set; }
    public bool DefenderEndpointAvailable { get; set; }
    public bool ActivationNeeded { get; set; }

    public bool Available => IntuneAvailable || DefenderEndpointAvailable;
}
