namespace KryossApi.Data;

/// <summary>
/// All entities with audit columns implement this interface.
/// AuditInterceptor auto-sets these fields on SaveChanges.
/// </summary>
public interface IAuditable
{
    Guid CreatedBy { get; set; }
    DateTime CreatedAt { get; set; }
    Guid? ModifiedBy { get; set; }
    DateTime? ModifiedAt { get; set; }
    Guid? DeletedBy { get; set; }
    DateTime? DeletedAt { get; set; }
}
