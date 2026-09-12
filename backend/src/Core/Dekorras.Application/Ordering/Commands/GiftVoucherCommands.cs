using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

public sealed record CreateGiftVoucherCommand(string Code, decimal AmountTry) : IRequest<Guid>;

public sealed class CreateGiftVoucherCommandValidator : AbstractValidator<CreateGiftVoucherCommand>
{
    public CreateGiftVoucherCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.AmountTry).GreaterThan(0);
    }
}

public sealed class CreateGiftVoucherCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateGiftVoucherCommand, Guid>
{
    public async Task<Guid> Handle(CreateGiftVoucherCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        var exists = unitOfWork.Repository<GiftVoucher>().Query().Any(v => v.Code == normalizedCode);
        if (exists)
            throw new InvalidOperationException($"'{normalizedCode}' kodlu bir hediye çeki zaten var.");

        var voucher = new GiftVoucher(normalizedCode, request.AmountTry);
        await unitOfWork.Repository<GiftVoucher>().AddAsync(voucher, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return voucher.Id;
    }
}

public sealed record SetGiftVoucherActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetGiftVoucherActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetGiftVoucherActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetGiftVoucherActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<GiftVoucher>();
        var voucher = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı hediye çeki bulunamadı.");

        // Update(voucher) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) voucher.Activate(); else voucher.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
