using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Ordering.Queries;

/// <summary>Bkz. plan §2.3 - Admin dashboard'ının "Toplam Sipariş"/"Toplam Satış"/"Toplam Kategori"/
/// "Toplam Müşteri"/"Toplam Ürün" kartları. **Bilinçli olarak kapsam dışı:** "Çevrimiçi Ziyaretçi"
/// (gerçek zamanlı oturum/presence takibi gerektirir - SignalR/Redis tabanlı ayrı bir alt yapı,
/// sahte bir sayı UYDURMAK yerine bu turda hiç yazılmadı), Türkiye haritası bölgesel dağılımı ve
/// sipariş/müşteri trend grafiği (bir grafik kütüphanesi gerektirir, ayrı bir UI yatırımı) - bu
/// üçü de plan'da geçiyor ama BAŞKA bir tasarım kararı/iş gerektiriyor.</summary>
public sealed record ECommerceDashboardMetricsDto(
    int TotalOrders,
    decimal TotalSalesTry,
    int TotalCategories,
    int TotalCustomers,
    int TotalActiveProducts);

public sealed record GetECommerceDashboardMetricsQuery : IRequest<ECommerceDashboardMetricsDto>;

public sealed class GetECommerceDashboardMetricsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetECommerceDashboardMetricsQuery, ECommerceDashboardMetricsDto>
{
    // Sahte/geçersiz siparişler (iptal/red/başarısız/hükümsüz/ters ibraz/süresi dolmuş/iade edilmiş)
    // "Toplam Satış" tutarına DAHİL EDİLMEZ - bunlar gerçekleşmemiş ya da geri alınmış bir geliri
    // temsil eder. Bu, tek doğru cevabı olmayan bir muhasebe kararı - burada AÇIKÇA belgeleniyor.
    private static readonly OrderStatus[] ExcludedFromSalesStatuses =
    [
        OrderStatus.Cancelled, OrderStatus.Rejected, OrderStatus.Failed,
        OrderStatus.Voided, OrderStatus.Chargeback, OrderStatus.Expired, OrderStatus.Refunded
    ];

    public Task<ECommerceDashboardMetricsDto> Handle(GetECommerceDashboardMetricsQuery request, CancellationToken cancellationToken)
    {
        var orderRepository = unitOfWork.Repository<Order>();

        var totalOrders = orderRepository.Query().Count();
        var totalSales = orderRepository.Query()
            .Where(o => !ExcludedFromSalesStatuses.Contains(o.Status))
            .Sum(o => (decimal?)o.GrandTotalTry) ?? 0m;

        var totalCategories = unitOfWork.Repository<Category>().Query().Count();
        var totalCustomers = unitOfWork.Repository<Customer>().Query().Count();
        var totalActiveProducts = unitOfWork.Repository<Product>().Query().Count(p => p.Status == ProductStatus.Active);

        var dto = new ECommerceDashboardMetricsDto(totalOrders, totalSales, totalCategories, totalCustomers, totalActiveProducts);
        return Task.FromResult(dto);
    }
}
