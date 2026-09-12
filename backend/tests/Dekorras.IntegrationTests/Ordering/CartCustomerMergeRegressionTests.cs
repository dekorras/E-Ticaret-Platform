using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Ordering;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Cart.CustomerId` Faz 0/1'den beri vardı ama
/// hiçbir yerden set edilmiyordu, yani bir müşterinin sepeti YALNIZCA o anki tarayıcı çerezine
/// bağlıydı ve başka bir cihazdan asla erişilemezdi. `MergeGuestCartIntoCustomerCommand` bunu
/// giriş/kayıt anında kapatır.</summary>
public sealed class CartCustomerMergeRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCartMergeTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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

    private static async Task<Guid> CreateProductAsync(Dekorras.Application.Common.Interfaces.IUnitOfWork unitOfWork, string slug, string code)
    {
        var productId = await new CreateProductCommandHandler(unitOfWork).Handle(new CreateProductCommand(
            slug, code, 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [], LanguageCode: "tr", Name: slug, Description: null), CancellationToken.None);
        await new SetProductPublishedCommandHandler(unitOfWork).Handle(new SetProductPublishedCommand(productId, Published: true), CancellationToken.None);
        return productId;
    }

    [Fact]
    public async Task MusterininIlkSepetiVarolanMisafirSepetineBaglanir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var group = new CustomerGroup("Bireysel");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var customer = new Customer("identity-merge-1", "Test Müşteri", "merge1@test.com", group.Id);
        await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var productId = await CreateProductAsync(unitOfWork, "test-cart-merge-1", "CM-1");
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-a-session", productId, 1), CancellationToken.None);

        await new MergeGuestCartIntoCustomerCommandHandler(unitOfWork).Handle(new MergeGuestCartIntoCustomerCommand("device-a-session", customer.Id), CancellationToken.None);

        var cart = await dbContext.Set<Cart>().SingleAsync(c => c.SessionKey == "device-a-session");
        Assert.Equal(customer.Id, cart.CustomerId);
    }

    [Fact]
    public async Task BaskaCihazdakiMusteriSepetiBuOturumaTasinir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var group = new CustomerGroup("Bireysel");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var customer = new Customer("identity-merge-2", "Test Müşteri 2", "merge2@test.com", group.Id);
        await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var productId = await CreateProductAsync(unitOfWork, "test-cart-merge-2", "CM-2");
        // Müşteri cihaz A'da giriş yapıp sepetine bir şey eklemiş (CustomerId zaten bağlı).
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-a-session-2", productId, 1, null, null, customer.Id), CancellationToken.None);

        // Şimdi cihaz B'de (hiç sepeti olmayan yeni bir tarayıcı) giriş yapıyor.
        await new MergeGuestCartIntoCustomerCommandHandler(unitOfWork).Handle(new MergeGuestCartIntoCustomerCommand("device-b-session-2", customer.Id), CancellationToken.None);

        var cartOnDeviceB = await dbContext.Set<Cart>().SingleOrDefaultAsync(c => c.SessionKey == "device-b-session-2");
        Assert.NotNull(cartOnDeviceB);
        Assert.Equal(customer.Id, cartOnDeviceB!.CustomerId);

        // Cihaz A'nın eski sepet satırı artık YOK (aynı sepet, anahtarı taşındı) - yinelenen sepet oluşmadı.
        var cartOnDeviceA = await dbContext.Set<Cart>().SingleOrDefaultAsync(c => c.SessionKey == "device-a-session-2");
        Assert.Null(cartOnDeviceA);

        var totalCartsForCustomer = await dbContext.Set<Cart>().CountAsync(c => c.CustomerId == customer.Id);
        Assert.Equal(1, totalCartsForCustomer);
    }

    [Fact]
    public async Task IkiCihazdakiSepetlerCakismayanUrunlerdeBirlestirilirCakisandaAktifKorunur()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var group = new CustomerGroup("Bireysel");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var customer = new Customer("identity-merge-3", "Test Müşteri 3", "merge3@test.com", group.Id);
        await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var sharedProductId = await CreateProductAsync(unitOfWork, "test-cart-merge-shared", "CM-3-SHARED");
        var onlyOnDeviceAId = await CreateProductAsync(unitOfWork, "test-cart-merge-a-only", "CM-3-A");
        var onlyOnDeviceBId = await CreateProductAsync(unitOfWork, "test-cart-merge-b-only", "CM-3-B");

        // Cihaz A: müşterinin ZATEN bağlı (başka bir gün eklenmiş) sepeti - ortak üründen 5 adet.
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-a-session-3", sharedProductId, 5, null, null, customer.Id), CancellationToken.None);
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-a-session-3", onlyOnDeviceAId, 1, null, null, customer.Id), CancellationToken.None);

        // Cihaz B: şimdi (misafir olarak) sepete ekleyip AKTİF oturumda ortak üründen 2 adet var.
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-b-session-3", sharedProductId, 2), CancellationToken.None);
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("device-b-session-3", onlyOnDeviceBId, 1), CancellationToken.None);

        // Cihaz B'de giriş yapılıyor - iki sepet birleştirilmeli.
        await new MergeGuestCartIntoCustomerCommandHandler(unitOfWork).Handle(new MergeGuestCartIntoCustomerCommand("device-b-session-3", customer.Id), CancellationToken.None);

        await using var checkContext = CreateDbContext();
        var mergedCart = await checkContext.Set<Cart>().Include(c => c.Items).SingleAsync(c => c.SessionKey == "device-b-session-3");
        Assert.Equal(3, mergedCart.Items.Count);
        // Ortak üründe AKTİF (cihaz B'nin) miktarı korunur, üzerine yazılmaz.
        Assert.Equal(2, mergedCart.Items.Single(i => i.ProductId == sharedProductId).Quantity);
        Assert.Contains(mergedCart.Items, i => i.ProductId == onlyOnDeviceAId);
        Assert.Contains(mergedCart.Items, i => i.ProductId == onlyOnDeviceBId);

        // Cihaz A'nın eski sepet satırı silinmiş olmalı.
        var oldCart = await checkContext.Set<Cart>().SingleOrDefaultAsync(c => c.SessionKey == "device-a-session-3");
        Assert.Null(oldCart);
    }

    [Fact]
    public async Task BaskaMusteriyeAitSepeteDokunulmaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var group = new CustomerGroup("Bireysel");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var customerA = new Customer("identity-merge-4a", "Müşteri A", "merge4a@test.com", group.Id);
        var customerB = new Customer("identity-merge-4b", "Müşteri B", "merge4b@test.com", group.Id);
        await unitOfWork.Repository<Customer>().AddAsync(customerA, CancellationToken.None);
        await unitOfWork.Repository<Customer>().AddAsync(customerB, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var productId = await CreateProductAsync(unitOfWork, "test-cart-merge-4", "CM-4");
        // Müşteri A çıkış yapmadan bu paylaşılan cihazı bırakıyor - sepeti hâlâ ona bağlı.
        await new AddCartItemCommandHandler(unitOfWork).Handle(new AddCartItemCommand("shared-device-session", productId, 1, null, null, customerA.Id), CancellationToken.None);

        // Müşteri B AYNI cihazda kendi hesabına giriş yapıyor.
        await new MergeGuestCartIntoCustomerCommandHandler(unitOfWork).Handle(new MergeGuestCartIntoCustomerCommand("shared-device-session", customerB.Id), CancellationToken.None);

        var cart = await dbContext.Set<Cart>().SingleAsync(c => c.SessionKey == "shared-device-session");
        // Sepet HÂLÂ Müşteri A'ya ait - B'ye devredilmedi (sepet çalınmadı).
        Assert.Equal(customerA.Id, cart.CustomerId);
    }
}
