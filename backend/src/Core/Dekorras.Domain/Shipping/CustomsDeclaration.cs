using Dekorras.Domain.Common;

namespace Dekorras.Domain.Shipping;

/// <summary>Türkiye dışına (uluslararası) gönderilen bir siparişin gümrük beyanı - plan §7'nin
/// "Gümrük ve fatura gereksinimleri: Ürünlerde HS/GTİP kodu alanı" maddesiyle ilişkilidir.
/// `PlaceOrderCommand` checkout anında sipariş kalemlerinin `Product.HsCode`'larından otomatik bir
/// taslak oluşturur; Admin gerekirse (ör. bir üründe HS kodu hiç girilmemişse) `UpdateDeclaration`
/// ile düzeltebilir. Bilinçli olarak kapsam dışı: proforma fatura üretimi ve yurt dışı satışlarda
/// %0 KDV istisnası hesaplama mantığı - bunlar ayrı, daha büyük bir tasarım kararı gerektiriyor.</summary>
public class CustomsDeclaration : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public string HsCodeSummary { get; private set; } = default!;
    public decimal DeclaredValueTry { get; private set; }
    public string ContentDescription { get; private set; } = default!;

    private CustomsDeclaration() { }

    public CustomsDeclaration(Guid orderId, string hsCodeSummary, decimal declaredValueTry, string contentDescription)
    {
        OrderId = orderId;
        HsCodeSummary = hsCodeSummary;
        DeclaredValueTry = declaredValueTry;
        ContentDescription = contentDescription;
    }

    public void UpdateDeclaration(string hsCodeSummary, decimal declaredValueTry, string contentDescription)
    {
        HsCodeSummary = hsCodeSummary;
        DeclaredValueTry = declaredValueTry;
        ContentDescription = contentDescription;
    }
}
