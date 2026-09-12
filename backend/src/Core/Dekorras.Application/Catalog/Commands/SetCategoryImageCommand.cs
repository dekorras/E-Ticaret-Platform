using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SetCategoryImageCommand(Guid CategoryId, Stream Content, string FileName, string ContentType) : IRequest<Unit>;

public sealed class SetCategoryImageCommandValidator : AbstractValidator<SetCategoryImageCommand>
{
    public SetCategoryImageCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).Must(t => t.StartsWith("image/")).WithMessage("Yalnızca görsel dosyaları yüklenebilir.");
    }
}

public sealed class SetCategoryImageCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage) : IRequestHandler<SetCategoryImageCommand, Unit>
{
    public async Task<Unit> Handle(SetCategoryImageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Category>();
        var category = await repository.GetByIdAsync(request.CategoryId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CategoryId}' numaralı kategori bulunamadı.");

        var url = await fileStorage.UploadAsync("category-images", request.FileName, request.Content, request.ContentType, cancellationToken);
        category.SetImage(url);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
