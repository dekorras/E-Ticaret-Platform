using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

public sealed record StorefrontMenuItemDto(
    Guid Id,
    string Slug,
    string Name,
    string? ImageUrl,
    IReadOnlyCollection<StorefrontMenuItemDto> Children);

/// <summary>Mağaza üst menüsü - yalnızca Aktif kategoriler görünür (pasif bir kategori ve onun
/// altındaki dal, admin panelinde deaktifleştirilene kadar müşteriye hiç gösterilmez).</summary>
public sealed record GetStorefrontMenuQuery(string LanguageCode) : IRequest<IReadOnlyCollection<StorefrontMenuItemDto>>;

public sealed class GetStorefrontMenuQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetStorefrontMenuQuery, IReadOnlyCollection<StorefrontMenuItemDto>>
{
    public Task<IReadOnlyCollection<StorefrontMenuItemDto>> Handle(GetStorefrontMenuQuery request, CancellationToken cancellationToken)
    {
        var flat = unitOfWork.Repository<Category>().Query()
            .Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id,
                c.ParentCategoryId,
                c.Slug,
                c.DisplayOrder,
                c.ImageUrl,
                Name = c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault()
            })
            .ToList();

        StorefrontMenuItemDto Map(Guid id, string slug, string? name, string? imageUrl) => new(
            id,
            slug,
            name ?? slug,
            imageUrl,
            flat.Where(c => c.ParentCategoryId == id)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => Map(c.Id, c.Slug, c.Name, c.ImageUrl))
                .ToList());

        var roots = flat.Where(c => c.ParentCategoryId is null)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => Map(c.Id, c.Slug, c.Name, c.ImageUrl))
            .ToList();

        return Task.FromResult<IReadOnlyCollection<StorefrontMenuItemDto>>(roots);
    }
}
