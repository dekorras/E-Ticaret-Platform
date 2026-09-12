using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using MediatR;

namespace Dekorras.Application.Content.Queries;

public sealed record CmsPageListItemDto(Guid Id, string Slug, string Title, bool IsActive);

public sealed record GetCmsPagesQuery(string LanguageCode) : IRequest<IReadOnlyCollection<CmsPageListItemDto>>;

public sealed class GetCmsPagesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCmsPagesQuery, IReadOnlyCollection<CmsPageListItemDto>>
{
    public Task<IReadOnlyCollection<CmsPageListItemDto>> Handle(GetCmsPagesQuery request, CancellationToken cancellationToken)
    {
        var pages = unitOfWork.Repository<CmsPage>().Query()
            .OrderBy(p => p.Slug)
            .Select(p => new CmsPageListItemDto(
                p.Id,
                p.Slug,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Title).FirstOrDefault() ?? p.Slug,
                p.IsActive))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CmsPageListItemDto>>(pages);
    }
}

public sealed record CmsPageDetailDto(Guid Id, string Slug, bool IsActive, string Title, string ContentHtml, string? MetaTitle, string? MetaDescription);

public sealed record GetCmsPageByIdQuery(Guid Id, string LanguageCode) : IRequest<CmsPageDetailDto?>;

public sealed class GetCmsPageByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCmsPageByIdQuery, CmsPageDetailDto?>
{
    public Task<CmsPageDetailDto?> Handle(GetCmsPageByIdQuery request, CancellationToken cancellationToken)
    {
        var dto = unitOfWork.Repository<CmsPage>().Query()
            .Where(p => p.Id == request.Id)
            .Select(p => new CmsPageDetailDto(
                p.Id,
                p.Slug,
                p.IsActive,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Title).FirstOrDefault() ?? "",
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.ContentHtml).FirstOrDefault() ?? "",
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault()))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}

/// <summary>Storefront - yalnızca Aktif bir sayfa döner (bkz. Product/Category'deki aynı desen).</summary>
public sealed record GetCmsPageBySlugQuery(string Slug, string LanguageCode) : IRequest<CmsPageDetailDto?>;

public sealed class GetCmsPageBySlugQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCmsPageBySlugQuery, CmsPageDetailDto?>
{
    public Task<CmsPageDetailDto?> Handle(GetCmsPageBySlugQuery request, CancellationToken cancellationToken)
    {
        var dto = unitOfWork.Repository<CmsPage>().Query()
            .Where(p => p.Slug == request.Slug && p.IsActive)
            .Select(p => new CmsPageDetailDto(
                p.Id,
                p.Slug,
                p.IsActive,
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.Title).FirstOrDefault() ?? "",
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.ContentHtml).FirstOrDefault() ?? "",
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaTitle).FirstOrDefault(),
                p.Translations.Where(t => t.LanguageCode == request.LanguageCode).Select(t => t.MetaDescription).FirstOrDefault()))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
