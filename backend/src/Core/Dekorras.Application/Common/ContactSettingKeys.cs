namespace Dekorras.Application.Common;

/// <summary>Bkz. plan §2.1 - "İletişim kanalları: sabit telefon hattı, WhatsApp Business linki,
/// Instagram, Telegram — footer/header üzerinden doğrudan erişim." `Domain.SystemAdmin.Setting`
/// (Faz 0/1'den beri vardı, hiçbir tanımlı kullanım senaryosu olmadığı için bilinçli olarak orphaned
/// bırakılmıştı - bkz. devamı 22/43/45) bu tanımlı senaryoyla İLK kez gerçek bir amaca kavuşuyor.</summary>
public static class ContactSettingKeys
{
    public const string PhoneNumber = "Contact.PhoneNumber";
    public const string WhatsAppUrl = "Contact.WhatsAppUrl";
    public const string InstagramUrl = "Contact.InstagramUrl";
    public const string TelegramUrl = "Contact.TelegramUrl";

    public static readonly IReadOnlyList<string> All = [PhoneNumber, WhatsAppUrl, InstagramUrl, TelegramUrl];
}
