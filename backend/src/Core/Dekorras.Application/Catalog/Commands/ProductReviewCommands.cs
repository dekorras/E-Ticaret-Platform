using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SubmitProductReviewCommand(Guid ProductId, string IdentityUserId, int Rating, string Comment) : IRequest<Guid>;

public sealed class SubmitProductReviewCommandValidator : AbstractValidator<SubmitProductReviewCommand>
{
    public SubmitProductReviewCommandValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
    }
}

public sealed class SubmitProductReviewCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SubmitProductReviewCommand, Guid>
{
    public async Task<Guid> Handle(SubmitProductReviewCommand request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId)
            ?? throw new InvalidOperationException("Değerlendirme yapabilmek için önce müşteri profilinizin oluşturulmuş olması gerekir.");

        // Aynı müşteri aynı ürünü birden fazla kez değerlendiremez - mevcut değerlendirmesi varsa
        // güncellemek yerine reddediyoruz (ayrı bir "düzenle" akışı bu turun kapsamı dışında).
        var existingReview = unitOfWork.Repository<ProductReview>().Query()
            .FirstOrDefault(r => r.ProductId == request.ProductId && r.CustomerId == customer.Id);
        if (existingReview is not null)
            throw new InvalidOperationException("Bu ürünü zaten değerlendirdiniz.");

        var review = new ProductReview(request.ProductId, customer.Id, request.Rating, request.Comment);
        await unitOfWork.Repository<ProductReview>().AddAsync(review, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return review.Id;
    }
}

public sealed record ApproveProductReviewCommand(Guid ReviewId) : IRequest<Unit>;

public sealed class ApproveProductReviewCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ApproveProductReviewCommand, Unit>
{
    public async Task<Unit> Handle(ApproveProductReviewCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ProductReview>();
        var review = await repository.GetByIdAsync(request.ReviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ReviewId}' numaralı değerlendirme bulunamadı.");

        // Update(review) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        review.Approve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RejectProductReviewCommand(Guid ReviewId) : IRequest<Unit>;

/// <summary>`ProductReview.IsApproved` yalnızca iki durumludur (true/false) - `Reject()` çağrısı
/// bekleyen bir değerlendirmeyle (henüz hiç işlenmemiş, IsApproved=false) aynı duruma düşürür, bu
/// yüzden moderasyon kuyruğu reddedilmiş bir kaydı SONSUZA KADAR "bekliyor" olarak göstermeye devam
/// eder. Domain'e üçüncü bir durum (Rejected) eklemek yerine (bu turun kapsamını genişletir), reddetme
/// burada kaydı KALICI OLARAK SİLER - moderasyon reddi = kaydı at, aynı e-ticaret sitelerindeki
/// "spam/uygunsuz yorumu sil" davranışıyla tutarlı.</summary>
public sealed class RejectProductReviewCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RejectProductReviewCommand, Unit>
{
    public async Task<Unit> Handle(RejectProductReviewCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ProductReview>();
        var review = await repository.GetByIdAsync(request.ReviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ReviewId}' numaralı değerlendirme bulunamadı.");

        repository.Remove(review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
