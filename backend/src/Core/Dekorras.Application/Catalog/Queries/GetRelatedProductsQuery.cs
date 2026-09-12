using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record RelatedProductItemDto(Guid Id, string Name, string ProductCode, ProductStatus Status);

/// <summary>Admin ürün düzenleme ekranındaki "Bağlantılar" sekmesi için - durumdan bağımsız
/// (Taslak dahil) TÜM ilişkili ürünleri döner, admin hangisinin yayında olmadığını görebilsin.</summary>
public sealed record GetRelatedProductsQuery(Guid ProductId, string LanguageCode) : IRequest<IReadOnlyCollection<RelatedProductItemDto>>;

public sealed class GetRelatedProductsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetRelatedProductsQuery, IReadOnlyCollection<RelatedProductItemDto>>
{
    public Task<IReadOnlyCollection<RelatedProductItemDto>> Handle(GetRelatedProductsQuery request, CancellationToken cancellationToken)
    {
        var relatedIds = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Id == request.ProductId)
            .SelectMany(p => p.RelatedProducts.Select(r => r.RelatedProductId));

        var result = unitOfWork.Repository<Product>().Query()
            .Where(p => relatedIds.Contains(p.Id))
            .Select(p => new RelatedProductItemDto(
                p.Id,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                p.ProductCode,
                p.Status))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<RelatedProductItemDto>>(result);
    }
}
