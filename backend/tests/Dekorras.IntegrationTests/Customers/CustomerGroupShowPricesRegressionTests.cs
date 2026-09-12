using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Application.Customers.Commands;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Customers;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.6 "müşteri grupları: ... ürün fiyatlarını
/// sadece belirli gruplara gösterme opsiyonu mevcut". `CustomerGroup.ShowPricesOnStorefront`
/// Faz 0/1'den beri vardı ama hiçbir yerden set edilemiyordu (her zaman varsayılan `true` kalıyordu).</summary>
public sealed class CustomerGroupShowPricesRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasGroupPriceVisibilityTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task GrupFiyatGorunurluguKapatilipAcilabilir()
    {
        Guid groupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Bayi Onay Bekliyor");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;

            // Varsayılan olarak fiyat gösterilir.
            Assert.True(group.ShowPricesOnStorefront);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SetCustomerGroupShowPricesCommandHandler(unitOfWork).Handle(new SetCustomerGroupShowPricesCommand(groupId, false), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var group = await dbContext.CustomerGroups.SingleAsync(g => g.Id == groupId);
            Assert.False(group.ShowPricesOnStorefront);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SetCustomerGroupShowPricesCommandHandler(unitOfWork).Handle(new SetCustomerGroupShowPricesCommand(groupId, true), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var group = await dbContext.CustomerGroups.SingleAsync(g => g.Id == groupId);
            Assert.True(group.ShowPricesOnStorefront);
        }
    }

    [Fact]
    public async Task MisafirHerZamanGorurUyeGizliGruptaGormez()
    {
        Guid productId, hiddenGroupId, visibleGroupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);

            var hiddenGroup = new CustomerGroup("Bayi Onay Bekliyor");
            var visibleGroup = new CustomerGroup("Kurumsal");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(hiddenGroup, CancellationToken.None);
            await unitOfWork.Repository<CustomerGroup>().AddAsync(visibleGroup, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            hiddenGroupId = hiddenGroup.Id;
            visibleGroupId = visibleGroup.Id;

            await new SetCustomerGroupShowPricesCommandHandler(unitOfWork).Handle(new SetCustomerGroupShowPricesCommand(hiddenGroupId, false), CancellationToken.None);

            productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
                "test-fiyat-gorunurluk-urun", "VIS-1", 250m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
                BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Görünürlük Ürünü", Description: null), CancellationToken.None);
            await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
        }

        await using var dbContext2 = CreateDbContext();
        var uow = new UnitOfWork(dbContext2);

        // Misafir (grup yok) - her zaman görür.
        var guestDetail = await new GetProductBySlugQueryHandler(uow).Handle(new GetProductBySlugQuery("test-fiyat-gorunurluk-urun", "tr"), CancellationToken.None);
        Assert.True(guestDetail!.PricesVisible);

        var guestList = await new GetStorefrontProductsQueryHandler(uow).Handle(new GetStorefrontProductsQuery("tr", null), CancellationToken.None);
        Assert.True(guestList.Items.Single(p => p.Id == productId).PricesVisible);

        // Fiyatı AÇIK olan bir grubun üyesi - görür.
        var visibleDetail = await new GetProductBySlugQueryHandler(uow).Handle(new GetProductBySlugQuery("test-fiyat-gorunurluk-urun", "tr", visibleGroupId), CancellationToken.None);
        Assert.True(visibleDetail!.PricesVisible);

        // Fiyatı KAPALI olan bir grubun üyesi - göremez.
        var hiddenDetail = await new GetProductBySlugQueryHandler(uow).Handle(new GetProductBySlugQuery("test-fiyat-gorunurluk-urun", "tr", hiddenGroupId), CancellationToken.None);
        Assert.False(hiddenDetail!.PricesVisible);

        var hiddenList = await new GetStorefrontProductsQueryHandler(uow).Handle(new GetStorefrontProductsQuery("tr", null, CustomerGroupId: hiddenGroupId), CancellationToken.None);
        Assert.False(hiddenList.Items.Single(p => p.Id == productId).PricesVisible);
    }
}
