using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class Brand : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Brand() { }

    public Brand(string name, string slug, string? logoUrl = null)
    {
        Name = name;
        Slug = slug;
        LogoUrl = logoUrl;
    }
}
