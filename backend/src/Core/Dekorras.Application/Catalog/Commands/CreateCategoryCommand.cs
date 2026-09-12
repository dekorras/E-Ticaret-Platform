using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record CreateCategoryCommand(
    string Slug,
    Guid? ParentCategoryId,
    int DisplayOrder,
    string LanguageCode,
    string Name,
    string? Description,
    string? MetaTitle = null,
    string? MetaDescription = null) : IRequest<Guid>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateCategoryCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCategoryCommand, Guid>
{
    public async Task<Guid> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = new Category(request.Slug, request.ParentCategoryId, request.DisplayOrder);
        category.SetTranslation(request.LanguageCode, request.Name, request.Description, request.MetaTitle, request.MetaDescription);

        await unitOfWork.Repository<Category>().AddAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return category.Id;
    }
}
