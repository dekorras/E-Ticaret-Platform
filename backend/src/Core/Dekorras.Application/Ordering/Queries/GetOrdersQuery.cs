using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

public sealed record OrderListItemDto(Guid Id, string OrderNumber, string CustomerName, DateTime CreatedAtUtc, decimal GrandTotalTry, OrderStatus Status, OrderSource Source);

/// <summary>Admin panelinin Satış/Sipariş listesi - kaynağı (Web/Mobil/Trendyol/...) fark
/// etmeksizin TÜM siparişleri tek bir ekranda birleştirir (bkz. plan §9.1: "tüm siparişler admin
/// panelde tek bir Satış ekranında birleşir"). Filtreleme seçenekleri plan §2.5'in "sipariş
/// listesinde ... filtreleme (sipariş no, müşteri, durum, tutar, tarih aralığı)" maddesiyle
/// eşleşir - başlangıçta yalnızca durum filtresi vardı, diğer dördü sonradan eklendi.</summary>
public sealed record GetOrdersQuery(
    OrderStatus? StatusFilter = null,
    string? OrderNumberFilter = null,
    string? CustomerNameFilter = null,
    decimal? MinAmountTry = null,
    decimal? MaxAmountTry = null,
    DateTime? FromDateUtc = null,
    DateTime? ToDateUtc = null,
    int Page = 1,
    int PageSize = 50)
    : IRequest<IReadOnlyCollection<OrderListItemDto>>;

public sealed class GetOrdersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetOrdersQuery, IReadOnlyCollection<OrderListItemDto>>
{
    public Task<IReadOnlyCollection<OrderListItemDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var query =
            from o in unitOfWork.Repository<Order>().Query()
            join c in unitOfWork.Repository<Customer>().Query() on o.CustomerId equals c.Id
            select new { Order = o, Customer = c };

        if (request.StatusFilter is not null)
            query = query.Where(x => x.Order.Status == request.StatusFilter);

        if (!string.IsNullOrWhiteSpace(request.OrderNumberFilter))
            query = query.Where(x => x.Order.OrderNumber.Contains(request.OrderNumberFilter));

        if (!string.IsNullOrWhiteSpace(request.CustomerNameFilter))
            query = query.Where(x => x.Customer.FullName.Contains(request.CustomerNameFilter));

        if (request.MinAmountTry is decimal minAmount)
            query = query.Where(x => x.Order.GrandTotalTry >= minAmount);

        if (request.MaxAmountTry is decimal maxAmount)
            query = query.Where(x => x.Order.GrandTotalTry <= maxAmount);

        if (request.FromDateUtc is DateTime fromDate)
            query = query.Where(x => x.Order.CreatedAtUtc >= fromDate);

        if (request.ToDateUtc is DateTime toDate)
            query = query.Where(x => x.Order.CreatedAtUtc <= toDate);

        var orders = query
            .OrderByDescending(x => x.Order.CreatedAtUtc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new OrderListItemDto(x.Order.Id, x.Order.OrderNumber, x.Customer.FullName, x.Order.CreatedAtUtc, x.Order.GrandTotalTry, x.Order.Status, x.Order.Source))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<OrderListItemDto>>(orders);
    }
}
