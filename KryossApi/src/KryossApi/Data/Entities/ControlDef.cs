namespace KryossApi.Data.Entities;

public class ControlDef : IAuditable
{
    public int Id { get; set; }
    public string ControlId { get; set; } = null!; // BL-001
    public int CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!; // registry, secedit, auditpol, firewall, service, netaccount, command
    public string? Severity { get; set; } // low, medium, high, critical
    public string CheckJson { get; set; } = null!; // JSON: agent instructions + expected values
    public string? Remediation { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    // Audit
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation
    public ControlCategory Category { get; set; } = null!;
    public ICollection<ControlFramework> ControlFrameworks { get; set; } = [];
    public ICollection<ControlPlatform> ControlPlatforms { get; set; } = [];
}

public class ControlCategory : IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public short SortOrder { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Framework : IAuditable
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Version { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Platform : IAuditable
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class ControlFramework
{
    public int ControlDefId { get; set; }
    public int FrameworkId { get; set; }
    public string? FrameworkRef { get; set; } // e.g. 'CIS 5.2.1'

    public ControlDef ControlDef { get; set; } = null!;
    public Framework Framework { get; set; } = null!;
}

public class ControlPlatform
{
    public int ControlDefId { get; set; }
    public int PlatformId { get; set; }

    public ControlDef ControlDef { get; set; } = null!;
    public Platform Platform { get; set; } = null!;
}
