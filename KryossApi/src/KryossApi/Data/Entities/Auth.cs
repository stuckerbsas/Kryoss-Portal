namespace KryossApi.Data.Entities;

public class Module
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public short SortOrder { get; set; }
}

public class Action
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class Permission
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public int ActionId { get; set; }
    public string Slug { get; set; } = null!; // 'assessment:read'
    public string? Description { get; set; }

    public Module Module { get; set; } = null!;
    public Action Action { get; set; } = null!;
}

public class Role : IAuditable
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsSystem { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}

public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }

    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}

public class User : IAuditable
{
    public Guid Id { get; set; }
    public Guid? EntraOid { get; set; }
    public Guid? B2cOid { get; set; }
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public int RoleId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string AuthSource { get; set; } = null!; // entra, b2c
    public DateTime? LastLoginAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Role Role { get; set; } = null!;
    public Franchise? Franchise { get; set; }
    public Organization? Organization { get; set; }
}

public class Actlog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorIp { get; set; }
    public string? SessionId { get; set; }
    public string Severity { get; set; } = null!; // INFO, WARN, ERR, CRIT, SEC
    public string Module { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? RequestBody { get; set; }
    public short? ResponseCode { get; set; }
    public int? DurationMs { get; set; }
    public string? Message { get; set; }
}
