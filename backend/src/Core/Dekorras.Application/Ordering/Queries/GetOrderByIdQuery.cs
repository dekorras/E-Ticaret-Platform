using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using Dekorras.Domain.Shipping;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

public sealed record OrderItemDetailDto(string ProductName, decimal UnitPriceTry, int Quantity, decimal LineTotalTry);
public sealed record OrderStatusHistoryDto(OrderStatus Status, string? Note, DateTime ChangedAtUtc);
public sealed record CustomsDeclarationDto(string HsCodeSummary, decimal DeclaredValueTry, string ContentDescription);

public sealed record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    OrderSource Source,
    DateTime CreatedAtUtc,
    string CustomerName,
    string CustomerEmail,
    string ShippingRecipientName,
    string ShippingAddressLine1,
    string ShippingCity,
    string ShippingCountryCode,
    string ShippingPhoneNumber,
    decimal SubTotalTry,
    decimal TaxTotalTry,
    decimal ShippingTotalTry,
    decimal DiscountTotalTry,
    decimal GrandTotalTry,
    string? ShipmentTrackingNumber,
    PaymentStatus? PaymentStatus,
    IReadOnlyCollection<OrderItemDetailDto> Items,
    IReadOnlyCollection<OrderStatusHistoryDto> StatusHistory,
    IReadOnlyCollection<OrderStatus> ValidNextStatuses,
    CustomsDeclarationDto? CustomsDeclaration);

public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDetailDto?>;

public sealed class GetOrderByIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetOrderByIdQuery, OrderDetailDto?>
{
    public Task<OrderDetailDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = unitOfWork.Repository<Order>().Query().FirstOrDefault(o => o.Id == request.Id);
        if (order is null) return Task.FromResult<OrderDetailDto?>(null);

        var customer = unitOfWork.Repository<Customer>().Query().First(c => c.Id == order.CustomerId);
        var address = unitOfWork.Repository<Address>().Query().First(a => a.Id == order.ShippingAddressId);

        var items = unitOfWork.Repository<OrderItem>().Query()
            .Where(i => i.OrderId == order.Id)
            .Select(i => new OrderItemDetailDto(i.ProductName, i.UnitPriceTry, i.Quantity, i.UnitPriceTry * i.Quantity))
            .ToList();

        var history = unitOfWork.Repository<OrderStatusHistory>().Query()
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => new OrderStatusHistoryDto(h.Status, h.Note, h.ChangedAtUtc))
            .ToList();

        var paymentStatus = unitOfWork.Repository<Payment>().Query()
            .Where(p => p.OrderId == order.Id)
            .Select(p => (PaymentStatus?)p.Status)
            .FirstOrDefault();

        var customsDeclaration = unitOfWork.Repository<CustomsDeclaration>().Query()
            .Where(d => d.OrderId == order.Id)
            .Select(d => new CustomsDeclarationDto(d.HsCodeSummary, d.DeclaredValueTry, d.ContentDescription))
            .FirstOrDefault();

        var dto = new OrderDetailDto(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.Source,
            order.CreatedAtUtc,
            customer.FullName,
            customer.Email,
            address.RecipientName,
            address.AddressLine1,
            address.City,
            address.CountryCode,
            address.PhoneNumber,
            order.SubTotalTry,
            order.TaxTotalTry,
            order.ShippingTotalTry,
            order.DiscountTotalTry,
            order.GrandTotalTry,
            order.ShipmentTrackingNumber,
            paymentStatus,
            items,
            history,
            order.GetValidNextStatuses(),
            customsDeclaration);

        return Task.FromResult<OrderDetailDto?>(dto);
    }
}
