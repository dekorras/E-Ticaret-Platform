using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductReviewDto(Guid Id, Guid ProductId, string CustomerName, int Rating, string Comment, bool IsApproved, DateTime CreatedAtUtc);

/// <summary>Storefront (yalnızca onaylanmış - Admin bilinçli olarak `OnlyApproved: false` geçer)
/// ürün detay sayfasında ve Admin moderasyon listesinde kullanılır.</summary>
public sealed record GetProductReviewsQuery(Guid ProductId, bool OnlyApproved) : IRequest<IReadOnlyCollection<ProductReviewDto>>;

public sealed class GetProductReviewsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductReviewsQuery, IReadOnlyCollection<ProductReviewDto>>
{
    public Task<IReadOnlyCollection<ProductReviewDto>> Handle(GetProductReviewsQuery request, CancellationToken cancellationToken)
    {
        var reviews = unitOfWork.Repository<ProductReview>().Query()
            .Where(r => r.ProductId == request.ProductId && (!request.OnlyApproved || r.IsApproved))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Join(unitOfWork.Repository<Customer>().Query(),
                r => r.CustomerId,
                c => c.Id,
                (r, c) => new ProductReviewDto(r.Id, r.ProductId, c.FullName, r.Rating, r.Comment, r.IsApproved, r.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductReviewDto>>(reviews);
    }
}

/// <summary>Admin moderasyon kuyruğu - TÜM ürünlerdeki onay bekleyen değerlendirmeler.</summary>
public sealed record GetPendingProductReviewsQuery : IRequest<IReadOnlyCollection<PendingProductReviewDto>>;

public sealed record PendingProductReviewDto(Guid Id, Guid ProductId, string ProductName, string CustomerName, int Rating, string Comment, DateTime CreatedAtUtc);

public sealed class GetPendingProductReviewsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPendingProductReviewsQuery, IReadOnlyCollection<PendingProductReviewDto>>
{
    public Task<IReadOnlyCollection<PendingProductReviewDto>> Handle(GetPendingProductReviewsQuery request, CancellationToken cancellationToken)
    {
        var pending = unitOfWork.Repository<ProductReview>().Query()
            .Where(r => !r.IsApproved)
            .OrderBy(r => r.CreatedAtUtc)
            .Join(unitOfWork.Repository<Customer>().Query(), r => r.CustomerId, c => c.Id, (r, c) => new { r, c })
            .Join(unitOfWork.Repository<Product>().Query(), x => x.r.ProductId, p => p.Id, (x, p) => new PendingProductReviewDto(
                x.r.Id, p.Id,
                p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                x.c.FullName, x.r.Rating, x.r.Comment, x.r.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<PendingProductReviewDto>>(pending);
    }
}
