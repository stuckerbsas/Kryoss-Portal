namespace KryossApi.Data.Entities;

public class EnrollmentCode : IAuditable
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = null!;
    public int? AssessmentId { get; set; }
    public string? Label { get; set; }
    public Guid? UsedBy { get; set; } // machine_id
    public DateTime? UsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public int? MaxUses { get; set; }       // NULL = single-use, N = can enroll N machines
    public int UseCount { get; set; }        // how many machines have used this code

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Assessment? Assessment { get; set; }
    public Machine? UsedByMachine { get; set; }
}

public class OrgCryptoKey : IAuditable
{
    public int Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string PublicKeyPem { get; set; } = null!;
    public string KeyVaultRef { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime? RotatedAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
