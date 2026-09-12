using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record RecordLedgerTransactionCommand(Guid LedgerAccountId, LedgerTransactionDirection Direction, decimal AmountTry, string Description) : IRequest<Unit>;

public sealed class RecordLedgerTransactionCommandValidator : AbstractValidator<RecordLedgerTransactionCommand>
{
    public RecordLedgerTransactionCommandValidator()
    {
        RuleFor(x => x.AmountTry).GreaterThan(0);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
    }
}

public sealed class RecordLedgerTransactionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RecordLedgerTransactionCommand, Unit>
{
    public async Task<Unit> Handle(RecordLedgerTransactionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<LedgerAccount>();
        var account = await repository.GetByIdAsync(request.LedgerAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.LedgerAccountId}' numaralı cari hesap bulunamadı.");

        // Yeni bir işlem satırı eklendiği için önce Transactions yüklenir - bkz.
        // IRepository<T>.LoadCollectionAsync dokümantasyonu (aksi halde ikinci hareket kaydında
        // EF Core bunu "Modified" sanıp DbUpdateConcurrencyException fırlatır).
        await repository.LoadCollectionAsync(account, a => a.Transactions, cancellationToken);

        account.RecordTransaction(request.Direction, request.AmountTry, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
