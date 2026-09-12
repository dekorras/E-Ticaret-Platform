using Dekorras.Application.Ordering.Queries;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Ordering;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.5'in "sipariş listesinde ... filtreleme
/// (sipariş no, müşteri, durum, tutar, tarih aralığı)" maddesi Faz 0/1'den beri yalnızca "durum"
/// filtresini destekliyordu, diğer dördü bu turda eklendi.</summary>
public sealed class OrderListFilteringRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasOrderFilteringTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static async Task SeedTwoOrdersAsync(ApplicationDbContext dbContext)
    {
        var customerGroup = new CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(customerGroup);
        await dbContext.SaveChangesAsync();

        var customerA = new Customer("identity-filter-a", "Ahmet Yılmaz", "ahmet@test.com", customerGroup.Id);
        var customerB = new Customer("identity-filter-b", "Berna Kaya", "berna@test.com", customerGroup.Id);
        dbContext.Set<Customer>().AddRange(customerA, customerB);
        var addressA = new Address(customerA.Id, "Ahmet Yılmaz", "TR", "Kayseri", "Adres 1", "5551111111");
        var addressB = new Address(customerB.Id, "Berna Kaya", "TR", "İstanbul", "Adres 2", "5552222222");
        dbContext.Set<Address>().AddRange(addressA, addressB);
        await dbContext.SaveChangesAsync();

        var orderA = new Order("FILTER-AAA-001", customerA.Id, OrderSource.Web, addressA.Id, addressA.Id);
        orderA.AddItem(Guid.NewGuid(), "Ürün A", 100m, 1, 0m); // KDV 0 - matematik basit kalsın

        var orderB = new Order("FILTER-BBB-002", customerB.Id, OrderSource.Web, addressB.Id, addressB.Id);
        orderB.AddItem(Guid.NewGuid(), "Ürün B", 500m, 1, 0m);

        dbContext.Set<Order>().AddRange(orderA, orderB);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SiparisNoFiltresiKismiEslesirVeYalnizcaIlgiliSiparisiDoner()
    {
        await using var dbContext = CreateDbContext();
        await SeedTwoOrdersAsync(dbContext);
        var unitOfWork = new UnitOfWork(dbContext);

        var result = await new GetOrdersQueryHandler(unitOfWork).Handle(new GetOrdersQuery(OrderNumberFilter: "AAA"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("FILTER-AAA-001", result.Single().OrderNumber);
    }

    [Fact]
    public async Task MusteriAdiFiltresiKismiEslesir()
    {
        await using var dbContext = CreateDbContext();
        await SeedTwoOrdersAsync(dbContext);
        var unitOfWork = new UnitOfWork(dbContext);

        var result = await new GetOrdersQueryHandler(unitOfWork).Handle(new GetOrdersQuery(CustomerNameFilter: "Berna"), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Berna Kaya", result.Single().CustomerName);
    }

    [Fact]
    public async Task TutarAraligiFiltresiDogruSiparisleriDoner()
    {
        await using var dbContext = CreateDbContext();
        await SeedTwoOrdersAsync(dbContext);
        var unitOfWork = new UnitOfWork(dbContext);

        // Yalnızca 400-600 TRY aralığındaki (500 TRY'lik B siparişi) dönmeli.
        var result = await new GetOrdersQueryHandler(unitOfWork).Handle(new GetOrdersQuery(MinAmountTry: 400m, MaxAmountTry: 600m), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("FILTER-BBB-002", result.Single().OrderNumber);
    }

    [Fact]
    public async Task TarihAraligiFiltresiGunSonunuDaKapsar()
    {
        await using var dbContext = CreateDbContext();
        await SeedTwoOrdersAsync(dbContext);
        var unitOfWork = new UnitOfWork(dbContext);

        var today = DateTime.UtcNow.Date;
        // Bitiş tarihi TAM olarak bugün (saat 00:00) verilse bile, bugün oluşturulan siparişler
        // (herhangi bir saatte) sonuca dahil olmalı - handler bunu gün sonuna genişletiyor.
        var result = await new GetOrdersQueryHandler(unitOfWork).Handle(
            new GetOrdersQuery(FromDateUtc: today, ToDateUtc: today.AddDays(1).AddTicks(-1)), CancellationToken.None);

        Assert.Equal(2, result.Count);

        // Yarından itibaren bir aralık hiçbir sipariş DÖNMEMELİ.
        var futureResult = await new GetOrdersQueryHandler(unitOfWork).Handle(
            new GetOrdersQuery(FromDateUtc: today.AddDays(1)), CancellationToken.None);
        Assert.Empty(futureResult);
    }

    [Fact]
    public async Task BirlestirilmisFiltrelerBirlikteCalisir()
    {
        await using var dbContext = CreateDbContext();
        await SeedTwoOrdersAsync(dbContext);
        var unitOfWork = new UnitOfWork(dbContext);

        // Doğru sipariş no + YANLIŞ tutar aralığı -> boş sonuç dönmeli (AND mantığı).
        var result = await new GetOrdersQueryHandler(unitOfWork).Handle(
            new GetOrdersQuery(OrderNumberFilter: "AAA", MinAmountTry: 1000m), CancellationToken.None);

        Assert.Empty(result);
    }
}
