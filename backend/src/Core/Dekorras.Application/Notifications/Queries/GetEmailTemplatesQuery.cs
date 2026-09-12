using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Notifications;
using MediatR;

namespace Dekorras.Application.Notifications.Queries;

public sealed record EmailTemplateListItemDto(Guid Id, string Key, string LanguageCode, string Subject);

public sealed record GetEmailTemplatesQuery : IRequest<IReadOnlyCollection<EmailTemplateListItemDto>>;

public sealed class GetEmailTemplatesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetEmailTemplatesQuery, IReadOnlyCollection<EmailTemplateListItemDto>>
{
    public Task<IReadOnlyCollection<EmailTemplateListItemDto>> Handle(GetEmailTemplatesQuery request, CancellationToken cancellationToken)
    {
        var templates = unitOfWork.Repository<EmailTemplate>().Query()
            .OrderBy(t => t.Key).ThenBy(t => t.LanguageCode)
            .Select(t => new EmailTemplateListItemDto(t.Id, t.Key, t.LanguageCode, t.Subject))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<EmailTemplateListItemDto>>(templates);
    }
}

public sealed record EmailTemplateDetailDto(Guid Id, string Key, string LanguageCode, string Subject, string BodyHtml);

public sealed record GetEmailTemplateByIdQuery(Guid Id) : IRequest<EmailTemplateDetailDto?>;

public sealed class GetEmailTemplateByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetEmailTemplateByIdQuery, EmailTemplateDetailDto?>
{
    public async Task<EmailTemplateDetailDto?> Handle(GetEmailTemplateByIdQuery request, CancellationToken cancellationToken)
    {
        var template = await unitOfWork.Repository<EmailTemplate>().GetByIdAsync(request.Id, cancellationToken);
        return template is null ? null : new EmailTemplateDetailDto(template.Id, template.Key, template.LanguageCode, template.Subject, template.BodyHtml);
    }
}

public sealed record NotificationLogDto(NotificationChannel Channel, string Recipient, string TemplateKey, bool Success, DateTime SentAtUtc);

/// <summary>Admin denetim ekranı - en son gönderilen (veya gönderilmeye ÇALIŞILAN) bildirimler.</summary>
public sealed record GetRecentNotificationLogsQuery : IRequest<IReadOnlyCollection<NotificationLogDto>>;

public sealed class GetRecentNotificationLogsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetRecentNotificationLogsQuery, IReadOnlyCollection<NotificationLogDto>>
{
    public Task<IReadOnlyCollection<NotificationLogDto>> Handle(GetRecentNotificationLogsQuery request, CancellationToken cancellationToken)
    {
        var logs = unitOfWork.Repository<NotificationLog>().Query()
            .OrderByDescending(l => l.SentAtUtc)
            .Take(20)
            .Select(l => new NotificationLogDto(l.Channel, l.Recipient, l.TemplateKey, l.Success, l.SentAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<NotificationLogDto>>(logs);
    }
}
