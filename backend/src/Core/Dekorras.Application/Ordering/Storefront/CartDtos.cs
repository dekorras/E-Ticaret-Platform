namespace Dekorras.Application.Ordering.Storefront;

/// <param name="Configuration">Ölçüye özel duvar kağıdı satırında konfigürasyon özeti; düz ürünlerde null.</param>
/// <param name="RequiresConfiguration">Konfigüratör öncesi eklenmiş ÖLÇÜSÜZ duvar kağıdı/poster satırı (eski sabit fiyatlı):
/// toplamlara katılmaz, siparişe dönüştürülemez; müşteri ölçü seçmeli ya da kaldırmalı.</param>
public sealed record CartItemDto(
    Guid ProductId,
    Guid? VariantId,
    string Name,
    string? VariantOptionName,
    string Slug,
    decimal UnitPriceTry,
    int Quantity,
    decimal LineTotalTry,
    Guid CartItemId = default,
    CartItemConfigurationDto? Configuration = null,
    bool RequiresConfiguration = false);

public sealed record CartItemConfigurationDto(
    decimal WidthCm,
    decimal HeightCm,
    string Unit,
    string MaterialCode,
    string MaterialName,
    string Fit,
    bool Mirror,
    string Filter,
    string? Crop,
    decimal BilledAreaM2,
    int PanelCount,
    string ConfigurationJson);

public sealed record CartDto(
    Guid CartId,
    IReadOnlyCollection<CartItemDto> Items,
    decimal SubTotalTry,
    string? CouponCode,
    decimal DiscountTry,
    decimal GrandTotalTry,
    string? CampaignName = null,
    string? GiftVoucherCode = null,
    decimal GiftVoucherAmountAppliedTry = 0m,
    decimal GlueDiscountTry = 0m,
    decimal? FreeShippingRemainingTry = null,
    bool FreeShipping = false,
    IReadOnlyList<GlueSuggestionDto>? GlueSuggestions = null);

/// <summary>Sepette tutkal gerektiren duvar kağıdı var ama tutkal ürünü yok - "Tutkal ekle" önerisi.</summary>
public sealed record GlueSuggestionDto(Guid GlueProductId, string Name, string Slug, decimal UnitPriceTry, int SuggestedQuantity, bool WillBeFree);
