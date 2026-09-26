using Dekorras.Domain.Common;

namespace Dekorras.Domain.Ordering;

public class Cart : AuditableEntity
{
    public Guid? CustomerId { get; private set; } // misafir alışverişinde null
    public string SessionKey { get; private set; } = default!;
    public string? CouponCode { get; private set; }
    public string? GiftVoucherCode { get; private set; } // Coupon'un aksine bir İNDİRİM değil, bir ÖDEME yöntemidir - kuponla birlikte kullanılabilir

    private readonly List<CartItem> _items = [];
    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    private Cart() { }

    public Cart(string sessionKey, Guid? customerId = null)
    {
        SessionKey = sessionKey;
        CustomerId = customerId;
    }

    public void AddOrUpdateItem(Guid productId, Guid? variantId, int quantity, decimal unitPriceTry)
    {
        // Konfigüre (ölçüye özel) satırlar ConfigHash ile ayrı eşleşir - düz ürün satırıyla asla birleşmez.
        var existing = _items.FirstOrDefault(i => i.ProductId == productId && i.VariantId == variantId && i.ConfigHash == null);
        if (existing is not null)
        {
            existing.SetQuantity(quantity);
            // Birim fiyat da güncellenir: adet arttıkça bir toplu alım indirimi kademesine
            // girilmiş/çıkılmış olabilir (bkz. QuantityDiscount) - çağıran taraf her zaman
            // GÜNCEL fiyatı hesaplayıp buraya geçirir.
            existing.SetUnitPrice(unitPriceTry);
            return;
        }
        _items.Add(new CartItem(Id, productId, variantId, quantity, unitPriceTry));
    }

    /// <summary>Ölçüye özel duvar kağıdı satırı. Aynı ürün + aynı konfigürasyon (ConfigHash) varsa
    /// adet ARTIRILIR, farklı ölçü/malzeme ayrı satır olur. Fiyat daima çağıran tarafça SUNUCUDA
    /// hesaplanmış olmalıdır (istemciden gelen fiyat asla kullanılmaz).</summary>
    public CartItem AddConfiguredItem(Guid productId, string configurationJson, string configHash, string priceSnapshotJson, int quantity, decimal unitPriceTry)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId && i.ConfigHash == configHash);
        if (existing is not null)
        {
            existing.SetQuantity(existing.Quantity + quantity);
            existing.SetUnitPrice(unitPriceTry);
            existing.SetPriceSnapshot(priceSnapshotJson);
            return existing;
        }

        var item = new CartItem(Id, productId, null, quantity, unitPriceTry);
        item.SetConfiguration(configurationJson, configHash, priceSnapshotJson);
        _items.Add(item);
        return item;
    }

    public void RemoveItem(Guid productId, Guid? variantId) => _items.RemoveAll(i => i.ProductId == productId && i.VariantId == variantId && i.ConfigHash == null);
    public void RemoveItemById(Guid cartItemId) => _items.RemoveAll(i => i.Id == cartItemId);
    public void SetCustomerId(Guid customerId) => CustomerId = customerId;

    /// <summary>Giriş yapan bir müşterinin başka bir cihazda kayıtlı sepetini, o cihazın tarayıcı
    /// çerezindeki (aktif) oturum anahtarına taşımak için kullanılır - bkz.
    /// `MergeGuestCartIntoCustomerCommand`. Bundan sonra o cihazdaki sepet sorguları (SessionKey'e
    /// göre arar) bu sepeti bulur.</summary>
    public void SetSessionKey(string sessionKey) => SessionKey = sessionKey;
    public void ApplyCoupon(string couponCode) => CouponCode = couponCode;
    public void RemoveCoupon() => CouponCode = null;
    public void ApplyGiftVoucher(string giftVoucherCode) => GiftVoucherCode = giftVoucherCode;
    public void RemoveGiftVoucher() => GiftVoucherCode = null;
    public void Clear()
    {
        _items.Clear();
        CouponCode = null;
        GiftVoucherCode = null;
    }
}

public class CartItem : BaseEntity
{
    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPriceTry { get; private set; }

    private CartItem() { }

    public CartItem(Guid cartId, Guid productId, Guid? variantId, int quantity, decimal unitPriceTry)
    {
        CartId = cartId;
        ProductId = productId;
        VariantId = variantId;
        Quantity = quantity;
        UnitPriceTry = unitPriceTry;
    }

    /// <summary>Ölçüye özel satırda WallConfiguration JSON'u; düz ürünlerde null.</summary>
    public string? ConfigurationJson { get; private set; }
    public string? ConfigHash { get; private set; }

    /// <summary>Sepete eklendiği andaki fiyat kırılımı (WallpaperLinePrice JSON) - yalnızca gösterim;
    /// checkout fiyatı yeniden hesaplar.</summary>
    public string? PriceSnapshotJson { get; private set; }

    public bool IsConfigured => ConfigHash is not null;

    public void SetQuantity(int quantity) => Quantity = quantity;
    public void SetUnitPrice(decimal unitPriceTry) => UnitPriceTry = unitPriceTry;
    public void SetPriceSnapshot(string priceSnapshotJson) => PriceSnapshotJson = priceSnapshotJson;

    public void SetConfiguration(string configurationJson, string configHash, string priceSnapshotJson)
    {
        ConfigurationJson = configurationJson;
        ConfigHash = configHash;
        PriceSnapshotJson = priceSnapshotJson;
    }
}
