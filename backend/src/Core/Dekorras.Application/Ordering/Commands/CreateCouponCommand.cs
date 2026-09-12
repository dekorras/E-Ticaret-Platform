using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

public sealed record CreateCouponCommand(
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTime ValidFromUtc,
    DateTime ValidToUtc,
    int? UsageLimit) : IRequest<Guid>;

public sealed class CreateCouponCommandValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.DiscountValue).LessThanOrEqualTo(100).When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage("Yüzde indirimi 100'den büyük olamaz.");
        RuleFor(x => x.ValidToUtc).GreaterThan(x => x.ValidFromUtc).WithMessage("Bitiş tarihi başlangıçtan sonra olmalıdır.");
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit is not null);
    }
}

public sealed class CreateCouponCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCouponCommand, Guid>
{
    public async Task<Guid> Handle(CreateCouponCommand request, CancellationToken cancellationToken)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        var exists = unitOfWork.Repository<Coupon>().Query().Any(c => c.Code == normalizedCode);
        if (exists)
            throw new InvalidOperationException($"'{normalizedCode}' kodlu bir kupon zaten var.");

        var coupon = new Coupon(normalizedCode, request.DiscountType, request.DiscountValue, request.ValidFromUtc, request.ValidToUtc, request.UsageLimit);
        await unitOfWork.Repository<Coupon>().AddAsync(coupon, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return coupon.Id;
    }
}
