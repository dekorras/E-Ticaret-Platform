using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateQuoteCommand(Guid LedgerAccountId, DateTime ValidUntilUtc) : IRequest<Guid>;

public sealed class CreateQuoteCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateQuoteCommand, Guid>
{
    public async Task<Guid> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        _ = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(request.LedgerAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.LedgerAccountId}' numaralı cari hesap bulunamadı.");

        var quoteNumber = $"TEKLIF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        var quote = new Quote(quoteNumber, request.LedgerAccountId, request.ValidUntilUtc);

        await unitOfWork.Repository<Quote>().AddAsync(quote, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return quote.Id;
    }
}

public sealed record AddQuoteLineCommand(Guid QuoteId, string Description, decimal UnitPriceTry, int Quantity) : IRequest<Unit>;

public sealed class AddQuoteLineCommandValidator : AbstractValidator<AddQuoteLineCommand>
{
    public AddQuoteLineCommandValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.UnitPriceTry).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class AddQuoteLineCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddQuoteLineCommand, Unit>
{
    public async Task<Unit> Handle(AddQuoteLineCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Quote>();
        var quote = await repository.GetByIdAsync(request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.QuoteId}' numaralı teklif bulunamadı.");

        // Yeni bir kalem eklendiği için önce Lines yüklenir - bkz. IRepository<T>.LoadCollectionAsync
        // dokümantasyonu (aksi halde ikinci kalem eklendiğinde DbUpdateConcurrencyException fırlatır).
        await repository.LoadCollectionAsync(quote, q => q.Lines, cancellationToken);

        quote.AddLine(request.Description, request.UnitPriceTry, request.Quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
