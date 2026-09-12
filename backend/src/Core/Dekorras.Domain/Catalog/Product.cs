using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class Product : AuditableEntity
{
    public string Slug { get; private set; } = default!;
    public string ProductCode { get; private set; } = default!;
    public string? Sku { get; private set; }
    public string? Upc { get; private set; }
    public string? Ean { get; private set; }
    public string? Jan { get; private set; }
    public string? Isbn { get; private set; }
    public string? Mpn { get; private set; }
    public string? HsCode { get; private set; } // GTİP - e-ihracat gümrük kodu

    public Guid? BrandId { get; private set; }
    public Brand? Brand { get; private set; }

    public decimal BasePriceTry { get; private set; } // temel para birimi TRY
    public decimal TaxRatePercentage { get; private set; } // KDV %
    public UnitOfMeasure UnitOfMeasure { get; private set; }

    public int StockQuantity { get; private set; }
    public int MinimumOrderQuantity { get; private set; } = 1;
    public bool TrackStock { get; private set; } = true;
    public StockAvailability StockAvailability { get; private set; } = StockAvailability.InStock;
    public bool RequiresShipping { get; private set; } = true;

    public decimal? Length { get; private set; }
    public decimal? Width { get; private set; }
    public decimal? Height { get; private set; }
    public string? DimensionUnit { get; private set; }
    public decimal? Weight { get; private set; }
    public string? WeightUnit { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }
    public ProductStatus Status { get; private set; } = ProductStatus.Draft;
    public string? MetaRobots { get; private set; }
    public string? GoogleMerchantAgeGroup { get; private set; }
    public string? GoogleMerchantGender { get; private set; }
    public int DisplayOrder { get; private set; }

    private readonly List<ProductTranslation> _translations = [];
    public IReadOnlyCollection<ProductTranslation> Translations => _translations.AsReadOnly();

    private readonly List<ProductCategory> _productCategories = [];
    public IReadOnlyCollection<ProductCategory> ProductCategories => _productCategories.AsReadOnly();

    private readonly List<ProductImage> _images = [];
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    private readonly List<ProductVideo> _videos = [];
    public IReadOnlyCollection<ProductVideo> Videos => _videos.AsReadOnly();

    private readonly List<ProductVariant> _variants = [];
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    private readonly List<ProductAttributeValue> _attributeValues = [];
    public IReadOnlyCollection<ProductAttributeValue> AttributeValues => _attributeValues.AsReadOnly();

    private readonly List<QuantityDiscount> _quantityDiscounts = [];
    public IReadOnlyCollection<QuantityDiscount> QuantityDiscounts => _quantityDiscounts.AsReadOnly();

    private readonly List<ProductGroupPrice> _groupPrices = [];
    public IReadOnlyCollection<ProductGroupPrice> GroupPrices => _groupPrices.AsReadOnly();

    private readonly List<RelatedProduct> _relatedProducts = [];
    public IReadOnlyCollection<RelatedProduct> RelatedProducts => _relatedProducts.AsReadOnly();

    private Product() { }

    public Product(string slug, string productCode, decimal basePriceTry, decimal taxRatePercentage, UnitOfMeasure unitOfMeasure)
    {
        Slug = slug;
        ProductCode = productCode;
        BasePriceTry = basePriceTry;
        TaxRatePercentage = taxRatePercentage;
        UnitOfMeasure = unitOfMeasure;
    }

    public void SetTranslation(string languageCode, string name, string? description, string? metaTitle, string? metaDescription, string? metaKeywords)
    {
        var existing = _translations.FirstOrDefault(t => t.LanguageCode == languageCode);
        if (existing is not null)
        {
            existing.Update(name, description, metaTitle, metaDescription, metaKeywords);
            return;
        }
        _translations.Add(new ProductTranslation(Id, languageCode, name, description, metaTitle, metaDescription, metaKeywords));
    }

    public void AssignToCategory(Guid categoryId, bool isPrimary = false)
    {
        if (_productCategories.Any(pc => pc.CategoryId == categoryId)) return;
        _productCategories.Add(new ProductCategory(Id, categoryId, isPrimary));
    }

    public void RemoveFromCategory(Guid categoryId) =>
        _productCategories.RemoveAll(pc => pc.CategoryId == categoryId);

    public void AddImage(string url, int displayOrder, bool isPrimary = false) =>
        _images.Add(new ProductImage(Id, url, displayOrder, isPrimary || _images.Count == 0));

    /// <summary>Not: çağırmadan önce Images koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek görsel bulunamaz.</summary>
    public void RemoveImage(Guid imageId)
    {
        var wasPrimary = _images.FirstOrDefault(i => i.Id == imageId)?.IsPrimary ?? false;
        _images.RemoveAll(i => i.Id == imageId);

        if (wasPrimary && _images.Count > 0)
            _images[0].SetPrimary(true);
    }

    /// <summary>Not: çağırmadan önce Images koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde mevcut ana görsel bulunup değiştirilemez.</summary>
    public void SetPrimaryImage(Guid imageId)
    {
        foreach (var image in _images)
            image.SetPrimary(image.Id == imageId);
    }

    public void AddVideo(string url, string? title) =>
        _videos.Add(new ProductVideo(Id, url, title));

    /// <summary>Not: çağırmadan önce Videos koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek video bulunamaz.</summary>
    public void RemoveVideo(Guid videoId) => _videos.RemoveAll(v => v.Id == videoId);

    public void AddVariant(ProductVariant variant) => _variants.Add(variant);

    /// <summary>Not: çağırmadan önce Variants koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek varyant bulunamaz.</summary>
    public void RemoveVariant(Guid variantId) => _variants.RemoveAll(v => v.Id == variantId);

    public void AddAttributeValue(ProductAttributeValue value) => _attributeValues.Add(value);

    /// <summary>Not: çağırmadan önce AttributeValues koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek özellik değeri bulunamaz.</summary>
    public void RemoveAttributeValue(Guid attributeValueId) => _attributeValues.RemoveAll(v => v.Id == attributeValueId);

    public void AddQuantityDiscount(int minimumQuantity, decimal priceTry) =>
        _quantityDiscounts.Add(new QuantityDiscount(Id, minimumQuantity, priceTry));

    /// <summary>Not: çağırmadan önce QuantityDiscounts koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek kademe bulunamaz.</summary>
    public void RemoveQuantityDiscount(Guid quantityDiscountId) => _quantityDiscounts.RemoveAll(d => d.Id == quantityDiscountId);

    public void SetGroupPrice(Guid customerGroupId, decimal priceTry)
    {
        var existing = _groupPrices.FirstOrDefault(g => g.CustomerGroupId == customerGroupId);
        if (existing is not null)
        {
            existing.SetPrice(priceTry);
            return;
        }
        _groupPrices.Add(new ProductGroupPrice(Id, customerGroupId, priceTry));
    }

    /// <summary>Not: çağırmadan önce GroupPrices koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek fiyat bulunamaz.</summary>
    public void RemoveGroupPrice(Guid customerGroupId) => _groupPrices.RemoveAll(g => g.CustomerGroupId == customerGroupId);

    public decimal GetPriceFor(Guid? customerGroupId)
    {
        if (customerGroupId is null) return BasePriceTry;
        var groupPrice = _groupPrices.FirstOrDefault(g => g.CustomerGroupId == customerGroupId);
        return groupPrice?.PriceTry ?? BasePriceTry;
    }

    public void UpdateStock(int quantity)
    {
        StockQuantity = quantity;
        if (TrackStock)
            StockAvailability = quantity > 0 ? StockAvailability.InStock : StockAvailability.OutOfStock;
    }

    /// <summary>Admin'in "Stok Dışı Durumu" alanını elle seçmesi için (bkz. plan §2.4 -
    /// "Stok Dışı Durumu: 2-3 gün içinde / Ön Sipariş / Stokta var / Stokta yok"). `TrackStock`
    /// AKTİFSE, checkout'taki `UpdateStock` GERÇEK stok miktarına göre bunu InStock/OutOfStock'a
    /// GERİ DÖNDÜRÜR - bu yüzden `PreOrder`/`ArrivesInDays` yalnızca stok takibi KAPALI ürünlerde
    /// (ör. sipariş üzerine üretilen/tedarik edilen ürünler) kalıcıdır, bu bilinçli bir tasarımdır.</summary>
    public void SetStockAvailability(StockAvailability status) => StockAvailability = status;

    /// <summary>Bkz. plan §2.4 - "Stoktan Düş (E/H)". Faz 0/1'den beri `TrackStock` alanı VARDI ama
    /// hiçbir yerden DEĞİŞTİRİLEMİYORDU (her zaman varsayılan `true` kalıyordu) - bu da
    /// `SetStockAvailability`'nin `PreOrder`/`ArrivesInDays` durumlarını PRATİKTE hiçbir zaman kalıcı
    /// kılamayacağı anlamına geliyordu (bkz. o metodun belgesi). Bu ikisi birlikte kapatılmalıydı.</summary>
    public void SetTrackStock(bool trackStock) => TrackStock = trackStock;

    public void Publish()
    {
        Status = ProductStatus.Active;
        PublishedAtUtc ??= DateTime.UtcNow;
    }

    public void Unpublish() => Status = ProductStatus.Inactive;

    public void SetIdentifiers(string? sku, string? upc, string? ean, string? jan, string? isbn, string? mpn)
    {
        Sku = sku; Upc = upc; Ean = ean; Jan = jan; Isbn = isbn; Mpn = mpn;
    }

    public void SetHsCode(string? hsCode) => HsCode = hsCode;

    /// <summary>Ör. "noindex,nofollow" - arama motorlarının bu ürün sayfasını dizinlemesini/
    /// izlemesini engellemek için (bkz. Storefront `_Layout.cshtml`'in `MetaDescription`/
    /// `MetaKeywords`'le aynı desende render ettiği `&lt;meta name="robots"&gt;` etiketi).</summary>
    public void SetMetaRobots(string? metaRobots) => MetaRobots = metaRobots;

    /// <summary>Kargo/gümrük beyanı için paket boyutları (bkz. plan §7 "boyut/ağırlık") - `Weight`/
    /// `WeightUnit`'e KASITLI OLARAK dokunmaz (bkz. SetWeight), aksi halde ayrı ayrı düzenlenebilir
    /// iki alan grubu birbirini ezerdi. Birim her zaman santimetredir.</summary>
    public void SetPackageDimensions(decimal? lengthCm, decimal? widthCm, decimal? heightCm)
    {
        Length = lengthCm; Width = widthCm; Height = heightCm;
        DimensionUnit = lengthCm is null && widthCm is null && heightCm is null ? null : "cm";
    }

    /// <summary>Kargo ücreti hesaplamasında kullanılan ağırlık - bkz. PlaceOrderCommand.DefaultItemWeightKg
    /// (bu ayarlanmazsa checkout varsayılan 1kg kullanır). Boyutlara (Length/Width/Height) dokunmaz.</summary>
    public void SetWeight(decimal? weightKg)
    {
        Weight = weightKg;
        WeightUnit = weightKg is null ? null : "kg";
    }

    public void SetMinimumOrderQuantity(int quantity) => MinimumOrderQuantity = quantity < 1 ? 1 : quantity;

    public void AssignBrand(Guid? brandId) => BrandId = brandId;

    public void UpdateSlug(string slug) => Slug = slug;

    public void UpdateProductCode(string productCode) => ProductCode = productCode;

    public void UpdatePricing(decimal basePriceTry, decimal taxRatePercentage)
    {
        BasePriceTry = basePriceTry;
        TaxRatePercentage = taxRatePercentage;
    }

    public void UpdateUnitOfMeasure(UnitOfMeasure unitOfMeasure) => UnitOfMeasure = unitOfMeasure;

    /// <summary>Ürünün kategori atamalarını verilen kümeyle birebir eşleşecek şekilde değiştirir.</summary>
    public void SetCategories(IReadOnlyCollection<Guid> categoryIds)
    {
        var toRemove = _productCategories.Where(pc => !categoryIds.Contains(pc.CategoryId)).Select(pc => pc.CategoryId).ToList();
        foreach (var categoryId in toRemove)
            RemoveFromCategory(categoryId);

        var isFirst = _productCategories.Count == 0;
        foreach (var categoryId in categoryIds)
        {
            AssignToCategory(categoryId, isPrimary: isFirst);
            isFirst = false;
        }
    }

    /// <summary>Bkz. plan §2.4 - "Bağlantılar" sekmesi: "ilgili ürünler". Tek yönlüdür (Admin bu
    /// ürünün sayfasında GÖSTERİLECEK ilgili ürünleri seçer) - karşılıklı bir "sen de bana ilgili
    /// ürün olarak eklendin" ilişkisi OTOMATİK KURULMAZ, bu bilinçli bir tasarım basitleştirmesidir.</summary>
    public void AddRelatedProduct(Guid relatedProductId)
    {
        if (relatedProductId == Id) throw new DomainException("Bir ürün kendisiyle ilişkilendirilemez.");
        if (_relatedProducts.Any(r => r.RelatedProductId == relatedProductId)) return;
        _relatedProducts.Add(new RelatedProduct(Id, relatedProductId));
    }

    /// <summary>Not: çağırmadan önce RelatedProducts koleksiyonunun IRepository&lt;T&gt;.LoadCollectionAsync
    /// ile yüklenmiş olması gerekir, aksi halde silinecek ilişki bulunamaz.</summary>
    public void RemoveRelatedProduct(Guid relatedProductId) => _relatedProducts.RemoveAll(r => r.RelatedProductId == relatedProductId);
}

