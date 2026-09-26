using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Ölçüye özel duvar kağıdının basıldığı malzeme - fiyatın TEK kaynağı (₺/m², KDV HARİÇ;
/// mevcut sistemin geri kalanıyla aynı: KDV sepette fiyatın üzerine eklenir).</summary>
public class Material : AuditableEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Satır satır özellik listesi (ürün sayfasındaki malzeme kartında madde olarak gösterilir).</summary>
    public string? Features { get; private set; }

    public decimal PricePerM2 { get; private set; }
    public decimal PanelWidthCm { get; private set; }
    public decimal MaxHeightCm { get; private set; }
    public int WeightGsm { get; private set; }
    public string? FireRating { get; private set; }
    public bool IsSelfAdhesive { get; private set; }
    public bool RequiresGlue { get; private set; }
    public decimal BleedCm { get; private set; } = 5m;
    public decimal MinBillableAreaM2 { get; private set; } = 1m;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Bu malzemenin numunesi olarak satılan (stoklu, sabit fiyatlı) normal katalog ürünü.</summary>
    public Guid? SampleProductId { get; private set; }

    /// <summary>RequiresGlue ise sepette önerilecek tutkal ürünü (normal katalog ürünü).</summary>
    public Guid? GlueProductId { get; private set; }

    private Material() { }

    public Material(string code, string name, decimal pricePerM2, decimal panelWidthCm, decimal maxHeightCm, int weightGsm,
        string? fireRating, bool isSelfAdhesive, bool requiresGlue, int sortOrder, decimal bleedCm = 5m, decimal minBillableAreaM2 = 1m)
    {
        Code = code;
        Update(name, null, null, pricePerM2, panelWidthCm, maxHeightCm, weightGsm, fireRating, isSelfAdhesive, requiresGlue, bleedCm, minBillableAreaM2, sortOrder);
    }

    public void Update(string name, string? description, string? features, decimal pricePerM2, decimal panelWidthCm, decimal maxHeightCm,
        int weightGsm, string? fireRating, bool isSelfAdhesive, bool requiresGlue, decimal bleedCm, decimal minBillableAreaM2, int sortOrder)
    {
        if (pricePerM2 <= 0) throw new DomainException("Malzeme m² fiyatı sıfırdan büyük olmalıdır.");
        if (panelWidthCm <= 0) throw new DomainException("Panel eni sıfırdan büyük olmalıdır.");
        if (maxHeightCm < WallDimensions.MinSideCm) throw new DomainException($"Maksimum yükseklik en az {WallDimensions.MinSideCm} cm olmalıdır.");
        if (bleedCm < 0) throw new DomainException("Kesim payı negatif olamaz.");
        if (minBillableAreaM2 < 0) throw new DomainException("Minimum faturalanan alan negatif olamaz.");

        Name = name;
        Description = description;
        Features = features;
        PricePerM2 = pricePerM2;
        PanelWidthCm = panelWidthCm;
        MaxHeightCm = maxHeightCm;
        WeightGsm = weightGsm;
        FireRating = fireRating;
        IsSelfAdhesive = isSelfAdhesive;
        RequiresGlue = requiresGlue;
        BleedCm = bleedCm;
        MinBillableAreaM2 = minBillableAreaM2;
        SortOrder = sortOrder;
    }

    public void LinkProducts(Guid? sampleProductId, Guid? glueProductId)
    {
        SampleProductId = sampleProductId;
        GlueProductId = glueProductId;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>Ürün bazında malzeme fiyatı istisnası veya malzemenin o üründe kapatılması.</summary>
public class ProductMaterialOverride : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal? PricePerM2 { get; private set; }
    public bool IsAllowed { get; private set; } = true;

    private ProductMaterialOverride() { }

    public ProductMaterialOverride(Guid productId, Guid materialId, decimal? pricePerM2, bool isAllowed)
    {
        ProductId = productId;
        MaterialId = materialId;
        Set(pricePerM2, isAllowed);
    }

    public void Set(decimal? pricePerM2, bool isAllowed)
    {
        if (pricePerM2 is <= 0) throw new DomainException("Özel m² fiyatı sıfırdan büyük olmalıdır.");
        PricePerM2 = pricePerM2;
        IsAllowed = isAllowed;
    }
}
