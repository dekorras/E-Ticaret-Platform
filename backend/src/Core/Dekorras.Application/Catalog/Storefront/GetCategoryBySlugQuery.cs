using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Storefront;

public sealed record StorefrontCategoryDto(Guid Id, string Slug, string Name, string? Description, string? MetaTitle, string? MetaDescription);

/// <summary>Yalnızca Aktif bir kategori döner - pasif kategorinin slug'ı ile doğrudan gidilmeye
/// çalışılsa bile mağazada 404 görünmelidir.</summary>
public sealed record GetCategoryBySlugQuery(string Slug, string LanguageCode) : IRequest<StorefrontCategoryDto?>;

public sealed class GetCategoryBySlugQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCategoryBySlugQuery, StorefrontCategoryDto?>
{
    public Task<StorefrontCategoryDto?> Handle(GetCategoryBySlugQuery request, CancellationToken cancellationToken)
    {
        var dto = unitOfWork.Repository<Category>().Query()
            .Where(c => c.Slug == request.Slug && c.IsActive)
            .Select(c => new StorefrontCategoryDto(
                c.Id,
                c.Slug,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? c.Slug,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Description).FirstOrDefault(),
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault()))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
