using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

public sealed record WishlistItemDto(Guid ProductId, string Name, string Slug, decimal PriceTry, string? ImageUrl, bool InStock);

public sealed record GetMyWishlistQuery(string IdentityUserId, string LanguageCode) : IRequest<IReadOnlyCollection<WishlistItemDto>>;

public sealed class GetMyWishlistQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyWishlistQuery, IReadOnlyCollection<WishlistItemDto>>
{
    public Task<IReadOnlyCollection<WishlistItemDto>> Handle(GetMyWishlistQuery request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null)
            return Task.FromResult<IReadOnlyCollection<WishlistItemDto>>([]);

        // customer.Wishlist gezinme özelliğine burada DOKUNULMAZ (Include olmadan boş dönerdi -
        // bkz. GetCategoryTreeQuery'de bulunan hata). Bunun yerine WishlistItem'lar CustomerId
        // (skaler) üzerinden ayrı bir IQueryable ile, Product'la JOIN edilerek okunur.
        var items = unitOfWork.Repository<WishlistItem>().Query()
            .Where(w => w.CustomerId == customer.Id)
            .Join(unitOfWork.Repository<Product>().Query(),
                w => w.ProductId,
                p => p.Id,
                (w, p) => new WishlistItemDto(
                    p.Id,
                    p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                    p.Slug,
                    p.BasePriceTry,
                    p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault(),
                    p.StockAvailability == StockAvailability.InStock))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<WishlistItemDto>>(items);
    }
}

public sealed record IsInWishlistQuery(string? IdentityUserId, Guid ProductId) : IRequest<bool>;

public sealed class IsInWishlistQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<IsInWishlistQuery, bool>
{
    public Task<bool> Handle(IsInWishlistQuery request, CancellationToken cancellationToken)
    {
        if (request.IdentityUserId is null) return Task.FromResult(false);

        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null) return Task.FromResult(false);

        var isInWishlist = unitOfWork.Repository<WishlistItem>().Query()
            .Any(w => w.CustomerId == customer.Id && w.ProductId == request.ProductId);

        return Task.FromResult(isInWishlist);
    }
}
