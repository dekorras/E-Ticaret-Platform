using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class ProductAttribute : AuditableEntity
{
    public string Name { get; private set; } = default!; // ör. Renk, Ölçü

    private ProductAttribute() { }

    public ProductAttribute(string name) => Name = name;
}

public class ProductAttributeValue : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid ProductAttributeId { get; private set; }
    public string Value { get; private set; } = default!;

    private ProductAttributeValue() { }

    public ProductAttributeValue(Guid productId, Guid productAttributeId, string value)
    {
        ProductId = productId;
        ProductAttributeId = productAttributeId;
        Value = value;
    }

    public void UpdateValue(string value) => Value = value;
}

public class ProductVariant : BaseEntity
{
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = default!;
    public string OptionName { get; private set; } = default!; // ör. "Ölçü: 60x60"
    public decimal? PriceAdjustmentTry { get; private set; }
    public int StockQuantity { get; private set; }

    private ProductVariant() { }

    public ProductVariant(Guid productId, string sku, string optionName, decimal? priceAdjustmentTry, int stockQuantity)
    {
        ProductId = productId;
        Sku = sku;
        OptionName = optionName;
        PriceAdjustmentTry = priceAdjustmentTry;
        StockQuantity = stockQuantity;
    }

    public void UpdateStock(int quantity) => StockQuantity = quantity;

    public void UpdateDetails(string optionName, decimal? priceAdjustmentTry, int stockQuantity)
    {
        OptionName = optionName;
        PriceAdjustmentTry = priceAdjustmentTry;
        StockQuantity = stockQuantity;
    }
}
