using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Notifications;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Notifications.Commands;

/// <summary>Admin'in düzenleyebileceği e-posta şablonları (ör. "OrderConfirmation") - konu/içerikte
/// <c>{{OrderNumber}}</c>, <c>{{CustomerName}}</c>, <c>{{GrandTotal}}</c> gibi yer tutucular
/// kullanılabilir (bkz. PlaceOrderCommand'daki değiştirme mantığı).</summary>
public sealed record CreateEmailTemplateCommand(string Key, string LanguageCode, string Subject, string BodyHtml) : IRequest<Guid>;

public sealed class CreateEmailTemplateCommandValidator : AbstractValidator<CreateEmailTemplateCommand>
{
    public CreateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BodyHtml).NotEmpty();
    }
}

public sealed class CreateEmailTemplateCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateEmailTemplateCommand, Guid>
{
    public async Task<Guid> Handle(CreateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var exists = unitOfWork.Repository<EmailTemplate>().Query()
            .Any(t => t.Key == request.Key && t.LanguageCode == request.LanguageCode);
        if (exists)
            throw new InvalidOperationException($"'{request.Key}' anahtarlı, '{request.LanguageCode}' dilinde bir şablon zaten var.");

        var template = new EmailTemplate(request.Key, request.LanguageCode, request.Subject, request.BodyHtml);
        await unitOfWork.Repository<EmailTemplate>().AddAsync(template, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return template.Id;
    }
}

public sealed record UpdateEmailTemplateCommand(Guid Id, string Subject, string BodyHtml) : IRequest<Unit>;

public sealed class UpdateEmailTemplateCommandValidator : AbstractValidator<UpdateEmailTemplateCommand>
{
    public UpdateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BodyHtml).NotEmpty();
    }
}

public sealed class UpdateEmailTemplateCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateEmailTemplateCommand, Unit>
{
    public async Task<Unit> Handle(UpdateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<EmailTemplate>();
        var template = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı şablon bulunamadı.");

        template.Update(request.Subject, request.BodyHtml);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
