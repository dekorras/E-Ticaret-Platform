using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record FeaturedHomepageCategoryDto(Guid Id, string Slug, string Name);

/// <summary>
/// Anasayfada "blok" olarak gösterilecek kategorileri döner - yalnızca admin'in
/// SetCategoryFeaturedOnHomepageCommand ile açıkça işaretlediği VE hâlâ aktif olan kategoriler.
/// Anasayfa artık tüm ürünleri filtresiz göstermek yerine bu sorgunun döndüğü her kategori için
/// AYRI bir ürün bloğu render eder (bkz. HomeController.Index) - yeni bir ürün sorgu mantığı
/// icat edilmedi, mevcut GetStorefrontProductsQuery her blok için ayrı ayrı çağrılır.
/// </summary>
public sealed record GetFeaturedHomepageCategoriesQuery(string LanguageCode) : IRequest<IReadOnlyCollection<FeaturedHomepageCategoryDto>>;

public sealed class GetFeaturedHomepageCategoriesQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetFeaturedHomepageCategoriesQuery, IReadOnlyCollection<FeaturedHomepageCategoryDto>>
{
    public Task<IReadOnlyCollection<FeaturedHomepageCategoryDto>> Handle(GetFeaturedHomepageCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = unitOfWork.Repository<Category>().Query()
            .Where(c => c.IsFeaturedOnHomepage && c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new FeaturedHomepageCategoryDto(
                c.Id,
                c.Slug,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? c.Slug))
            .ToList();

        return Task.FromResult<IReadOnlyCollection<FeaturedHomepageCategoryDto>>(categories);
    }
}
