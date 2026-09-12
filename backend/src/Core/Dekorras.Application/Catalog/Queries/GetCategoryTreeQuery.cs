using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record CategoryTreeItemDto(
    Guid Id,
    Guid? ParentCategoryId,
    string Slug,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsFeaturedOnHomepage,
    IReadOnlyCollection<CategoryTreeItemDto> Children);

/// <summary>Kategori ağacını (sınırsız derinlik) admin panelde göstermek için hiyerarşik olarak döner.</summary>
public sealed record GetCategoryTreeQuery(string LanguageCode) : IRequest<IReadOnlyCollection<CategoryTreeItemDto>>;

public sealed class GetCategoryTreeQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCategoryTreeQuery, IReadOnlyCollection<CategoryTreeItemDto>>
{
    public Task<IReadOnlyCollection<CategoryTreeItemDto>> Handle(GetCategoryTreeQuery request, CancellationToken cancellationToken)
    {
        // Çeviri adı, entity'ler bellekte materyalize edilmeden ÖNCE, hâlâ IQueryable iken
        // projekte edilir - böylece EF Core bunu tek bir SQL sorgusuna çevirir. Materyalize
        // edildikten sonra Translations gezinme özelliğine dokunmak (Include olmadan) sessizce
        // boş koleksiyon döner; bu, ilk sürümde tespit edilip düzeltilen bir hataydı.
        var flat = unitOfWork.Repository<Category>().Query()
            .Select(c => new
            {
                c.Id,
                c.ParentCategoryId,
                c.Slug,
                c.DisplayOrder,
                c.IsActive,
                c.IsFeaturedOnHomepage,
                Name = c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault()
            })
            .ToList();

        CategoryTreeItemDto Map(Guid id, Guid? parentId, string slug, int displayOrder, bool isActive, bool isFeaturedOnHomepage, string? name) => new(
            id,
            parentId,
            slug,
            name ?? slug,
            displayOrder,
            isActive,
            isFeaturedOnHomepage,
            flat.Where(c => c.ParentCategoryId == id)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => Map(c.Id, c.ParentCategoryId, c.Slug, c.DisplayOrder, c.IsActive, c.IsFeaturedOnHomepage, c.Name))
                .ToList());

        var roots = flat.Where(c => c.ParentCategoryId is null)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => Map(c.Id, c.ParentCategoryId, c.Slug, c.DisplayOrder, c.IsActive, c.IsFeaturedOnHomepage, c.Name))
            .ToList();

        return Task.FromResult<IReadOnlyCollection<CategoryTreeItemDto>>(roots);
    }
}
