using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record CategoryDetailDto(Guid Id, string Slug, Guid? ParentCategoryId, string Name, string? Description, bool IsActive, string? MetaTitle, string? MetaDescription);

public sealed record GetCategoryByIdQuery(Guid Id, string LanguageCode) : IRequest<CategoryDetailDto?>;

public sealed class GetCategoryByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCategoryByIdQuery, CategoryDetailDto?>
{
    public Task<CategoryDetailDto?> Handle(GetCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        // Not: GetByIdAsync (DbSet.FindAsync) gezinme özelliklerini eager-load ETMEZ; bu yüzden
        // burada Translations'a IQueryable projeksiyonu üzerinden, hâlâ SQL'e çevrilebilirken erişiyoruz.
        var dto = unitOfWork.Repository<Category>().Query()
            .Where(c => c.Id == request.Id)
            .Select(c => new CategoryDetailDto(
                c.Id,
                c.Slug,
                c.ParentCategoryId,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Name).FirstOrDefault() ?? c.Slug,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Description).FirstOrDefault(),
                c.IsActive,
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                c.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault()))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
