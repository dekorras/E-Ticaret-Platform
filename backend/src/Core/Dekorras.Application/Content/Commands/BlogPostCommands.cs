using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

/// <summary>CmsPage'in aksine BlogPost'ta çok dillilik bir Translations alt koleksiyonu ile DEĞİL,
/// aynı slug'ı FARKLI dillerde AYRI satırlar olarak (bkz. `(Slug, LanguageCode)` benzersiz dizini)
/// tutarak sağlanır - her yeni dil çevirisi YENİ bir kayıttır, mevcut olanı güncellemez.</summary>
public sealed record CreateBlogPostCommand(string Slug, string LanguageCode, string Title, string ContentHtml) : IRequest<Guid>;

public sealed class CreateBlogPostCommandValidator : AbstractValidator<CreateBlogPostCommand>
{
    public CreateBlogPostCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentHtml).NotEmpty();
    }
}

public sealed class CreateBlogPostCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateBlogPostCommand, Guid>
{
    public async Task<Guid> Handle(CreateBlogPostCommand request, CancellationToken cancellationToken)
    {
        var exists = unitOfWork.Repository<BlogPost>().Query()
            .Any(p => p.Slug == request.Slug && p.LanguageCode == request.LanguageCode);
        if (exists)
            throw new InvalidOperationException($"'{request.Slug}' slug'lı, '{request.LanguageCode}' dilinde bir yazı zaten var.");

        var post = new BlogPost(request.Slug, request.Title, request.ContentHtml, request.LanguageCode);
        await unitOfWork.Repository<BlogPost>().AddAsync(post, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return post.Id;
    }
}

public sealed record UpdateBlogPostCommand(Guid Id, string Title, string ContentHtml) : IRequest<Unit>;

public sealed class UpdateBlogPostCommandValidator : AbstractValidator<UpdateBlogPostCommand>
{
    public UpdateBlogPostCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentHtml).NotEmpty();
    }
}

public sealed class UpdateBlogPostCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateBlogPostCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBlogPostCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BlogPost>();
        var post = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı yazı bulunamadı.");

        post.Update(request.Title, request.ContentHtml);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetBlogPostPublishedCommand(Guid Id, bool Published) : IRequest<Unit>;

public sealed class SetBlogPostPublishedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetBlogPostPublishedCommand, Unit>
{
    public async Task<Unit> Handle(SetBlogPostPublishedCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BlogPost>();
        var post = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı yazı bulunamadı.");

        if (request.Published) post.Publish(); else post.Unpublish();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
