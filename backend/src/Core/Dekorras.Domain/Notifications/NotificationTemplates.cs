using Dekorras.Domain.Common;

namespace Dekorras.Domain.Notifications;

public class EmailTemplate : AuditableEntity
{
    public string Key { get; private set; } = default!; // ör. "OrderCompleted", "PasswordReset"
    public string LanguageCode { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string BodyHtml { get; private set; } = default!;

    private EmailTemplate() { }

    public EmailTemplate(string key, string languageCode, string subject, string bodyHtml)
    {
        Key = key;
        LanguageCode = languageCode;
        Subject = subject;
        BodyHtml = bodyHtml;
    }

    public void Update(string subject, string bodyHtml)
    {
        Subject = subject;
        BodyHtml = bodyHtml;
    }
}

public enum NotificationChannel { Email, Sms, Push }

public class NotificationLog : BaseEntity
{
    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; } = default!;
    public string TemplateKey { get; private set; } = default!;
    public bool Success { get; private set; }
    public DateTime SentAtUtc { get; private set; } = DateTime.UtcNow;

    private NotificationLog() { }

    public NotificationLog(NotificationChannel channel, string recipient, string templateKey, bool success)
    {
        Channel = channel;
        Recipient = recipient;
        TemplateKey = templateKey;
        Success = success;
    }
}
