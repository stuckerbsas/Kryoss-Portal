using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.InfraAssessment.Pipelines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.InfraAssessment;

public class InfraAssessmentService : IInfraAssessmentService
{
    private readonly KryossDbContext _db;
    private readonly IHypervisorPipeline _hypervisor;
    private readonly ILogger<InfraAssessmentService> _log;

    public InfraAssessmentService(KryossDbContext db, IHypervisorPipeline hypervisor, ILogger<InfraAssessmentService> log)
    {
        _db = db;
        _hypervisor = hypervisor;
        _log = log;
    }

    public async Task<InfraAssessmentScan> StartScanAsync(Guid organizationId, string? scope)
    {
        var scan = new InfraAssessmentScan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = "running",
            Scope = scope,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.InfraAssessmentScans.Add(scan);
        await _db.SaveChangesAsync();

        try
        {
            var result = await _hypervisor.CollectAsync(scan.Id, organizationId);

            // Persist findings
            if (result.Findings.Count > 0)
            {
                _db.InfraAssessmentFindings.AddRange(result.Findings);
            }

            scan.Status = "completed";
            scan.DeviceCount = result.HostsDiscovered + result.VmsDiscovered;
            scan.FindingCount = result.Findings.Count;
            scan.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "IA scan failed for org {OrgId}", organizationId);
            scan.Status = "failed";
            scan.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return scan;
    }

    public async Task<InfraAssessmentScan?> GetLatestAsync(Guid organizationId)
    {
        return await _db.InfraAssessmentScans
            .Include(s => s.Sites)
            .Include(s => s.Devices)
            .Include(s => s.Connectivity)
            .Include(s => s.Capacity)
            .Include(s => s.Findings)
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<InfraAssessmentScan?> GetDetailAsync(Guid scanId)
    {
        return await _db.InfraAssessmentScans
            .Include(s => s.Sites)
            .Include(s => s.Devices)
            .Include(s => s.Connectivity)
            .Include(s => s.Capacity)
            .Include(s => s.Findings)
            .FirstOrDefaultAsync(s => s.Id == scanId);
    }

    public async Task<List<InfraAssessmentScan>> GetHistoryAsync(Guid organizationId, int take = 20)
    {
        return await _db.InfraAssessmentScans
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(take)
            .ToListAsync();
    }
}
