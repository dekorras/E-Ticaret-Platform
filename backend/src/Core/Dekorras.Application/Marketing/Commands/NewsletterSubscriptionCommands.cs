using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Notifications;
using MediatR;

namespace Dekorras.Application.Marketing.Commands;

/// <summary>`Domain.Marketing.NewsletterSubscriber` Faz 0/1'den beri vardı ama Application
/// katmanının HİÇBİR yerinden çağrılmıyordu - Storefront'ta misafirler için (üyelik gerektirmeyen)
/// bir bülten kaydı formu hiç yoktu (yalnızca giriş yapmış müşteriler için `Customer.
/// NewsletterSubscribed` bayrağı vardı, bkz. `SetNewsletterSubscriptionCommand`). `IsConfirmed`
/// alanının varlığı çift-onaylı (double opt-in) bir akış tasarlandığını gösteriyordu - bu turda
/// tamamlandı.</summary>
public sealed record SubscribeToNewsletterCommand(string Email, string ConfirmUrlTemplate) : IRequest<Unit>;

public sealed class SubscribeToNewsletterCommandHandler(IUnitOfWork unitOfWork, IEmailSender emailSender)
    : IRequestHandler<SubscribeToNewsletterCommand, Unit>
{
    public async Task<Unit> Handle(SubscribeToNewsletterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var repository = unitOfWork.Repository<NewsletterSubscriber>();
        var existing = repository.Query().FirstOrDefault(s => s.Email == email);

        Guid subscriberId;
        if (existing is not null)
        {
            // Zaten onaylı bir abone tekrar kayıt olmaya çalışırsa sessizce hiçbir şey yapılmaz -
            // hem gereksiz e-posta gönderimini önler hem de "bu e-posta zaten bizde kayıtlı mı"
            // sorgusuyla e-posta numaralandırmasına (enumeration) kapı aralamaz.
            if (existing.IsConfirmed) return Unit.Value;
            subscriberId = existing.Id;
        }
        else
        {
            var subscriber = new NewsletterSubscriber(email);
            await repository.AddAsync(subscriber, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            subscriberId = subscriber.Id;
        }

        await SendConfirmationEmailAsync(email, subscriberId, request.ConfirmUrlTemplate, cancellationToken);
        return Unit.Value;
    }

    /// <summary>Bkz. PlaceOrderCommand.SendOrderConfirmationEmailAsync'teki AYNI desen - e-posta
    /// gönderimindeki bir hata ana akışı BOZMAMALI, tamamen izole edilmiştir.</summary>
    private async Task SendConfirmationEmailAsync(string toEmail, Guid subscriberId, string confirmUrlTemplate, CancellationToken cancellationToken)
    {
        const string templateKey = "NewsletterConfirmation";
        var success = false;

        try
        {
            var template = unitOfWork.Repository<EmailTemplate>().Query()
                .FirstOrDefault(t => t.Key == templateKey && t.LanguageCode == "tr");

            var confirmUrl = string.Format(confirmUrlTemplate, subscriberId);
            var subject = template?.Subject ?? "Bülten Aboneliğinizi Onaylayın";
            var bodyHtml = (template?.BodyHtml ?? "<p>Bülten aboneliğinizi onaylamak için <a href=\"{{ConfirmUrl}}\">buraya tıklayın</a>.</p>")
                .Replace("{{ConfirmUrl}}", confirmUrl);

            await emailSender.SendAsync(toEmail, subject, bodyHtml, cancellationToken);
            success = true;
        }
        catch
        {
            success = false;
        }

        await unitOfWork.Repository<NotificationLog>().AddAsync(
            new NotificationLog(NotificationChannel.Email, toEmail, templateKey, success), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ConfirmNewsletterSubscriptionCommand(Guid SubscriberId) : IRequest<bool>;

public sealed class ConfirmNewsletterSubscriptionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ConfirmNewsletterSubscriptionCommand, bool>
{
    public async Task<bool> Handle(ConfirmNewsletterSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscriber = await unitOfWork.Repository<NewsletterSubscriber>().GetByIdAsync(request.SubscriberId, cancellationToken);
        if (subscriber is null) return false;

        subscriber.Confirm();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
