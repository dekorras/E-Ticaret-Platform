using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record AddProductVideoCommand(Guid ProductId, string Url, string? Title) : IRequest<Guid>;

public sealed class AddProductVideoCommandValidator : AbstractValidator<AddProductVideoCommand>
{
    public AddProductVideoCommandValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Title).MaximumLength(200);
    }
}

public sealed class AddProductVideoCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddProductVideoCommand, Guid>
{
    public async Task<Guid> Handle(AddProductVideoCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Videos, cancellationToken);

        product.AddVideo(request.Url, request.Title);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Videos.Last().Id;
    }
}

public sealed record RemoveProductVideoCommand(Guid ProductId, Guid VideoId) : IRequest<Unit>;

public sealed class RemoveProductVideoCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveProductVideoCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductVideoCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.Videos, cancellationToken);

        product.RemoveVideo(request.VideoId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