public class ProductTranslation : BaseEntity
{
    public Guid ProductId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }
    public string? MetaKeywords { get; private set; }

    private ProductTranslation() { }

    public ProductTranslation(Guid productId, string languageCode, string name, string? description, string? metaTitle, string? metaDescription, string? metaKeywords)
    {
        ProductId = productId;
        LanguageCode = languageCode;
        Name = name;
        Description = description;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
        MetaKeywords = metaKeywords;
    }

    public void Update(string name, string? description, string? metaTitle, string? metaDescription, string? metaKeywords)
    {
        Name = name;
        Description = description;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
        MetaKeywords = metaKeywords;
    }
}

public class ProductCategory : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid CategoryId { get; private set; }
    public bool IsPrimary { get; private set; }

    private ProductCategory() { }

    public ProductCategory(Guid productId, Guid categoryId, bool isPrimary)
    {
        ProductId = productId;
        CategoryId = categoryId;
        IsPrimary = isPrimary;
    }
}

public class ProductGroupPrice : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid CustomerGroupId { get; private set; }
    public decimal PriceTry { get; private set; }

    private ProductGroupPrice() { }

    public ProductGroupPrice(Guid productId, Guid customerGroupId, decimal priceTry)
    {
        ProductId = productId;
        CustomerGroupId = customerGroupId;
        PriceTry = priceTry;
    }

    public void SetPrice(decimal priceTry) => PriceTry = priceTry;
}

public class RelatedProduct : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid RelatedProductId { get; private set; }

    private RelatedProduct() { }

    public RelatedProduct(Guid productId, Guid relatedProductId)
    {
        ProductId = productId;
        RelatedProductId = relatedProductId;
    }
}

public class QuantityDiscount : BaseEntity
{
    public Guid ProductId { get; private set; }
    public int MinimumQuantity { get; private set; }
    public decimal PriceTry { get; private set; }

    private QuantityDiscount() { }

    public QuantityDiscount(Guid productId, int minimumQuantity, decimal priceTry)
    {
        ProductId = productId;
        MinimumQuantity = minimumQuantity;
        PriceTry = priceTry;
    }
}
