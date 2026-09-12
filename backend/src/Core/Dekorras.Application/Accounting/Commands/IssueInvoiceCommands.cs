using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

/// <summary>
/// Gerçek e-Fatura/e-Arşiv kesimi GİB sertifikalı bir "özel entegratör" (BizimHesap/Nilvera/
/// Uyumsoft/Foriba/İzibiz - bkz. plan §4.1 madde 8) gerektirir; bu proje o entegrasyonu sıfırdan
/// KURMAZ, yalnızca Provider Registry üzerinden BAĞLANIR (henüz gerçek bir API çağrısı yok, hepsi
/// stub). Bu komutlar, admin'in resmi kesimi (BizimHesap/Nilvera ekranından) yaptıktan SONRA
/// sonucu (resmi fatura no + hangi sağlayıcı) burada elle kaydetmesini sağlar - EInvoiceLog'a
/// denetim izi olarak yazılır (Faz 0/1'den beri domain'de vardı, hiç kullanılmıyordu).
/// </summary>
public sealed record IssueInvoiceAsEInvoiceCommand(Guid InvoiceId, Guid IntegrationProviderId, string OfficialInvoiceNumber) : IRequest<Unit>;

public sealed class IssueInvoiceAsEInvoiceCommandValidator : AbstractValidator<IssueInvoiceAsEInvoiceCommand>
{
    public IssueInvoiceAsEInvoiceCommandValidator() => RuleFor(x => x.OfficialInvoiceNumber).NotEmpty().MaximumLength(100);
}

public sealed class IssueInvoiceAsEInvoiceCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<IssueInvoiceAsEInvoiceCommand, Unit>
{
    public async Task<Unit> Handle(IssueInvoiceAsEInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await unitOfWork.Repository<Invoice>().GetByIdAsync(request.InvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.InvoiceId}' numaralı fatura bulunamadı.");

        invoice.IssueAsEInvoice(request.OfficialInvoiceNumber);

        var log = new EInvoiceLog(invoice.Id, request.IntegrationProviderId, success: true, request.OfficialInvoiceNumber, "Elle kaydedildi.");
        await unitOfWork.Repository<EInvoiceLog>().AddAsync(log, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record IssueInvoiceAsEArchiveCommand(Guid InvoiceId, Guid IntegrationProviderId, string OfficialInvoiceNumber) : IRequest<Unit>;

public sealed class IssueInvoiceAsEArchiveCommandValidator : AbstractValidator<IssueInvoiceAsEArchiveCommand>
{
    public IssueInvoiceAsEArchiveCommandValidator() => RuleFor(x => x.OfficialInvoiceNumber).NotEmpty().MaximumLength(100);
}

public sealed class IssueInvoiceAsEArchiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<IssueInvoiceAsEArchiveCommand, Unit>
{
    public async Task<Unit> Handle(IssueInvoiceAsEArchiveCommand request, CancellationToken cancellationToken)
    {
        var invoice = await unitOfWork.Repository<Invoice>().GetByIdAsync(request.InvoiceId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.InvoiceId}' numaralı fatura bulunamadı.");

        invoice.IssueAsEArchive(request.OfficialInvoiceNumber);

        var log = new EInvoiceLog(invoice.Id, request.IntegrationProviderId, success: true, request.OfficialInvoiceNumber, "Elle kaydedildi.");
        await unitOfWork.Repository<EInvoiceLog>().AddAsync(log, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
