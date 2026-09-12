using Dekorras.Application.Catalog;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Storefront;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Catalog;

/// <summary>Gerçek SQL Server'a karşı çalışır - plan §2.6 "müşteri grupları: ... ürün fiyatlarını
/// sadece belirli gruplara gösterme opsiyonu". `Product.GroupPrices`/`SetGroupPrice` Faz 0/1'den
/// beri vardı ama checkout/sepet/vitrin fiyat hesaplaması bunu HİÇ dikkate almıyordu - bu turda
/// `ProductPricingHelper` ile üç yerin de (sepet, sipariş, vitrin) AYNI önceliği (kademe > grup >
/// taban) uygulaması sağlandı.</summary>
public sealed class CustomerGroupPricingRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCustomerGroupPricingTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task GrupFiyatiAyarlanirGuncellenirVeKaldirilabilir()
    {
        Guid productId, groupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Kurumsal");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;

            productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
                "test-grup-fiyat-urun", "GRP-1", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
                BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Grup Fiyat Ürünü", Description: null), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SetProductGroupPriceCommandHandler(unitOfWork).Handle(new SetProductGroupPriceCommand(productId, groupId, 80m), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var prices = await new GetProductGroupPricesQueryHandler(unitOfWork).Handle(new GetProductGroupPricesQuery(productId), CancellationToken.None);
            Assert.Single(prices);
            Assert.Equal(80m, prices.Single().PriceTry);
            Assert.Equal("Kurumsal", prices.Single().CustomerGroupName);
        }

        // Aynı gruba İKİNCİ kez fiyat yazmak yeni bir satır DEĞİL, mevcut satırın GÜNCELLENMESİ olmalı.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SetProductGroupPriceCommandHandler(unitOfWork).Handle(new SetProductGroupPriceCommand(productId, groupId, 75m), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var prices = await new GetProductGroupPricesQueryHandler(unitOfWork).Handle(new GetProductGroupPricesQuery(productId), CancellationToken.None);
            Assert.Single(prices);
            Assert.Equal(75m, prices.Single().PriceTry);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new RemoveProductGroupPriceCommandHandler(unitOfWork).Handle(new RemoveProductGroupPriceCommand(productId, groupId), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var prices = await new GetProductGroupPricesQueryHandler(unitOfWork).Handle(new GetProductGroupPricesQuery(productId), CancellationToken.None);
            Assert.Empty(prices);
        }
    }

    [Fact]
    public async Task FiyatOnceligiKademeGruptanTabandanOncedirGrupTabandanOncedir()
    {
        Guid productId, groupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Kurumsal");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;

            productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
                "test-fiyat-onceligi-urun", "GRP-2", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 100,
                BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Öncelik Ürünü", Description: null), CancellationToken.None);

            await new SetProductGroupPriceCommandHandler(unitOfWork).Handle(new SetProductGroupPriceCommand(productId, groupId, 80m), CancellationToken.None);
            await new AddProductQuantityDiscountCommandHandler(unitOfWork).Handle(new AddProductQuantityDiscountCommand(productId, 10, 60m), CancellationToken.None);
        }

        await using var dbContext2 = CreateDbContext();
        var uow = new UnitOfWork(dbContext2);

        // Misafir (grup yok), az miktar -> taban fiyat.
        Assert.Equal(100m, ProductPricingHelper.ResolveUnitPriceTry(uow, productId, 100m, 1, null));

        // Kurumsal grup üyesi, az miktar (kademe eşiğinin altında) -> grup fiyatı.
        Assert.Equal(80m, ProductPricingHelper.ResolveUnitPriceTry(uow, productId, 100m, 1, groupId));

        // Misafir ama kademe eşiğini geçen miktar -> kademe fiyatı (gruptan bağımsız).
        Assert.Equal(60m, ProductPricingHelper.ResolveUnitPriceTry(uow, productId, 100m, 10, null));

        // Kurumsal grup üyesi VE kademe eşiğini geçen miktar -> kademe fiyatı KAZANIR (grup fiyatından ucuz olsa bile).
        Assert.Equal(60m, ProductPricingHelper.ResolveUnitPriceTry(uow, productId, 100m, 10, groupId));
    }

    [Fact]
    public async Task SepeteEklemeMisafirdeTabanUyedeGrupFiyatiUygular()
    {
        Guid productId, groupId, otherGroupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Kurumsal");
            var otherGroup = new CustomerGroup("Bireysel");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.Repository<CustomerGroup>().AddAsync(otherGroup, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;
            otherGroupId = otherGroup.Id;

            productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
                "test-sepet-grup-fiyat-urun", "GRP-3", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
                BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Sepet Grup Ürünü", Description: null), CancellationToken.None);
            await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
            await new SetProductGroupPriceCommandHandler(unitOfWork).Handle(new SetProductGroupPriceCommand(productId, groupId, 80m), CancellationToken.None);
        }

        // Misafir sepeti - CustomerGroupId verilmiyor -> taban fiyat.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("sepet-misafir", productId, 1), CancellationToken.None);
        }
        await using (var dbContext = CreateDbContext())
        {
            var cart = await new GetCartQueryHandler(new UnitOfWork(dbContext)).Handle(new GetCartQuery("sepet-misafir", "tr"), CancellationToken.None);
            Assert.Equal(100m, cart.Items.Single().UnitPriceTry);
        }

        // Kurumsal grup üyesi sepeti -> grup fiyatı.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("sepet-kurumsal", productId, 1, null, groupId), CancellationToken.None);
        }
        await using (var dbContext = CreateDbContext())
        {
            var cart = await new GetCartQueryHandler(new UnitOfWork(dbContext)).Handle(new GetCartQuery("sepet-kurumsal", "tr"), CancellationToken.None);
            Assert.Equal(80m, cart.Items.Single().UnitPriceTry);
        }

        // Farklı (fiyatı olmayan) bir gruba ait üye -> taban fiyat.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("sepet-bireysel", productId, 1, null, otherGroupId), CancellationToken.None);
        }
        await using (var dbContext = CreateDbContext())
        {
            var cart = await new GetCartQueryHandler(new UnitOfWork(dbContext)).Handle(new GetCartQuery("sepet-bireysel", "tr"), CancellationToken.None);
            Assert.Equal(100m, cart.Items.Single().UnitPriceTry);
        }
    }

    [Fact]
    public async Task VitrinListesiVeUrunDetayiGrupFiyatiniDogruGosterir()
    {
        Guid productId, groupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Kurumsal");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;

            productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
                "test-vitrin-grup-fiyat-urun", "GRP-4", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
                BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: "Vitrin Grup Ürünü", Description: null), CancellationToken.None);
            await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
            await new SetProductGroupPriceCommandHandler(unitOfWork).Handle(new SetProductGroupPriceCommand(productId, groupId, 80m), CancellationToken.None);
        }

        await using var dbContext2 = CreateDbContext();
        var uow = new UnitOfWork(dbContext2);

        var listAsGuest = await new GetStorefrontProductsQueryHandler(uow).Handle(new GetStorefrontProductsQuery("tr", null), CancellationToken.None);
        Assert.Equal(100m, listAsGuest.Items.Single(p => p.Id == productId).PriceTry);

        var listAsMember = await new GetStorefrontProductsQueryHandler(uow).Handle(new GetStorefrontProductsQuery("tr", null, CustomerGroupId: groupId), CancellationToken.None);
        Assert.Equal(80m, listAsMember.Items.Single(p => p.Id == productId).PriceTry);

        var detailAsGuest = await new GetProductBySlugQueryHandler(uow).Handle(new GetProductBySlugQuery("test-vitrin-grup-fiyat-urun", "tr"), CancellationToken.None);
        Assert.Equal(100m, detailAsGuest!.PriceTry);

        var detailAsMember = await new GetProductBySlugQueryHandler(uow).Handle(new GetProductBySlugQuery("test-vitrin-grup-fiyat-urun", "tr", groupId), CancellationToken.None);
        Assert.Equal(80m, detailAsMember!.PriceTry);
    }
}
