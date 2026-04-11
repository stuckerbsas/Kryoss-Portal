namespace KryossApi.Data.Entities;

public class AdHygieneScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ScannedBy { get; set; } = null!;
    public DateTime ScannedAt { get; set; }
    public int TotalMachines { get; set; }
    public int TotalUsers { get; set; }
    public int StaleMachines { get; set; }
    public int DormantMachines { get; set; }
    public int StaleUsers { get; set; }
    public int DormantUsers { get; set; }
    public int DisabledUsers { get; set; }
    public int PwdNeverExpire { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<AdHygieneFinding> Findings { get; set; } = [];
}

public class AdHygieneFinding
{
    public long Id { get; set; }
    public Guid ScanId { get; set; }
    public string Name { get; set; } = null!;
    public string ObjectType { get; set; } = null!; // Computer, User
    public string Status { get; set; } = null!;     // Stale, Dormant, Disabled, PwdNeverExpires, OldPassword
    public int DaysInactive { get; set; }
    public string? Detail { get; set; }

    public AdHygieneScan Scan { get; set; } = null!;
}
