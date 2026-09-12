using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Commands;

public sealed record AddToWishlistCommand(string IdentityUserId, Guid ProductId) : IRequest<Unit>;

public sealed class AddToWishlistCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AddToWishlistCommand, Unit>
{
    public async Task<Unit> Handle(AddToWishlistCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Customer>();
        var customer = repository.Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId)
            ?? throw new InvalidOperationException("Müşteri profili bulunamadı.");

        await repository.LoadCollectionAsync(customer, c => c.Wishlist, cancellationToken);

        customer.AddToWishlist(request.ProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record RemoveFromWishlistCommand(string IdentityUserId, Guid ProductId) : IRequest<Unit>;

public sealed class RemoveFromWishlistCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RemoveFromWishlistCommand, Unit>
{
    public async Task<Unit> Handle(RemoveFromWishlistCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Customer>();
        var customer = repository.Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null) return Unit.Value;

        await repository.LoadCollectionAsync(customer, c => c.Wishlist, cancellationToken);

        customer.RemoveFromWishlist(request.ProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetNewsletterSubscriptionCommand(string IdentityUserId, bool Subscribed) : IRequest<Unit>;

public sealed class SetNewsletterSubscriptionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetNewsletterSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(SetNewsletterSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Customer>();
        var customer = repository.Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId)
            ?? throw new InvalidOperationException("Müşteri profili bulunamadı.");

        // Update(customer) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.Subscribed) customer.SubscribeNewsletter(); else customer.UnsubscribeNewsletter();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
