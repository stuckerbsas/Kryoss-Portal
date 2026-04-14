using System.Management;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads BitLocker drive encryption state via WMI
/// <c>Win32_EncryptableVolume</c> class (native, no Process.Start).
///
/// v1.4.0: Replaced manage-bde.exe shell-out with direct WMI query.
///
/// Supported <c>CheckType</c> values:
///   protection_status     -> "On" / "Off" / "Unknown"
///   encryption_method     -> e.g. "XTS-AES 256" or "None"
///   conversion_status     -> "FullyEncrypted" / "FullyDecrypted" / etc.
///   lock_status           -> "Unlocked" / "Locked" / "Unknown"
///   protector_types       -> comma-separated list ("TPM,RecoveryPassword")
///   encryption_percent    -> int 0-100
///   recovery_key_present  -> bool
///
/// Required ControlDef fields: <c>Drive</c> (e.g. "C:") or "*" for any
/// encrypted drive (returns first match where ProtectionStatus == On).
/// </summary>
public class BitLockerEngine : ICheckEngine
{
    public string Type => "bitlocker";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        Dictionary<string, DriveInfoBlock> drives;
        string? executionError;
        try
        {
            drives = QueryWmiBitLocker(out executionError);
        }
        catch (Exception ex)
        {
            drives = new Dictionary<string, DriveInfoBlock>(StringComparer.OrdinalIgnoreCase);
            executionError = ex.Message;
        }

        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control, drives, executionError));
        }
        return results;
    }

    private static CheckResult ExecuteOne(
        ControlDef control,
        Dictionary<string, DriveInfoBlock> drives,
        string? executionError)
    {
        var result = new CheckResult { Id = control.Id };

        if (executionError is not null)
        {
            result.Exists = null;
            result.Value = $"ERROR: BitLocker WMI query failed: {executionError}";
            return result;
        }

        if (string.IsNullOrEmpty(control.Drive))
        {
            result.Exists = false;
            result.Value = "ERROR: drive is required";
            return result;
        }

        DriveInfoBlock? info;
        if (control.Drive == "*")
        {
            info = drives.Values.FirstOrDefault(d =>
                string.Equals(d.ProtectionStatus, "On", StringComparison.OrdinalIgnoreCase));
            if (info is null)
            {
                result.Exists = false;
                result.Value = null;
                return result;
            }
        }
        else
        {
            var key = NormalizeDrive(control.Drive);
            if (!drives.TryGetValue(key, out info))
            {
                result.Exists = false;
                result.Value = null;
                return result;
            }
        }

        var checkType = control.CheckType ?? "protection_status";
        switch (checkType)
        {
            case "protection_status":
                result.Exists = true;
                result.Value = info.ProtectionStatus ?? "Unknown";
                break;
            case "encryption_method":
                result.Exists = true;
                result.Value = info.EncryptionMethod ?? "None";
                break;
            case "conversion_status":
                result.Exists = true;
                result.Value = info.ConversionStatus ?? "Unknown";
                break;
            case "lock_status":
                result.Exists = true;
                result.Value = info.LockStatus ?? "Unknown";
                break;
            case "protector_types":
                result.Exists = true;
                result.Value = string.Join(",", info.ProtectorTypes);
                break;
            case "encryption_percent":
                result.Exists = true;
                result.Value = info.EncryptionPercent;
                break;
            case "recovery_key_present":
                result.Exists = true;
                result.Value = info.ProtectorTypes.Any(p =>
                    p.Contains("Recovery", StringComparison.OrdinalIgnoreCase));
                break;
            default:
                result.Exists = false;
                result.Value = $"ERROR: unknown checkType '{checkType}'";
                break;
        }

        return result;
    }

    private static string NormalizeDrive(string drive)
    {
        var d = drive.Trim().TrimEnd('\\');
        if (d.Length == 1) d += ":";
        return d.ToUpperInvariant();
    }

    /// <summary>
    /// Query WMI Win32_EncryptableVolume for all BitLocker-capable volumes.
    /// </summary>
    private static Dictionary<string, DriveInfoBlock> QueryWmiBitLocker(out string? error)
    {
        error = null;
        var result = new Dictionary<string, DriveInfoBlock>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT * FROM Win32_EncryptableVolume");

            foreach (ManagementObject vol in searcher.Get())
            {
                try
                {
                    var driveLetter = vol["DriveLetter"]?.ToString();
                    if (string.IsNullOrEmpty(driveLetter)) continue;

                    var info = new DriveInfoBlock();

                    // ProtectionStatus: 0=Off, 1=On, 2=Unknown
                    var protectionStatus = GetUInt32(vol, "ProtectionStatus");
                    info.ProtectionStatus = protectionStatus switch
                    {
                        0 => "Off",
                        1 => "On",
                        _ => "Unknown"
                    };

                    // ConversionStatus: 0=FullyDecrypted, 1=FullyEncrypted,
                    // 2=EncryptionInProgress, 3=DecryptionInProgress,
                    // 4=EncryptionPaused, 5=DecryptionPaused
                    var conversionStatus = GetUInt32(vol, "ConversionStatus");
                    info.ConversionStatus = conversionStatus switch
                    {
                        0 => "FullyDecrypted",
                        1 => "FullyEncrypted",
                        2 => "EncryptionInProgress",
                        3 => "DecryptionInProgress",
                        4 => "EncryptionPaused",
                        5 => "DecryptionPaused",
                        _ => "Unknown"
                    };

                    // EncryptionMethod: 0=None, 1=AES128Diffuser, 2=AES256Diffuser,
                    // 3=AES128, 4=AES256, 5=HardwareEncryption, 6=XTS_AES128, 7=XTS_AES256
                    var encryptionMethod = GetUInt32(vol, "EncryptionMethod");
                    info.EncryptionMethod = encryptionMethod switch
                    {
                        0 => "None",
                        1 => "AES 128 With Diffuser",
                        2 => "AES 256 With Diffuser",
                        3 => "AES 128",
                        4 => "AES 256",
                        5 => "Hardware Encryption",
                        6 => "XTS-AES 128",
                        7 => "XTS-AES 256",
                        _ => "Unknown"
                    };

                    // EncryptionPercentage via GetConversionStatus method
                    try
                    {
                        var outParams = vol.InvokeMethod("GetConversionStatus", null, null);
                        if (outParams != null)
                        {
                            var pct = outParams["EncryptionPercentage"];
                            if (pct != null && uint.TryParse(pct.ToString(), out var pctVal))
                                info.EncryptionPercent = (int)pctVal;
                        }
                    }
                    catch { /* method may not be available */ }

                    // LockStatus via GetLockStatus method
                    try
                    {
                        var outParams = vol.InvokeMethod("GetLockStatus", null, null);
                        if (outParams != null)
                        {
                            var lockStatus = outParams["LockStatus"];
                            if (lockStatus != null)
                            {
                                info.LockStatus = Convert.ToUInt32(lockStatus) switch
                                {
                                    0 => "Unlocked",
                                    1 => "Locked",
                                    _ => "Unknown"
                                };
                            }
                        }
                    }
                    catch { info.LockStatus = "Unknown"; }

                    // KeyProtectors via GetKeyProtectors method
                    try
                    {
                        // Type 0 = all protectors
                        var inParams = vol.GetMethodParameters("GetKeyProtectors");
                        inParams["KeyProtectorType"] = (uint)0;
                        var outParams = vol.InvokeMethod("GetKeyProtectors", inParams, null);
                        if (outParams?["VolumeKeyProtectorID"] is string[] protectorIds)
                        {
                            foreach (var protectorId in protectorIds)
                            {
                                try
                                {
                                    var typeParams = vol.GetMethodParameters("GetKeyProtectorType");
                                    typeParams["VolumeKeyProtectorID"] = protectorId;
                                    var typeResult = vol.InvokeMethod("GetKeyProtectorType", typeParams, null);
                                    if (typeResult != null)
                                    {
                                        var protectorType = Convert.ToUInt32(typeResult["KeyProtectorType"]);
                                        var name = protectorType switch
                                        {
                                            0 => "Unknown",
                                            1 => "TPM",
                                            2 => "ExternalKey",
                                            3 => "NumericalPassword",
                                            4 => "TPMAndPIN",
                                            5 => "TPMAndStartupKey",
                                            6 => "TPMAndPINAndStartupKey",
                                            7 => "PublicKey",
                                            8 => "Passphrase",
                                            9 => "TPMCertificate",
                                            10 => "CryptoNextGeneration",
                                            _ => $"Type{protectorType}"
                                        };
                                        info.ProtectorTypes.Add(name);
                                    }
                                }
                                catch { /* skip individual protector */ }
                            }
                        }
                    }
                    catch { /* protector enumeration optional */ }

                    var key = NormalizeDrive(driveLetter);
                    result[key] = info;
                }
                catch { /* skip individual volume on error */ }
            }
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace)
        {
            error = "BitLocker WMI namespace not available (Win32_EncryptableVolume)";
        }
        catch (Exception ex)
        {
            error = $"WMI query failed: {ex.Message}";
        }

        return result;
    }

    private static uint GetUInt32(ManagementObject obj, string propertyName)
    {
        var val = obj[propertyName];
        if (val == null) return uint.MaxValue;
        return Convert.ToUInt32(val);
    }

    private sealed class DriveInfoBlock
    {
        public string? ProtectionStatus { get; set; }
        public string? EncryptionMethod { get; set; }
        public string? ConversionStatus { get; set; }
        public string? LockStatus { get; set; }
        public int EncryptionPercent { get; set; }
        public List<string> ProtectorTypes { get; } = new();
    }
}
