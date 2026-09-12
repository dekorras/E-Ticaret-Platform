using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateCheckCommand(Guid LedgerAccountId, string CheckNumber, decimal AmountTry, DateTime DueDateUtc) : IRequest<Guid>;

public sealed class CreateCheckCommandValidator : AbstractValidator<CreateCheckCommand>
{
    public CreateCheckCommandValidator()
    {
        RuleFor(x => x.CheckNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AmountTry).GreaterThan(0);
    }
}

public sealed class CreateCheckCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCheckCommand, Guid>
{
    public async Task<Guid> Handle(CreateCheckCommand request, CancellationToken cancellationToken)
    {
        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(request.LedgerAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.LedgerAccountId}' numaralı cari hesap bulunamadı.");

        var check = new Check(request.LedgerAccountId, request.CheckNumber, request.AmountTry, request.DueDateUtc);
        await unitOfWork.Repository<Check>().AddAsync(check, cancellationToken);

        ledgerAccount.AdjustCheckNoteBalance(request.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return check.Id;
    }
}

public sealed record MarkCheckCollectedCommand(Guid CheckId) : IRequest<Unit>;

public sealed class MarkCheckCollectedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MarkCheckCollectedCommand, Unit>
{
    public async Task<Unit> Handle(MarkCheckCollectedCommand request, CancellationToken cancellationToken)
    {
        var check = await unitOfWork.Repository<Check>().GetByIdAsync(request.CheckId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CheckId}' numaralı çek bulunamadı.");
        if (check.Status != PaperInstrumentStatus.Pending)
            throw new InvalidOperationException("Bu çek zaten sonuçlandırılmış.");

        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(check.LedgerAccountId, cancellationToken);

        check.MarkCollected();
        ledgerAccount?.AdjustCheckNoteBalance(-check.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record MarkCheckBouncedCommand(Guid CheckId) : IRequest<Unit>;

public sealed class MarkCheckBouncedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MarkCheckBouncedCommand, Unit>
{
    public async Task<Unit> Handle(MarkCheckBouncedCommand request, CancellationToken cancellationToken)
    {
        var check = await unitOfWork.Repository<Check>().GetByIdAsync(request.CheckId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CheckId}' numaralı çek bulunamadı.");
        if (check.Status != PaperInstrumentStatus.Pending)
            throw new InvalidOperationException("Bu çek zaten sonuçlandırılmış.");

        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(check.LedgerAccountId, cancellationToken);

        check.MarkBounced();
        ledgerAccount?.AdjustCheckNoteBalance(-check.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
