using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.CloudAssessment;

public class FindingStatusService : IFindingStatusService
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        "open", "in_progress", "resolved", "deferred", "acknowledged_regression"
    };

    private readonly KryossDbContext _db;

    public FindingStatusService(KryossDbContext db)
    {
        _db = db;
    }

    // ── GetStatusAsync ──────────────────────────────────────────────────────

    public async Task<FindingStatusDto?> GetStatusAsync(
        Guid orgId, string area, string service, string feature)
    {
        var entity = await _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId
                     && s.Area == area
                     && s.Service == service
                     && s.Feature == feature)
            .FirstOrDefaultAsync();

        return entity is null ? null : ToDto(entity);
    }

    // ── GetStatusesForOrgAsync ──────────────────────────────────────────────

    public async Task<List<FindingStatusDto>> GetStatusesForOrgAsync(
        Guid orgId, string? area = null, string? statusFilter = null)
    {
        var query = _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId);

        if (!string.IsNullOrEmpty(area))
            query = query.Where(s => s.Area == area);

        if (!string.IsNullOrEmpty(statusFilter))
            query = query.Where(s => s.Status == statusFilter);

        var entities = await query
            .OrderBy(s => s.Area)
            .ThenBy(s => s.Service)
            .ThenBy(s => s.Feature)
            .ToListAsync();

        return entities.Select(ToDto).ToList();
    }

    // ── SetStatusAsync ──────────────────────────────────────────────────────

    public async Task<FindingStatusDto> SetStatusAsync(
        Guid orgId, string area, string service, string feature,
        string status, string? notes, Guid? ownerUserId, Guid actorUserId)
    {
        if (!ValidStatuses.Contains(status))
            throw new ArgumentException(
                $"Invalid status: {status}. Valid: open, in_progress, resolved, deferred, acknowledged_regression");

        var existing = await _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId
                     && s.Area == area
                     && s.Service == service
                     && s.Feature == feature)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            existing = new CloudAssessmentFindingStatus
            {
                OrganizationId = orgId,
                Area = area,
                Service = service,
                Feature = feature,
                Status = status,
                Notes = notes,
                OwnerUserId = ownerUserId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = actorUserId
            };
            _db.CloudAssessmentFindingStatuses.Add(existing);
        }
        else
        {
            existing.Status = status;
            existing.Notes = notes;
            existing.OwnerUserId = ownerUserId;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = actorUserId;
        }

        await _db.SaveChangesAsync();
        return ToDto(existing);
    }

    // ── ComputeSuggestionsForScanAsync ──────────────────────────────────────

    public async Task<List<SuggestionDto>> ComputeSuggestionsForScanAsync(
        Guid orgId, Guid scanId)
    {
        // Step 1: findings present in the current scan (by area+service+feature key)
        var scanFindingKeys = await _db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .Select(f => new { f.Area, f.Service, f.Feature })
            .ToListAsync();

        var scanKeys = scanFindingKeys
            .Select(f => (f.Area, f.Service, f.Feature))
            .ToHashSet();

        // Step 2: open/in_progress statuses for this org
        var activeStatuses = await _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId
                     && (s.Status == "open" || s.Status == "in_progress"))
            .ToListAsync();

        // Step 3: active findings NOT present in the scan → likely_resolved
        var suggestions = activeStatuses
            .Where(s => !scanKeys.Contains((s.Area, s.Service, s.Feature)))
            .Select(s => new SuggestionDto(
                Id: 0,
                OrganizationId: orgId,
                ScanId: scanId,
                Area: s.Area,
                Service: s.Service,
                Feature: s.Feature,
                SuggestionType: "likely_resolved",
                CreatedAt: DateTime.UtcNow))
            .ToList();

        // Step 4: resolved statuses for this org
        var resolvedStatuses = await _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId && s.Status == "resolved")
            .ToListAsync();

        // Step 5: resolved findings that ARE present in the scan → possible_regression
        var regressions = resolvedStatuses
            .Where(s => scanKeys.Contains((s.Area, s.Service, s.Feature)))
            .Select(s => new SuggestionDto(
                Id: 0,
                OrganizationId: orgId,
                ScanId: scanId,
                Area: s.Area,
                Service: s.Service,
                Feature: s.Feature,
                SuggestionType: "possible_regression",
                CreatedAt: DateTime.UtcNow));

        suggestions.AddRange(regressions);
        return suggestions;
    }

    // ── PersistSuggestionsAsync ─────────────────────────────────────────────

    public async Task PersistSuggestionsAsync(
        Guid orgId, Guid scanId, List<SuggestionDto> suggestions)
    {
        // Delete existing suggestions for this (orgId, scanId)
        var existing = await _db.CloudAssessmentSuggestions
            .Where(s => s.OrganizationId == orgId && s.ScanId == scanId)
            .ToListAsync();

        _db.CloudAssessmentSuggestions.RemoveRange(existing);

        // Insert new
        var entities = suggestions.Select(dto => new CloudAssessmentSuggestion
        {
            OrganizationId = orgId,
            ScanId = scanId,
            Area = dto.Area,
            Service = dto.Service,
            Feature = dto.Feature,
            SuggestionType = dto.SuggestionType,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _db.CloudAssessmentSuggestions.AddRange(entities);
        await _db.SaveChangesAsync();
    }

    // ── GetActiveSuggestionsAsync ───────────────────────────────────────────

    public async Task<List<SuggestionDto>> GetActiveSuggestionsAsync(Guid orgId)
    {
        var entities = await _db.CloudAssessmentSuggestions
            .Where(s => s.OrganizationId == orgId && s.DismissedAt == null)
            .OrderBy(s => s.Area)
            .ThenBy(s => s.Service)
            .ThenBy(s => s.Feature)
            .ToListAsync();

        return entities.Select(ToSuggestionDto).ToList();
    }

    // ── DismissSuggestionAsync ──────────────────────────────────────────────

    public async Task DismissSuggestionAsync(long suggestionId, Guid actorUserId)
    {
        var suggestion = await _db.CloudAssessmentSuggestions
            .FirstOrDefaultAsync(s => s.Id == suggestionId);

        if (suggestion is null)
            throw new InvalidOperationException($"Suggestion {suggestionId} not found");

        suggestion.DismissedAt = DateTime.UtcNow;
        suggestion.DismissedBy = actorUserId;
        await _db.SaveChangesAsync();
    }

    // ── GetStatsAsync ───────────────────────────────────────────────────────

    public async Task<RemediationStatsDto> GetStatsAsync(Guid orgId)
    {
        var counts = await _db.CloudAssessmentFindingStatuses
            .Where(s => s.OrganizationId == orgId)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Get(string key) => counts.FirstOrDefault(c => c.Status == key)?.Count ?? 0;

        var open = Get("open");
        var inProgress = Get("in_progress");
        var resolved = Get("resolved");
        var deferred = Get("deferred");
        var total = counts.Sum(c => c.Count);

        return new RemediationStatsDto(open, inProgress, resolved, deferred, total);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    private static FindingStatusDto ToDto(CloudAssessmentFindingStatus e) => new(
        e.Id, e.OrganizationId, e.Area, e.Service, e.Feature,
        e.Status, e.OwnerUserId, e.Notes, e.UpdatedAt, e.UpdatedBy);

    private static SuggestionDto ToSuggestionDto(CloudAssessmentSuggestion e) => new(
        e.Id, e.OrganizationId, e.ScanId, e.Area, e.Service, e.Feature,
        e.SuggestionType, e.CreatedAt);
}
