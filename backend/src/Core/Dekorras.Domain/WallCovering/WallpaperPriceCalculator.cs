namespace Dekorras.Domain.WallCovering;

/// <summary>Satır fiyatı için gereken malzeme bilgileri (ürün bazlı fiyat istisnası uygulanmış halde).</summary>
public sealed record MaterialPricing(string Code, string Name, decimal PricePerM2, decimal PanelWidthCm, decimal BleedCm, decimal MinBillableAreaM2)
{
    public static MaterialPricing From(Material material, decimal? overridePricePerM2 = null) =>
        new(material.Code, material.Name, overridePricePerM2 ?? material.PricePerM2, material.PanelWidthCm, material.BleedCm, material.MinBillableAreaM2);
}

/// <summary>Bir konfigüre satırın fiyat kırılımı. Siparişte OrderItem'a JSON olarak dondurulur.</summary>
public sealed record WallpaperLinePrice(
    string MaterialCode,
    string MaterialName,
    decimal WidthCm,
    decimal HeightCm,
    decimal ProductionWidthCm,
    decimal ProductionHeightCm,
    decimal BleedCm,
    bool BleedCharged,
    decimal AreaM2,
    decimal BilledAreaM2,
    int PanelCount,
    decimal PanelWidthCm,
    decimal UnitPricePerM2,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

/// <summary>Spec 1.4 fiyat formülü - TEK doğruluk kaynağı. İstemci aynı formülü yalnızca anlık
/// gösterim için kullanır; sepete eklerken ve checkout'ta fiyat daima burada yeniden hesaplanır.
/// <code>
/// uretimEn    = en + bleed
/// uretimBoy   = boy + bleed
/// panelSayisi = ceil(uretimEn / panelEni)
/// alanM2      = en * boy / 10 000
/// faturaM2    = max(uretimEn * uretimBoy / 10 000, minFaturaM2)   (ChargeBleed=false ise pay hariç alan)
/// satirTutari = round(faturaM2 * ₺/m², 2) * adet
/// </code></summary>
public static class WallpaperPriceCalculator
{
    public static WallpaperLinePrice Calculate(decimal widthCm, decimal heightCm, int quantity, MaterialPricing material, bool chargeBleed = true)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), "Adet en az 1 olmalıdır.");

        widthCm = Math.Round(widthCm, 1, MidpointRounding.AwayFromZero);
        heightCm = Math.Round(heightCm, 1, MidpointRounding.AwayFromZero);

        var productionWidth = widthCm + material.BleedCm;
        var productionHeight = heightCm + material.BleedCm;

        // Panel sayısı her durumda üretim enine göre - pay fiyata yansıtılmasa bile fiilen basılır.
        var panelCount = (int)Math.Ceiling(productionWidth / material.PanelWidthCm);

        var areaM2 = widthCm * heightCm / 10_000m;
        var rawBilled = chargeBleed ? productionWidth * productionHeight / 10_000m : areaM2;
        var billedM2 = Math.Max(rawBilled, material.MinBillableAreaM2);

        var unitPrice = Math.Round(billedM2 * material.PricePerM2, 2, MidpointRounding.AwayFromZero);

        return new WallpaperLinePrice(
            material.Code,
            material.Name,
            widthCm,
            heightCm,
            productionWidth,
            productionHeight,
            material.BleedCm,
            chargeBleed,
            Math.Round(areaM2, 4, MidpointRounding.AwayFromZero),
            Math.Round(billedM2, 4, MidpointRounding.AwayFromZero),
            panelCount,
            material.PanelWidthCm,
            material.PricePerM2,
            unitPrice,
            quantity,
            unitPrice * quantity);
    }
}
