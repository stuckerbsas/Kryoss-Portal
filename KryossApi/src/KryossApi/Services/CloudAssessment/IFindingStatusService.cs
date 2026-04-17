using KryossApi.Data.Entities;

namespace KryossApi.Services.CloudAssessment;

public record FindingStatusDto(
    long Id,
    Guid OrganizationId,
    string Area,
    string Service,
    string Feature,
    string Status,
    Guid? OwnerUserId,
    string? Notes,
    DateTime UpdatedAt,
    Guid? UpdatedBy
);

public record SuggestionDto(
    long Id,
    Guid OrganizationId,
    Guid ScanId,
    string Area,
    string Service,
    string Feature,
    string SuggestionType,
    DateTime CreatedAt
);

public record RemediationStatsDto(
    int Open,
    int InProgress,
    int Resolved,
    int Deferred,
    int Total
);

public interface IFindingStatusService
{
    Task<FindingStatusDto?> GetStatusAsync(Guid orgId, string area, string service, string feature);
    Task<List<FindingStatusDto>> GetStatusesForOrgAsync(Guid orgId, string? area = null, string? statusFilter = null);

    // Explicit user action — no silent updates
    Task<FindingStatusDto> SetStatusAsync(
        Guid orgId, string area, string service, string feature,
        string status, string? notes, Guid? ownerUserId, Guid actorUserId);

    // Suggestions — computed, NOT applied
    Task<List<SuggestionDto>> ComputeSuggestionsForScanAsync(Guid orgId, Guid scanId);
    Task PersistSuggestionsAsync(Guid orgId, Guid scanId, List<SuggestionDto> suggestions);
    Task<List<SuggestionDto>> GetActiveSuggestionsAsync(Guid orgId);
    Task DismissSuggestionAsync(long suggestionId, Guid actorUserId);

    // Stats
    Task<RemediationStatsDto> GetStatsAsync(Guid orgId);
}
