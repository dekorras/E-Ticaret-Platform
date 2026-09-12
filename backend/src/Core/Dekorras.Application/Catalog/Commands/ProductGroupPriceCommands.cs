using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Bkz. plan §2.6 - "Müşteri grupları: ... ürün fiyatlarını sadece belirli gruplara
/// gösterme opsiyonu mevcut". `Product.GroupPrices`/`SetGroupPrice` Faz 0/1'den beri vardı ama
/// hiçbir yerden çağrılmıyordu - checkout/sepet/vitrin fiyat hesaplaması bunu HİÇ dikkate almıyordu
/// (bkz. `ProductPricingHelper`).</summary>
public sealed record SetProductGroupPriceCommand(Guid ProductId, Guid CustomerGroupId, decimal PriceTry) : IRequest<Unit>;

public sealed class SetProductGroupPriceCommandValidator : AbstractValidator<SetProductGroupPriceCommand>
{
    public SetProductGroupPriceCommandValidator()
    {
        RuleFor(x => x.PriceTry).GreaterThan(0);
    }
}

public sealed class SetProductGroupPriceCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductGroupPriceCommand, Unit>
{
    public async Task<Unit> Handle(SetProductGroupPriceCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.GroupPrices, cancellationToken);
        product.SetGroupPrice(request.CustomerGroupId, request.PriceTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record RemoveProductGroupPriceCommand(Guid ProductId, Guid CustomerGroupId) : IRequest<Unit>;

public sealed class RemoveProductGroupPriceCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveProductGroupPriceCommand, Unit>
{
    public async Task<Unit> Handle(RemoveProductGroupPriceCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        await repository.LoadCollectionAsync(product, p => p.GroupPrices, cancellationToken);
        product.RemoveGroupPrice(request.CustomerGroupId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record ProductGroupPriceDto(Guid CustomerGroupId, string CustomerGroupName, decimal PriceTry);

public sealed record GetProductGroupPricesQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductGroupPriceDto>>;

public sealed class GetProductGroupPricesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductGroupPricesQuery, IReadOnlyCollection<ProductGroupPriceDto>>
{
    public Task<IReadOnlyCollection<ProductGroupPriceDto>> Handle(GetProductGroupPricesQuery request, CancellationToken cancellationToken)
    {
        var groupPrices = unitOfWork.Repository<Product>().Query()
            .Where(p => p.Id == request.ProductId)
            .SelectMany(p => p.GroupPrices)
            .ToList();

        var groupNames = unitOfWork.Repository<Dekorras.Domain.Customers.CustomerGroup>().Query();

        var result = groupPrices
            .Select(gp => new ProductGroupPriceDto(
                gp.CustomerGroupId,
                groupNames.Where(g => g.Id == gp.CustomerGroupId).Select(g => g.Name).FirstOrDefault() ?? "?",
                gp.PriceTry))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductGroupPriceDto>>(result);
    }
}
