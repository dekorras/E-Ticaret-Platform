using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record ApplyGiftVoucherResult(bool Success, string? Message);

public sealed record ApplyGiftVoucherCommand(string SessionKey, string GiftVoucherCode) : IRequest<ApplyGiftVoucherResult>;

public sealed class ApplyGiftVoucherCommandValidator : AbstractValidator<ApplyGiftVoucherCommand>
{
    public ApplyGiftVoucherCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.GiftVoucherCode).NotEmpty();
    }
}

public sealed class ApplyGiftVoucherCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ApplyGiftVoucherCommand, ApplyGiftVoucherResult>
{
    public async Task<ApplyGiftVoucherResult> Handle(ApplyGiftVoucherCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = request.GiftVoucherCode.Trim().ToUpperInvariant();

        var voucher = unitOfWork.Repository<GiftVoucher>().Query().FirstOrDefault(v => v.Code == normalizedCode);
        if (voucher is null)
            return new ApplyGiftVoucherResult(false, "Bu hediye çeki kodu geçersiz.");

        if (!voucher.IsUsable())
            return new ApplyGiftVoucherResult(false, "Bu hediye çeki pasif veya bakiyesi tükenmiş.");

        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null)
            return new ApplyGiftVoucherResult(false, "Sepet bulunamadı.");

        cart.ApplyGiftVoucher(normalizedCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApplyGiftVoucherResult(true, "Hediye çeki uygulandı.");
    }
}
