using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

/// <summary>Bir müşteri giriş yaptığında/kayıt olduğunda çağrılır - `Cart.CustomerId` Faz 0/1'den
/// beri vardı ama hiçbir yerden set edilmiyordu, yani bir müşterinin sepeti YALNIZCA o anki tarayıcı
/// çerezine (bkz. `CartSession`) bağlıydı ve başka bir cihazdan asla erişilemezdi. Bu komut iki
/// senaryoyu ele alır: (1) müşterinin başka bir cihazda kayıtlı sepeti yoksa, bu oturumun (varsa)
/// misafir sepetini müşteriye bağlar; (2) varsa, müşterinin kayıtlı sepetini bu cihaza taşır/
/// birleştirir.</summary>
public sealed record MergeGuestCartIntoCustomerCommand(string SessionKey, Guid CustomerId) : IRequest<Unit>;

public sealed class MergeGuestCartIntoCustomerCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MergeGuestCartIntoCustomerCommand, Unit>
{
    public async Task<Unit> Handle(MergeGuestCartIntoCustomerCommand request, CancellationToken cancellationToken)
    {
        var cartRepository = unitOfWork.Repository<Cart>();
        var sessionCart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey);

        // Bu cihazın sepeti ZATEN başka, bilinen bir müşteriye ait (muhtemelen o müşteri çıkış
        // yapmadan tarayıcıyı kapattı - bkz. AccountController.Logout'un çerezi temizlemesi normal
        // akışta bunu engeller, ama bir güvenlik ağı olarak burada da BİLİNÇLİ OLARAK dokunulmuyor).
        // Üzerine yazıp bir müşterinin sepetini başka birine "çalmak" yerine bu müşteri bu cihazda
        // BOŞ bir sepetle başlar; kendi sepeti (varsa) başka bir cihazdan erişilebilir kalır.
        if (sessionCart is not null && sessionCart.CustomerId is Guid existingOwnerId && existingOwnerId != request.CustomerId)
            return Unit.Value;

        var customerCart = cartRepository.Query().FirstOrDefault(c => c.CustomerId == request.CustomerId);

        if (customerCart is null)
        {
            if (sessionCart is not null)
            {
                sessionCart.SetCustomerId(request.CustomerId);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return Unit.Value;
        }

        if (sessionCart is null || sessionCart.Id == customerCart.Id)
        {
            // Bu cihazda misafir sepeti yok VEYA zaten müşterinin kendi sepeti (aynı cihazda tekrar
            // giriş) - müşterinin kayıtlı sepetini bu oturumun anahtarına taşı ki gelecekteki
            // istekler (SessionKey'e göre arar) bulabilsin.
            customerCart.SetSessionKey(request.SessionKey);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }

        // Hem bu cihazda (misafir) hem başka bir cihazda (müşteri) sepet var - birleştirilir. Aynı
        // üründen ikisinde de varsa AKTİF (bu oturumun) satır korunur, üzerine yazılmaz.
        await cartRepository.LoadCollectionAsync(sessionCart, c => c.Items, cancellationToken);
        await cartRepository.LoadCollectionAsync(customerCart, c => c.Items, cancellationToken);

        foreach (var item in customerCart.Items)
        {
            var alreadyInSession = sessionCart.Items.Any(i => i.ProductId == item.ProductId && i.VariantId == item.VariantId);
            if (!alreadyInSession)
                sessionCart.AddOrUpdateItem(item.ProductId, item.VariantId, item.Quantity, item.UnitPriceTry);
        }

        if (sessionCart.CouponCode is null && customerCart.CouponCode is not null)
            sessionCart.ApplyCoupon(customerCart.CouponCode);
        if (sessionCart.GiftVoucherCode is null && customerCart.GiftVoucherCode is not null)
            sessionCart.ApplyGiftVoucher(customerCart.GiftVoucherCode);

        sessionCart.SetCustomerId(request.CustomerId);
        cartRepository.Remove(customerCart);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
