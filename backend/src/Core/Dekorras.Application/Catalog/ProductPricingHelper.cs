using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;

namespace Dekorras.Application.Catalog;

/// <summary>Sepete ekleme/güncelleme ve checkout'un ÜÇÜNÜN de aynı fiyat önceliğini uygulaması için
/// tek durak noktası: (1) toplu alım kademesi (bkz. `QuantityDiscount`) - miktar bazlı en iyi fiyat,
/// kimden bağımsız uygulanır; (2) müşteri grubuna özel fiyat (bkz. `ProductGroupPrice`, plan §2.6 -
/// B2B/B2C farklı fiyatlandırma) - `Product.GetPriceFor` yerine BİLİNÇLİ OLARAK doğrudan repository
/// sorgusu kullanılır, aksi halde `GetByIdAsync` ile pasif gezinme yoluyla materyalize edilmiş bir
/// üründe `GroupPrices` koleksiyonu ÖNCEDEN `LoadCollectionAsync` ile yüklenmediği sürece sessizce
/// boş görünür (bkz. README "Önemli mimari not"); (3) taban fiyat.</summary>
public static class ProductPricingHelper
{
    public static decimal ResolveUnitPriceTry(IUnitOfWork unitOfWork, Guid productId, decimal basePriceTry, int quantity, Guid? customerGroupId)
    {
        var tierPrice = unitOfWork.Repository<QuantityDiscount>().Query()
            .Where(d => d.ProductId == productId && d.MinimumQuantity <= quantity)
            .OrderByDescending(d => d.MinimumQuantity)
            .Select(d => (decimal?)d.PriceTry)
            .FirstOrDefault();
        if (tierPrice is decimal tier) return tier;

        if (customerGroupId is Guid groupId)
        {
            var groupPrice = unitOfWork.Repository<ProductGroupPrice>().Query()
                .Where(g => g.ProductId == productId && g.CustomerGroupId == groupId)
                .Select(g => (decimal?)g.PriceTry)
                .FirstOrDefault();
            if (groupPrice is decimal group) return group;
        }

        return basePriceTry;
    }
}
