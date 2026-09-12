using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreatePromissoryNoteCommand(Guid LedgerAccountId, string NoteNumber, decimal AmountTry, DateTime DueDateUtc) : IRequest<Guid>;

public sealed class CreatePromissoryNoteCommandValidator : AbstractValidator<CreatePromissoryNoteCommand>
{
    public CreatePromissoryNoteCommandValidator()
    {
        RuleFor(x => x.NoteNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AmountTry).GreaterThan(0);
    }
}

public sealed class CreatePromissoryNoteCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreatePromissoryNoteCommand, Guid>
{
    public async Task<Guid> Handle(CreatePromissoryNoteCommand request, CancellationToken cancellationToken)
    {
        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(request.LedgerAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.LedgerAccountId}' numaralı cari hesap bulunamadı.");

        var note = new PromissoryNote(request.LedgerAccountId, request.NoteNumber, request.AmountTry, request.DueDateUtc);
        await unitOfWork.Repository<PromissoryNote>().AddAsync(note, cancellationToken);

        ledgerAccount.AdjustCheckNoteBalance(request.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return note.Id;
    }
}

public sealed record MarkPromissoryNoteCollectedCommand(Guid PromissoryNoteId) : IRequest<Unit>;

public sealed class MarkPromissoryNoteCollectedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MarkPromissoryNoteCollectedCommand, Unit>
{
    public async Task<Unit> Handle(MarkPromissoryNoteCollectedCommand request, CancellationToken cancellationToken)
    {
        var note = await unitOfWork.Repository<PromissoryNote>().GetByIdAsync(request.PromissoryNoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.PromissoryNoteId}' numaralı senet bulunamadı.");
        if (note.Status != PaperInstrumentStatus.Pending)
            throw new InvalidOperationException("Bu senet zaten sonuçlandırılmış.");

        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(note.LedgerAccountId, cancellationToken);

        note.MarkCollected();
        ledgerAccount?.AdjustCheckNoteBalance(-note.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record MarkPromissoryNoteBouncedCommand(Guid PromissoryNoteId) : IRequest<Unit>;

public sealed class MarkPromissoryNoteBouncedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MarkPromissoryNoteBouncedCommand, Unit>
{
    public async Task<Unit> Handle(MarkPromissoryNoteBouncedCommand request, CancellationToken cancellationToken)
    {
        var note = await unitOfWork.Repository<PromissoryNote>().GetByIdAsync(request.PromissoryNoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.PromissoryNoteId}' numaralı senet bulunamadı.");
        if (note.Status != PaperInstrumentStatus.Pending)
            throw new InvalidOperationException("Bu senet zaten sonuçlandırılmış.");

        var ledgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(note.LedgerAccountId, cancellationToken);

        note.MarkBounced();
        ledgerAccount?.AdjustCheckNoteBalance(-note.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
