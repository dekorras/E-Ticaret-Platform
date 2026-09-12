using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record SetLedgerAccountActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetLedgerAccountActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetLedgerAccountActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetLedgerAccountActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<LedgerAccount>();
        var account = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı cari hesap bulunamadı.");

        // Update(account) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) account.Activate(); else account.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
