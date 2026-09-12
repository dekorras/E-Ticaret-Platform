namespace Dekorras.Domain.Ordering;

public enum OrderStatus
{
    PendingApproval,       // Onay Bekliyor
    Preparing,             // Hazırlanıyor
    Prepared,              // Hazırlandı
    Shipped,               // Kargoya Verildi
    Completed,             // Tamamlandı
    Cancelled,             // İptal Edildi
    CancellationReverted,  // İptal Geri Alındı
    Refunded,              // İade Edildi
    Rejected,              // Reddedildi
    Expired,               // Süresi Doldu
    Chargeback,            // Ters İbraz
    Failed,                // Başarısız
    OnHold,                // Durduruldu
    Voided                 // Hükümsüz
}

public enum OrderSource
{
    Web,
    Mobile,
    Trendyol,
    Hepsiburada,
    N11,
    Idefix,
    Amazon,

    /// <summary>Admin panelinde onaylı bir B2B teklifin (bkz. `Quote.ConvertToOrder`) siparişe
    /// dönüştürülmesiyle oluşur - normal Storefront checkout'undan (Web) BİLİNÇLİ OLARAK ayrı
    /// bir kaynak, raporlama/filtreleme "bu sipariş nereden geldi" sorusunu doğru yanıtlayabilsin diye.</summary>
    AdminQuote
}
