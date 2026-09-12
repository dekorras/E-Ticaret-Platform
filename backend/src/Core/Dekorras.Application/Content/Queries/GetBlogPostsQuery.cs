using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using MediatR;

namespace Dekorras.Application.Content.Queries;

public sealed record BlogPostListItemDto(Guid Id, string Slug, string LanguageCode, string Title, bool IsPublished);

/// <summary>Admin listesi - TÜM yazıları (taslak/yayında) döner.</summary>
public sealed record GetBlogPostsQuery : IRequest<IReadOnlyCollection<BlogPostListItemDto>>;

public sealed class GetBlogPostsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBlogPostsQuery, IReadOnlyCollection<BlogPostListItemDto>>
{
    public Task<IReadOnlyCollection<BlogPostListItemDto>> Handle(GetBlogPostsQuery request, CancellationToken cancellationToken)
    {
        var posts = unitOfWork.Repository<BlogPost>().Query()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new BlogPostListItemDto(p.Id, p.Slug, p.LanguageCode, p.Title, p.PublishedAtUtc != null))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BlogPostListItemDto>>(posts);
    }
}

public sealed record BlogPostDetailDto(Guid Id, string Slug, string LanguageCode, string Title, string ContentHtml, bool IsPublished, DateTime? PublishedAtUtc);

public sealed record GetBlogPostByIdQuery(Guid Id) : IRequest<BlogPostDetailDto?>;

public sealed class GetBlogPostByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBlogPostByIdQuery, BlogPostDetailDto?>
{
    public async Task<BlogPostDetailDto?> Handle(GetBlogPostByIdQuery request, CancellationToken cancellationToken)
    {
        var post = await unitOfWork.Repository<BlogPost>().GetByIdAsync(request.Id, cancellationToken);
        return post is null ? null : new BlogPostDetailDto(post.Id, post.Slug, post.LanguageCode, post.Title, post.ContentHtml, post.PublishedAtUtc != null, post.PublishedAtUtc);
    }
}

/// <summary>Storefront listesi - yalnızca yayınlanmış yazılar, en yeniden eskiye.</summary>
public sealed record GetPublishedBlogPostsQuery(string LanguageCode) : IRequest<IReadOnlyCollection<BlogPostListItemDto>>;

public sealed class GetPublishedBlogPostsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPublishedBlogPostsQuery, IReadOnlyCollection<BlogPostListItemDto>>
{
    public Task<IReadOnlyCollection<BlogPostListItemDto>> Handle(GetPublishedBlogPostsQuery request, CancellationToken cancellationToken)
    {
        var posts = unitOfWork.Repository<BlogPost>().Query()
            .Where(p => p.LanguageCode == request.LanguageCode && p.PublishedAtUtc != null)
            .OrderByDescending(p => p.PublishedAtUtc)
            .Select(p => new BlogPostListItemDto(p.Id, p.Slug, p.LanguageCode, p.Title, true))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BlogPostListItemDto>>(posts);
    }
}

/// <summary>Storefront detayı - yalnızca yayınlanmış bir yazı döner.</summary>
public sealed record GetBlogPostBySlugQuery(string Slug, string LanguageCode) : IRequest<BlogPostDetailDto?>;

public sealed class GetBlogPostBySlugQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBlogPostBySlugQuery, BlogPostDetailDto?>
{
    public Task<BlogPostDetailDto?> Handle(GetBlogPostBySlugQuery request, CancellationToken cancellationToken)
    {
        var dto = unitOfWork.Repository<BlogPost>().Query()
            .Where(p => p.Slug == request.Slug && p.LanguageCode == request.LanguageCode && p.PublishedAtUtc != null)
            .Select(p => new BlogPostDetailDto(p.Id, p.Slug, p.LanguageCode, p.Title, p.ContentHtml, true, p.PublishedAtUtc))
            .FirstOrDefault();

        return Task.FromResult(dto);
    }
}
