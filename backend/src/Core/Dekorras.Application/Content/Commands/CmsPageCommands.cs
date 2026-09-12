using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

public sealed record CreateCmsPageCommand(
    string Slug,
    string LanguageCode,
    string Title,
    string ContentHtml,
    string? MetaTitle = null,
    string? MetaDescription = null) : IRequest<Guid>;

public sealed class CreateCmsPageCommandValidator : AbstractValidator<CreateCmsPageCommand>
{
    public CreateCmsPageCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentHtml).NotEmpty();
    }
}

public sealed class CreateCmsPageCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCmsPageCommand, Guid>
{
    public async Task<Guid> Handle(CreateCmsPageCommand request, CancellationToken cancellationToken)
    {
        var exists = unitOfWork.Repository<CmsPage>().Query().Any(p => p.Slug == request.Slug);
        if (exists)
            throw new InvalidOperationException($"'{request.Slug}' slug'lı bir sayfa zaten var.");

        var page = new CmsPage(request.Slug);
        page.SetTranslation(request.LanguageCode, request.Title, request.ContentHtml, request.MetaTitle, request.MetaDescription);

        await unitOfWork.Repository<CmsPage>().AddAsync(page, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return page.Id;
    }
}

public sealed record UpdateCmsPageCommand(
    Guid Id,
    string Slug,
    string LanguageCode,
    string Title,
    string ContentHtml,
    string? MetaTitle = null,
    string? MetaDescription = null) : IRequest<Unit>;

public sealed class UpdateCmsPageCommandValidator : AbstractValidator<UpdateCmsPageCommand>
{
    public UpdateCmsPageCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContentHtml).NotEmpty();
    }
}

public sealed class UpdateCmsPageCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateCmsPageCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCmsPageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<CmsPage>();
        var page = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı sayfa bulunamadı.");

        // Translations yüklenmeden SetTranslation çağrılırsa mevcut kayıt bulunamaz sanılır ve
        // yinelenen bir satır eklenir - bkz. IRepository<T>.LoadCollectionAsync.
        await repository.LoadCollectionAsync(page, p => p.Translations, cancellationToken);

        page.UpdateSlug(request.Slug);
        // Update(page) BİLİNÇLİ OLARAK çağrılmaz: SetTranslation yeni bir çeviri ekleyebilir -
        // bkz. TransitionOrderStatusCommand'daki not.
        page.SetTranslation(request.LanguageCode, request.Title, request.ContentHtml, request.MetaTitle, request.MetaDescription);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetCmsPageActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetCmsPageActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCmsPageActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetCmsPageActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<CmsPage>();
        var page = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı sayfa bulunamadı.");

        if (request.IsActive) page.Activate(); else page.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
