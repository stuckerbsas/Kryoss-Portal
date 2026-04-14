using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KryossApi.Data;

/// <summary>
/// EF Core SaveChanges interceptor that auto-sets audit columns
/// (CreatedBy/At, ModifiedBy/At, DeletedBy/At) on all IAuditable entities.
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;

    public AuditInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditFields(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext context)
    {
        var now = DateTime.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = userId;
                    // Prevent overwriting created fields
                    entry.Property(x => x.CreatedAt).IsModified = false;
                    entry.Property(x => x.CreatedBy).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // Convert hard delete to soft delete
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedBy = userId;
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = userId;
                    break;
            }
        }
    }
}

/// <summary>
/// Provides the current authenticated user's ID.
/// Set by middleware from JWT/API Key on each request.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
    Guid? FranchiseId { get; }
    Guid? OrganizationId { get; }
    bool IsAdmin { get; }
    string? Email { get; }
    string? DisplayName { get; }
    string? Phone { get; }
    string? JobTitle { get; }
    string? IpAddress { get; }
    string? SessionId { get; }
    string[] Permissions { get; }
}

public class CurrentUserService : ICurrentUserService
{
    public Guid UserId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? OrganizationId { get; set; }
    public bool IsAdmin { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
    public string[] Permissions { get; set; } = [];
}
