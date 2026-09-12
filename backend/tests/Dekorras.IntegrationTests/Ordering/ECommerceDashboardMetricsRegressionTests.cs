using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Ordering.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Ordering;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.3'ün Admin dashboard metrik kartlarını
/// (Toplam Sipariş/Satış/Kategori/Müşteri/Ürün) hiç yazılmamış bir özellik olarak kapatır. "Toplam
/// Satış"ın iptal/red/başarısız/hükümsüz/ters ibraz/süresi dolmuş/iade edilmiş siparişleri HARİÇ
/// TUTTUĞUNU kanıtlamak bu testin asıl amacı.</summary>
public sealed class ECommerceDashboardMetricsRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasECommerceDashboardTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task MetrikSayimlariDogruVeToplamSatisGecersizSiparisleriHaricTutar()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var customerGroup = new CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(customerGroup);
        await dbContext.SaveChangesAsync();

        var customer = new Customer("identity-dashboard-1", "Test Müşteri", "dashboard@test.com", customerGroup.Id);
        dbContext.Set<Customer>().Add(customer);
        var address = new Address(customer.Id, "Test Müşteri", "TR", "Kayseri", "Test Adres 1", "5551234567");
        dbContext.Set<Address>().Add(address);
        await dbContext.SaveChangesAsync();

        var categoryId = await new CreateCategoryCommandHandler(unitOfWork)
            .Handle(new CreateCategoryCommand("test-dashboard-kategori", null, 0, "tr", "Test Kategori", null), CancellationToken.None);

        var activeProductId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-dashboard-aktif-urun", "DASH-ACTIVE", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Aktif Ürün", Description: null), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(activeProductId, Published: true), CancellationToken.None);

        // İKİNCİ ürün BİLİNÇLİ OLARAK yayınlanmadı - "Toplam Ürün (Aktif)" yalnızca Aktif ürünleri
        // saymalı, taslak dahil TÜM ürünleri değil.
        await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            "test-dashboard-taslak-urun", "DASH-DRAFT", 50m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Taslak Ürün", Description: null), CancellationToken.None);

        // Sipariş A: PendingApproval'da kalır - GEÇERLİ bir satış, toplama DAHİL.
        var orderA = new Order("DASH-A", customer.Id, OrderSource.Web, address.Id, address.Id);
        orderA.AddItem(activeProductId, "Aktif Ürün", 100m, 1, 20m);

        // Sipariş B: İptal edilir - GEÇERSİZ bir satış, toplamdan HARİÇ TUTULMALI.
        var orderB = new Order("DASH-B", customer.Id, OrderSource.Web, address.Id, address.Id);
        orderB.AddItem(activeProductId, "Aktif Ürün", 200m, 1, 20m);
        orderB.TransitionTo(OrderStatus.Cancelled);

        // Sipariş C: Tamamlanana kadar ilerletilir - GEÇERLİ bir satış, toplama DAHİL.
        var orderC = new Order("DASH-C", customer.Id, OrderSource.Web, address.Id, address.Id);
        orderC.AddItem(activeProductId, "Aktif Ürün", 300m, 1, 20m);
        orderC.TransitionTo(OrderStatus.Preparing);
        orderC.TransitionTo(OrderStatus.Prepared);
        orderC.TransitionTo(OrderStatus.Shipped);
        orderC.TransitionTo(OrderStatus.Completed);

        dbContext.Set<Order>().AddRange(orderA, orderB, orderC);
        await dbContext.SaveChangesAsync();

        var metrics = await new GetECommerceDashboardMetricsQueryHandler(unitOfWork)
            .Handle(new GetECommerceDashboardMetricsQuery(), CancellationToken.None);

        Assert.Equal(3, metrics.TotalOrders);
        // KDV dahil tutarlar: A = 100*1.2=120, C = 300*1.2=360 -> toplam 480. B (240) HARİÇ.
        Assert.Equal(480m, metrics.TotalSalesTry);
        Assert.Equal(1, metrics.TotalCategories);
        Assert.Equal(1, metrics.TotalCustomers);
        Assert.Equal(1, metrics.TotalActiveProducts);
    }
}
