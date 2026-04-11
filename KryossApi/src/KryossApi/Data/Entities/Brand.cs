namespace KryossApi.Data.Entities;

public class Brand
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string ColorPrimary { get; set; } = null!;
    public string ColorAccent { get; set; } = null!;
    public string? ColorDarkBg { get; set; }
    public string? LogoUrl { get; set; }
    public string FontFamily { get; set; } = "Montserrat";
    public bool IsActive { get; set; } = true;
}
