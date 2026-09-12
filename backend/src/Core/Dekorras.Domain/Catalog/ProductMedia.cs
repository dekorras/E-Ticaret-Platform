using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = default!;
    public int DisplayOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    private ProductImage() { }

    public ProductImage(Guid productId, string url, int displayOrder, bool isPrimary)
    {
        ProductId = productId;
        Url = url;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
    }

    public void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

public class ProductVideo : BaseEntity
{
    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = default!;
    public string? Title { get; private set; }

    private ProductVideo() { }

    public ProductVideo(Guid productId, string url, string? title)
    {
        ProductId = productId;
        Url = url;
        Title = title;
    }
}
