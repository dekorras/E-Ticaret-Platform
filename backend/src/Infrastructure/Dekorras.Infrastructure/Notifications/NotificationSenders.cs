using Dekorras.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dekorras.Infrastructure.Notifications;

/// <summary>TODO: gerçek bir SMTP sağlayıcısı (SendGrid, Amazon SES, kurumsal SMTP) ile değiştirilecek.</summary>
public sealed class SmtpEmailSender(ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken)
    {
        logger.LogInformation("E-posta gönderildi (simülasyon): {ToEmail} - {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}

/// <summary>TODO: Firebase Cloud Messaging SDK ile değiştirilecek (bkz. plan §8, §16.C).</summary>
public sealed class FirebasePushNotificationSender(ILogger<FirebasePushNotificationSender> logger) : IPushNotificationSender
{
    public Task SendAsync(string deviceToken, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken cancellationToken)
    {
        logger.LogInformation("Push bildirimi gönderildi (simülasyon): {DeviceToken} - {Title}", deviceToken, title);
        return Task.CompletedTask;
    }
}
