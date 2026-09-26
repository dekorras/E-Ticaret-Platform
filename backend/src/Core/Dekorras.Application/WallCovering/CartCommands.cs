using System.Text.Json;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record AddConfiguredCartItemResult(Guid CartItemId, WallpaperLinePrice Line);

/// <summary>Ölçüye özel ürünü sepete ekler. Fiyat istemciden ALINMAZ - burada sunucuda hesaplanır.</summary>
public sealed record AddConfiguredCartItemCommand(string SessionKey, Guid ProductId, WallConfiguration Configuration, int Quantity, Guid? CustomerId = null)
    : IRequest<AddConfiguredCartItemResult>;

public sealed class AddConfiguredCartItemCommandValidator : AbstractValidator<AddConfiguredCartItemCommand>
{
    public AddConfiguredCartItemCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
        RuleFor(x => x.Configuration).NotNull();
        RuleFor(x => x.Configuration.MaterialCode).NotEmpty().When(x => x.Configuration is not null);
    }
}

public sealed class AddConfiguredCartItemCommandHandler(IUnitOfWork unitOfWork, IPricingService pricingService)
    : IRequestHandler<AddConfiguredCartItemCommand, AddConfiguredCartItemResult>
{
    public async Task<AddConfiguredCartItemResult> Handle(AddConfiguredCartItemCommand request, CancellationToken cancellationToken)
    {
        // Birim fiyat = 1 adetlik satır fiyatı; sepet satırı adetle çarpar (formüldeki "× adet" ile aynı).
        var quote = pricingService.QuoteLine(request.ProductId, request.Configuration, 1);
        if (!quote.IsValid) throw new WallConfigurationException(quote.Errors);
        var unitQuote = quote.Line!;

        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        CartItem item;
        if (cart is null)
        {
            cart = new Cart(request.SessionKey, request.CustomerId);
            item = cart.AddConfiguredItem(request.ProductId, request.Configuration.ToJson(), request.Configuration.Hash(), JsonSerializer.Serialize(unitQuote), request.Quantity, unitQuote.UnitPrice);
            await cartRepository.AddAsync(cart, cancellationToken);
        }
        else
        {
            // Items yüklenmeden aynı konfigürasyon bulunamaz ve yinelenen satır eklenir - bkz. IRepository.LoadCollectionAsync.
            await cartRepository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);
            item = cart.AddConfiguredItem(request.ProductId, request.Configuration.ToJson(), request.Configuration.Hash(), JsonSerializer.Serialize(unitQuote), request.Quantity, unitQuote.UnitPrice);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AddConfiguredCartItemResult(item.Id, unitQuote with { Quantity = item.Quantity, LineTotal = unitQuote.UnitPrice * item.Quantity });
    }
}

/// <summary>Sepet satırının adedini kimliğine göre değiştirir (konfigüre satırlar ProductId ile ayırt
/// edilemez). Konfigüre satırda fiyat GÜNCEL malzeme fiyatıyla yeniden hesaplanır.</summary>
public sealed record UpdateCartItemQuantityByIdCommand(string SessionKey, Guid CartItemId, int Quantity) : IRequest<Unit>;

public sealed class UpdateCartItemQuantityByIdCommandHandler(IUnitOfWork unitOfWork, IPricingService pricingService)
    : IRequestHandler<UpdateCartItemQuantityByIdCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCartItemQuantityByIdCommand request, CancellationToken cancellationToken)
    {
        if (request.Quantity is < 1 or > 99)
            throw new WallConfigurationException(new Dictionary<string, string> { ["quantity"] = "Adet 1 ile 99 arasında olmalıdır." });

        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey)
            ?? throw new KeyNotFoundException("Sepet bulunamadı.");
        await cartRepository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);

        var item = cart.Items.FirstOrDefault(i => i.Id == request.CartItemId)
            ?? throw new KeyNotFoundException("Sepet satırı bulunamadı.");

        if (item.IsConfigured && WallConfiguration.FromJson(item.ConfigurationJson) is { } configuration)
        {
            var quote = pricingService.QuoteLine(item.ProductId, configuration, 1);
            if (!quote.IsValid) throw new WallConfigurationException(quote.Errors);
            item.SetUnitPrice(quote.Line!.UnitPrice);
            item.SetPriceSnapshot(JsonSerializer.Serialize(quote.Line));
        }

        item.SetQuantity(request.Quantity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RemoveCartItemByIdCommand(string SessionKey, Guid CartItemId) : IRequest<Unit>;

public sealed class RemoveCartItemByIdCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveCartItemByIdCommand, Unit>
{
    public async Task<Unit> Handle(RemoveCartItemByIdCommand request, CancellationToken cancellationToken)
    {
        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);
        if (cart is null) return Unit.Value;

        await cartRepository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);
        cart.RemoveItemById(request.CartItemId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
