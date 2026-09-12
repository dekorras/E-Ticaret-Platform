using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

public sealed record ApplyCouponResult(bool Success, string? Message);

public sealed record ApplyCouponCommand(string SessionKey, string CouponCode) : IRequest<ApplyCouponResult>;

public sealed class ApplyCouponCommandValidator : AbstractValidator<ApplyCouponCommand>
{
    public ApplyCouponCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.CouponCode).NotEmpty();
    }
}

public sealed class ApplyCouponCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ApplyCouponCommand, ApplyCouponResult>
{
    public async Task<ApplyCouponResult> Handle(ApplyCouponCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();

        var coupon = unitOfWork.Repository<Coupon>().Query().FirstOrDefault(c => c.Code == normalizedCode);
        if (coupon is null)
            return new ApplyCouponResult(false, "Bu kupon kodu geçersiz.");

        if (!coupon.IsValidNow(DateTime.UtcNow))
            return new ApplyCouponResult(false, "Bu kuponun süresi dolmuş veya kullanım limitine ulaşılmış.");

        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null)
            return new ApplyCouponResult(false, "Sepet bulunamadı.");

        cart.ApplyCoupon(normalizedCode);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApplyCouponResult(true, "Kupon uygulandı.");
    }
}
