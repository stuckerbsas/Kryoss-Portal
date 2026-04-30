using System.Security.Cryptography;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services;

public interface IEnrollmentService
{
    Task<string> GenerateCodeAsync(Guid organizationId, int? assessmentId, string? label, int expiryDays = 7, int? maxUses = null, bool isTrial = false, int? trialDays = null);
    Task<EnrollmentResult?> RedeemCodeAsync(
        string code, string hostname, string? os, string? osVersion, string? osBuild,
        string? hwid = null, int? productType = null);
}

public record EnrollmentResult(
    Guid AgentId,
    string ApiKey,
    string ApiSecret,
    string PublicKeyPem,
    int? AssessmentId,
    string? AssessmentName,
    bool ProtocolAuditEnabled,
    bool IsTrial = false,
    DateTime? TrialExpiresAt = null,
    Guid OrganizationId = default,
    string? MachineSecret = null,
    string? SessionKey = null,
    DateTime? SessionKeyExpiresAt = null
);

public class EnrollmentService : IEnrollmentService
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IPlatformResolver _platformResolver;
    private readonly IScanScheduleService _schedule;
    private readonly IKeyRotationService _keyRotation;

    public EnrollmentService(KryossDbContext db, ICurrentUserService user, IPlatformResolver platformResolver, IScanScheduleService schedule, IKeyRotationService keyRotation)
    {
        _db = db;
        _user = user;
        _platformResolver = platformResolver;
        _schedule = schedule;
        _keyRotation = keyRotation;
    }

    public async Task<string> GenerateCodeAsync(Guid organizationId, int? assessmentId, string? label, int expiryDays = 7, int? maxUses = null, bool isTrial = false, int? trialDays = null)
    {
        var code = GenerateRandomCode(19); // XXXX-XXXX-XXXX-XXXX format

        var enrollment = new EnrollmentCode
        {
            OrganizationId = organizationId,
            Code = code,
            AssessmentId = assessmentId,
            Label = label,
            MaxUses = maxUses,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            IsTrial = isTrial,
            TrialDays = trialDays,
        };

        _db.EnrollmentCodes.Add(enrollment);
        await _db.SaveChangesAsync();
        return code;
    }

    public async Task<EnrollmentResult?> RedeemCodeAsync(
        string code, string hostname, string? os, string? osVersion, string? osBuild,
        string? hwid = null, int? productType = null)
    {
        var enrollment = await _db.EnrollmentCodes
            .Include(x => x.Organization)
            .Include(x => x.Assessment)
            .FirstOrDefaultAsync(x => x.Code == code
                && x.ExpiresAt > DateTime.UtcNow
                && (
                    // Single-use: not yet consumed
                    (x.MaxUses == null && x.UsedBy == null)
                    // Multi-use: under the limit
                    || (x.MaxUses != null && x.UseCount < x.MaxUses)
                ));

        if (enrollment is null)
            return null;

        var platformId = await _platformResolver.ResolveIdAsync(os, osVersion, osBuild, productType);

        // Find existing machine by hostname in same org (re-enrollment case)
        var machine = await _db.Machines
            .FirstOrDefaultAsync(m => m.OrganizationId == enrollment.OrganizationId
                && m.Hostname == hostname);

        Guid agentId;
        if (machine is not null)
        {
            // Re-enrollment: keep existing AgentId (avoids desync if SaveChanges fails)
            agentId = machine.AgentId;
            machine.OsName = os;
            machine.OsVersion = osVersion;
            machine.OsBuild = osBuild;
            machine.PlatformId = platformId;
            machine.Hwid = string.IsNullOrWhiteSpace(hwid) ? null : hwid;
            if (productType is > 0)
                machine.ProductType = (short)productType;
            machine.LastSeenAt = DateTime.UtcNow;
            machine.IsActive = true;
        }
        else
        {
            // New machine
            agentId = Guid.NewGuid();
            machine = new Machine
            {
                OrganizationId = enrollment.OrganizationId,
                AgentId = agentId,
                Hostname = hostname,
                OsName = os,
                OsVersion = osVersion,
                OsBuild = osBuild,
                PlatformId = platformId,
                ProductType = productType is > 0 ? (short)productType : null,
                Hwid = string.IsNullOrWhiteSpace(hwid) ? null : hwid,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                IsActive = true
            };
            _db.Machines.Add(machine);
        }

        // Trial propagation
        if (enrollment.IsTrial)
        {
            machine.IsTrial = true;
            machine.TrialExpiresAt = DateTime.UtcNow.AddDays(enrollment.TrialDays ?? 30);
        }

        // Generate per-machine auth keys — only for new machines or machines
        // with missing credentials. Re-enrollment preserves existing keys to
        // prevent desync if SaveChangesAsync fails downstream.
        if (string.IsNullOrEmpty(machine.MachineSecret))
        {
            var (machineSecret, sessionKey, expiresAt) = _keyRotation.GenerateInitialKeys();
            machine.MachineSecret = machineSecret;
            machine.SessionKey = sessionKey;
            machine.SessionKeyExpiresAt = expiresAt;
            machine.AuthVersion = 2;
            machine.KeyRotatedAt = DateTime.UtcNow;
        }

        // Mark enrollment code usage
        enrollment.UseCount++;
        if (enrollment.MaxUses is null)
        {
            // Single-use: mark fully consumed (backwards compatible)
            enrollment.UsedBy = machine.Id;
            enrollment.UsedAt = DateTime.UtcNow;
        }

        // Get org's API key (or generate one if missing)
        var org = enrollment.Organization;
        if (string.IsNullOrEmpty(org.ApiKey))
        {
            org.ApiKey = GenerateRandomCode(64).Replace("-", "");
            org.ApiSecret = GenerateRandomCode(128).Replace("-", "");
        }

        // Get active crypto key for the org
        var cryptoKey = await _db.OrgCryptoKeys
            .FirstOrDefaultAsync(x => x.OrganizationId == enrollment.OrganizationId && x.IsActive);

        // Resolve assessment: use the one from the enrollment code, or fall back
        // to the org's default assessment, or create one if none exists.
        var assessmentId = enrollment.AssessmentId;
        string? assessmentName = enrollment.Assessment?.Name;

        if (assessmentId is null)
        {
            var defaultAssessment = await _db.Assessments
                .FirstOrDefaultAsync(a => a.OrganizationId == enrollment.OrganizationId
                    && a.IsDefault && a.IsActive);

            if (defaultAssessment is null)
            {
                // Auto-create a default assessment with all active controls
                defaultAssessment = new Data.Entities.Assessment
                {
                    OrganizationId = enrollment.OrganizationId,
                    Name = "Full Assessment",
                    Description = "All active controls",
                    IsDefault = true,
                    IsActive = true
                };
                _db.Assessments.Add(defaultAssessment);
                await _db.SaveChangesAsync();

                // Link all active controls to this assessment
                var activeControlIds = await _db.ControlDefs
                    .Where(c => c.IsActive)
                    .Select(c => c.Id)
                    .ToListAsync();

                foreach (var ctrlId in activeControlIds)
                {
                    _db.AssessmentControls.Add(new Data.Entities.AssessmentControl
                    {
                        AssessmentId = defaultAssessment.Id,
                        ControlDefId = ctrlId
                    });
                }
                await _db.SaveChangesAsync();
            }

            assessmentId = defaultAssessment.Id;
            assessmentName = defaultAssessment.Name;
        }

        await _db.SaveChangesAsync();

        await _schedule.AssignSlotAsync(machine.Id, machine.OrganizationId);

        return new EnrollmentResult(
            AgentId: agentId,
            ApiKey: org.ApiKey,
            ApiSecret: org.ApiSecret!,
            PublicKeyPem: cryptoKey?.PublicKeyPem ?? "",
            AssessmentId: assessmentId,
            AssessmentName: assessmentName,
            ProtocolAuditEnabled: org.ProtocolAuditEnabled,
            IsTrial: enrollment.IsTrial,
            TrialExpiresAt: machine.TrialExpiresAt,
            OrganizationId: enrollment.OrganizationId,
            MachineSecret: machine.MachineSecret,
            SessionKey: machine.SessionKey,
            SessionKeyExpiresAt: machine.SessionKeyExpiresAt
        );
    }

    private static string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0, O, I, 1 (30 chars)
        const int charCount = 30;
        const int maxUnbiased = 256 - (256 % charCount); // 240
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            if (i > 0 && i % 5 == 4)
            {
                result[i] = '-';
                continue;
            }
            byte b;
            do { b = RandomNumberGenerator.GetBytes(1)[0]; }
            while (b >= maxUnbiased);
            result[i] = chars[b % charCount];
        }
        return new string(result);
    }
}
