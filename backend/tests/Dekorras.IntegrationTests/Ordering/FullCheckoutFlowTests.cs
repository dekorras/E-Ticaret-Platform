using Dekorras.Application;
using Dekorras.Application.Accounting.EventHandlers;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Ordering.Commands;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Notifications;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Ordering;

/// <summary>
/// Bu proje boyunca kurulan mimarinin uçtan uca (katalog → sepet → checkout → Provider Registry
/// üzerinden ödeme → sipariş durum makinesi → muhasebe otomasyonu) gerçekten birlikte çalıştığını
/// kanıtlayan kapsayıcı bir test. Ayrıca AddDataProtection().SetApplicationName("Dekorras") +
/// PersistKeysToFileSystem düzeltmesini de doğrular: sağlayıcı anahtarı burada şifrelenir, gerçek
/// Admin/Api/Storefront süreçleriyle AYNI fiziksel anahtar deposunu kullanarak.
/// </summary>
public sealed class FullCheckoutFlowTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasFullFlowTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
    private ServiceProvider _serviceProvider = default!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["ConnectionStrings:Redis"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistence(configuration);
        services.AddInfrastructure(configuration);
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureDeletedAsync();
        }
        await _serviceProvider.DisposeAsync();
    }

    [Fact]
    public async Task KatalogdanFaturaTasligina_TumZincir_DogruCalisir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1) Bireysel müşteri grubu seed edilmeli (PlaceOrderCommand bunu arıyor) - DbInitializer
        // burada çağrılmıyor, doğrudan gereken minimum veriyi ekliyoruz.
        var customerGroup = new Dekorras.Domain.Customers.CustomerGroup("Bireysel");
        dbContext.CustomerGroups.Add(customerGroup);
        await dbContext.SaveChangesAsync();

        // 2) Katalog: kategori + yayınlanmış bir ürün (CreateProductCommand üzerinden - gerçek CQRS akışı)
        var categoryId = await sender.Send(new CreateCategoryCommand("test-fullflow-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-fullflow-urun", "FF-001", 200m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        // 3) Provider Registry: Banka Havale/EFT VE Yurtiçi Kargo'yu admin panelindeki akışla
        // BİREBİR aynı komutla aktifleştir.
        var configurePaymentResult = await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        Assert.True(configurePaymentResult.Success);
        Assert.Equal(ProviderStatus.Active, configurePaymentResult.Status);

        var configureCargoResult = await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));
        Assert.True(configureCargoResult.Success);
        Assert.Equal(ProviderStatus.Active, configureCargoResult.Status);

        // 4) Sepete ekle (misafir alışverişi - üyelik yok)
        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 2));

        // 5) Checkout - PlaceOrderCommand: Customer/Address otomatik oluşturulur, Order + Payment yaratılır,
        // IPaymentGateway.AuthorizeAsync ve ICargoProvider.GetRateAsync BAŞKA bir "süreç"
        // simülasyonuyla değil ama aynı DI zinciriyle çağrılır.
        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Test Müşteri", "test@dekorras.com", "5551234567", "TR", "Kayseri", "Test Adres 1",
            "bank-transfer", "yurtici-kargo"));

        Assert.True(placeOrderResult.PaymentAuthorized);
        Assert.StartsWith("WEB-", placeOrderResult.OrderNumber);
        // YurticiKargoProvider: weightKg * 15 TRY; ağırlığı olmayan ürün için varsayılan 1kg x 2 adet = 2kg -> 30 TRY.
        Assert.Equal(30m, placeOrderResult.ShippingCostTry);

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(400m, order.SubTotalTry); // 2 x 200 TRY
        Assert.Equal(80m, order.TaxTotalTry); // %20 KDV
        Assert.Equal(30m, order.ShippingTotalTry);
        Assert.Equal(510m, order.GrandTotalTry);
        Assert.Equal(OrderStatus.PendingApproval, order.Status);

        // Sipariş kalemine müşterinin gördüğü ürün ADI yazılmalı, SKU/ürün kodu ("FF-001") değil -
        // aksi halde Admin sipariş detayı ve Storefront "Siparişlerim" sayfası ürün kodunu gösterirdi.
        var orderItem = await dbContext.Set<OrderItem>().FirstAsync(i => i.OrderId == order.Id);
        Assert.Equal("Test Ürün", orderItem.ProductName);

        var payment = await dbContext.Set<Dekorras.Domain.Payments.Payment>().FirstAsync(p => p.OrderId == order.Id);
        Assert.Equal(Dekorras.Domain.Payments.PaymentStatus.Authorized, payment.Status);

        // Sepet gerçekten boşaltıldı mı?
        var cartAfterCheckout = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Empty(cartAfterCheckout.Items);

        // 6) Sipariş durum makinesi üzerinden Tamamlandı'ya kadar ilerlet (kaynağı fark etmeksizin aynı kısıtlar).
        await sender.Send(new TransitionOrderStatusCommand(order.Id, OrderStatus.Preparing, null));
        await sender.Send(new TransitionOrderStatusCommand(order.Id, OrderStatus.Prepared, null));
        await sender.Send(new TransitionOrderStatusCommand(order.Id, OrderStatus.Shipped, null));
        await sender.Send(new TransitionOrderStatusCommand(order.Id, OrderStatus.Completed, null));

        // 7) Kabul kriteri: sipariş Tamamlandı'ya geçince Muhasebe modülünde otomatik fatura taslağı oluşmalı
        // (OrderCompletedEventHandler, DbContext.SaveChangesAsync sonrası IDomainEventDispatcher ile tetiklenir).
        var invoice = await dbContext.Set<Invoice>().FirstOrDefaultAsync(i => i.OrderId == order.Id);
        Assert.NotNull(invoice);
        Assert.Equal(InvoiceStatus.Draft, invoice!.Status);
        Assert.Equal(400m, invoice.SubTotalTry);
        Assert.Equal(80m, invoice.TaxTotalTry); // %20 KDV
    }

    [Fact]
    public async Task KayitliMusteri_IkinciSiparisiVerdiginde_YeniBirMusteriOlusturulmaz()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-musteri-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-musteri-urun", "MU-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 50,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // Gerçek kayıt akışı: AccountController.Register ile BİREBİR aynı adımlar.
        var identityUser = new IdentityUser { UserName = "musteri@dekorras.com", Email = "musteri@dekorras.com", EmailConfirmed = true };
        var createResult = await userManager.CreateAsync(identityUser, "Test123!*");
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));
        await sender.Send(new CreateCustomerProfileCommand(identityUser.Id, "Kayıtlı Müşteri", "musteri@dekorras.com"));

        var sessionKey1 = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey1, productId, Quantity: 1));
        var firstOrder = await sender.Send(new PlaceOrderCommand(
            sessionKey1, identityUser.Id, "Kayıtlı Müşteri", "musteri@dekorras.com", "5550001111",
            "TR", "Kayseri", "İlk Adres", "bank-transfer", "yurtici-kargo"));

        var sessionKey2 = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey2, productId, Quantity: 1));
        var secondOrder = await sender.Send(new PlaceOrderCommand(
            sessionKey2, identityUser.Id, "Kayıtlı Müşteri", "musteri@dekorras.com", "5550001111",
            "TR", "İstanbul", "İkinci Adres", "bank-transfer", "yurtici-kargo"));

        var customers = await dbContext.Set<Customer>().Where(c => c.IdentityUserId == identityUser.Id).ToListAsync();
        Assert.Single(customers); // iki sipariş, TEK müşteri kaydı

        var orders = await dbContext.Set<Order>().Where(o => o.CustomerId == customers[0].Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Contains(orders, o => o.Id == firstOrder.OrderId);
        Assert.Contains(orders, o => o.Id == secondOrder.OrderId);

        var addresses = await dbContext.Set<Address>().Where(a => a.CustomerId == customers[0].Id).ToListAsync();
        Assert.Equal(2, addresses.Count); // her siparişte girilen adres ayrı ayrı saklanır

        // Hesabım > Siparişlerim sayfasının kullandığı sorgu da doğru çalışmalı.
        var myOrders = await sender.Send(new GetMyOrdersQuery(identityUser.Id));
        Assert.Equal(2, myOrders.Count);
    }

    [Fact]
    public async Task KuponUygulanmisSepet_SiparisTutarindanDogruIndirimYapar_VeKullanimSayaciniArtirir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-kupon-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-kupon-urun", "KUPON-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // Admin akışıyla BİREBİR aynı komutla %10'luk bir kupon oluştur.
        var couponId = await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCouponCommand(
            "test10", DiscountType.Percentage, 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), UsageLimit: 5));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));

        var applyResult = await sender.Send(new ApplyCouponCommand(sessionKey, "test10")); // küçük harfle - normalize edilmeli
        Assert.True(applyResult.Success);

        var cartPreview = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal("TEST10", cartPreview.CouponCode);
        Assert.Equal(50m, cartPreview.DiscountTry); // 500 TRY'nin %10'u
        Assert.Equal(450m, cartPreview.GrandTotalTry);

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Kupon Müşterisi", "kupon@test.com", "5551112233",
            "TR", "Kayseri", "Kupon Test Adresi", "bank-transfer", "yurtici-kargo"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(500m, order.SubTotalTry);
        Assert.Equal(100m, order.TaxTotalTry); // KDV, İNDİRİM ÖNCESİ taban üzerinden hesaplanır
        Assert.Equal(50m, order.DiscountTotalTry);
        Assert.Equal("TEST10", order.CouponCode);
        // GrandTotal = SubTotal + Tax + Shipping - Discount = 500 + 100 + 15 - 50 = 565
        // (1 adet x varsayılan 1kg x 15 TRY/kg = 15 TRY kargo - YurticiKargoProvider)
        Assert.Equal(565m, order.GrandTotalTry);

        var coupon = await dbContext.Set<Coupon>().FirstAsync(c => c.Id == couponId);
        Assert.Equal(1, coupon.UsageCount);
    }

    [Fact]
    public async Task VaryantSecilmisSepet_FiyatFarkiniDogruUygularVeSiparisKalemineVaryantiKaydeder()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-varyant-checkout-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-varyant-checkout-urun", "VARCHK-001", 300m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));
        var variantId = await sender.Send(new AddProductVariantCommand(productId, "VARCHK-001-XL", "Ölçü: 100x100", 50m, 5));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1, VariantId: variantId));

        // Sepet önizlemesi taban fiyat + varyant farkını (300 + 50 = 350) yansıtmalı.
        var cartPreview = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        var cartItem = Assert.Single(cartPreview.Items);
        Assert.Equal(variantId, cartItem.VariantId);
        Assert.Equal("Ölçü: 100x100", cartItem.VariantOptionName);
        Assert.Equal(350m, cartItem.UnitPriceTry);

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Varyant Müşterisi", "varyant@test.com", "5551112233",
            "TR", "Kayseri", "Varyant Test Adresi", "bank-transfer", "yurtici-kargo"));

        var orderItem = await dbContext.Set<OrderItem>().FirstAsync(i => i.OrderId == placeOrderResult.OrderId);
        Assert.Equal(variantId, orderItem.VariantId);
        Assert.Equal(350m, orderItem.UnitPriceTry);
        Assert.Contains("Ölçü: 100x100", orderItem.ProductName);

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(350m, order.SubTotalTry); // 1 adet x (300 + 50 varyant farkı)

        // Varyantın stoku düşülmeli (5 - 1 = 4), taban ürünün stoku ETKİLENMEMELİ (varyantlı
        // bir üründe stok varyant bazında takip edilir).
        var variant = await dbContext.Set<ProductVariant>().FirstAsync(v => v.Id == variantId);
        Assert.Equal(4, variant.StockQuantity);
        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public async Task BasariliSiparis_TabanUrununStokunuDogruDuser()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-stok-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-stok-urun", "STOK-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 3));
        await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Stok Müşterisi", "stok@test.com", "5551112233",
            "TR", "Kayseri", "Stok Test Adresi", "bank-transfer", "yurtici-kargo"));

        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal(2, product.StockQuantity); // 5 - 3
        Assert.Equal(StockAvailability.InStock, product.StockAvailability);
    }

    [Fact]
    public async Task StokYetersizIse_SiparisReddedilirVeStokDegismez()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-stokyok-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-stokyok-urun", "STOKYOK-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 2,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // Sepete stoktan FAZLA (5) ekleniyor - AddCartItemCommand şu an sepet aşamasında stok
        // doğrulamıyor (yalnızca checkout ANINDA doğrulanır, bkz. PlaceOrderCommand), bu yüzden
        // sepete ekleme başarılı olur ama checkout reddedilmelidir.
        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Stok Müşterisi", "stokyok@test.com", "5551112233",
            "TR", "Kayseri", "Stok Test Adresi", "bank-transfer", "yurtici-kargo")));

        // Reddedilen sipariş HİÇBİR kalıcı iz bırakmamalı: stok değişmemiş, sipariş oluşmamış olmalı.
        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal(2, product.StockQuantity);
        Assert.Empty(await dbContext.Set<Order>().ToListAsync());
    }

    [Fact]
    public async Task TopluAlimKademesi_EsigiAsanAdette_KademeFiyatiniUygularVeAdetDusunceEskiFiyataDoner()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-toplu-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-toplu-urun", "TOPLU-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 100,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        // 10 adet ve üzeri: birim fiyat 100 TRY'den 80 TRY'ye düşer.
        await sender.Send(new AddProductQuantityDiscountCommand(productId, MinimumQuantity: 10, PriceTry: 80m));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");

        // 5 adet - kademe eşiğinin ALTINDA, taban fiyat (100) geçerli olmalı.
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 5));
        var cartBelowTier = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal(100m, Assert.Single(cartBelowTier.Items).UnitPriceTry);

        // 12 adete çıkınca (aynı satır güncelleniyor) kademe fiyatı (80) devreye girmeli - bu,
        // Cart.AddOrUpdateItem'ın adet DEĞİŞTİĞİNDE birim fiyatı da yeniden hesaplaması gerektiğini
        // kanıtlayan asıl regresyon senaryosu.
        await sender.Send(new UpdateCartItemQuantityCommand(sessionKey, productId, Quantity: 12));
        var cartAboveTier = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal(80m, Assert.Single(cartAboveTier.Items).UnitPriceTry);

        // 8 adete geri düşünce kademe eşiğinin altına inilir - fiyat taban fiyata GERİ dönmeli.
        await sender.Send(new UpdateCartItemQuantityCommand(sessionKey, productId, Quantity: 8));
        var cartBackBelowTier = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal(100m, Assert.Single(cartBackBelowTier.Items).UnitPriceTry);

        // 15 adetle checkout - sipariş kalemine checkout ANINDA yeniden hesaplanan kademe fiyatı (80) yazılmalı.
        await sender.Send(new UpdateCartItemQuantityCommand(sessionKey, productId, Quantity: 15));
        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Toplu Alım Müşterisi", "toplu@test.com", "5551112233",
            "TR", "Kayseri", "Toplu Test Adresi", "bank-transfer", "yurtici-kargo"));

        var orderItem = await dbContext.Set<OrderItem>().FirstAsync(i => i.OrderId == placeOrderResult.OrderId);
        Assert.Equal(80m, orderItem.UnitPriceTry);
        Assert.Equal(15, orderItem.Quantity);

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(1200m, order.SubTotalTry); // 15 x 80
    }

    [Fact]
    public async Task KosuluSaglayanSepete_EnYuksekIndirimliKampanyaOtomatikUygulanirVeKullanimSayaciniArtirir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-kampanya-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-kampanya-urun", "KAMP-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // İki geçerli kampanya: Sepet 500 TRY olduğunda ikisi de MinCartTotal koşulunu sağlar,
        // en yüksek indirimi veren (%15 = 75 TRY) seçilmelidir, %5'lik (25 TRY) değil.
        await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCampaignCommand(
            "Düşük İndirim", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30),
            DiscountType.Percentage, 5m, UsageLimit: null, MinCartTotalTry: 100m));
        var bestCampaignId = await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCampaignCommand(
            "Yüksek İndirim", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30),
            DiscountType.Percentage, 15m, UsageLimit: 5, MinCartTotalTry: 100m));
        // Koşulu sağlamayan (min sepet 10.000 TRY) üçüncü bir kampanya - ASLA seçilmemeli.
        await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCampaignCommand(
            "Ulaşılamaz Kampanya", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30),
            DiscountType.Percentage, 90m, UsageLimit: null, MinCartTotalTry: 10000m));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Kampanya Müşterisi", "kampanya@test.com", "5551112233",
            "TR", "Kayseri", "Kampanya Test Adresi", "bank-transfer", "yurtici-kargo"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(500m, order.SubTotalTry);
        Assert.Equal(75m, order.DiscountTotalTry); // 500 TRY'nin %15'i - en iyi kampanya
        Assert.Equal(bestCampaignId, order.CampaignId);
        Assert.Null(order.CouponCode);

        var bestCampaign = await dbContext.Set<Dekorras.Domain.Marketing.Campaign>().FirstAsync(c => c.Id == bestCampaignId);
        Assert.Equal(1, bestCampaign.UsageCount);
    }

    [Fact]
    public async Task KuponUygulanmisSepette_UygunKampanyaOlsaBileAtlanir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-kamp-kupon-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-kamp-kupon-urun", "KAMPKUP-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // Sepet koşulunu fazlasıyla sağlayan yüksek indirimli bir kampanya var - ama kupon ZATEN
        // uygulandığı için PlaceOrderCommand kampanyayı hiç değerlendirmemeli (bkz. Campaign belgesi).
        var campaignId = await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCampaignCommand(
            "Atlanması Gereken Kampanya", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30),
            DiscountType.Percentage, 50m, UsageLimit: null, MinCartTotalTry: 100m));

        var couponId = await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCouponCommand(
            "kampkup10", DiscountType.Percentage, 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), UsageLimit: 5));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));
        await sender.Send(new ApplyCouponCommand(sessionKey, "kampkup10"));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Kupon Öncelikli Müşteri", "kampkup@test.com", "5551112233",
            "TR", "Kayseri", "Kampanya Kupon Test Adresi", "bank-transfer", "yurtici-kargo"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(50m, order.DiscountTotalTry); // 500 TRY'nin %10'u - kupon, kampanya (%50) DEĞİL
        Assert.Equal("KAMPKUP10", order.CouponCode);
        Assert.Null(order.CampaignId);

        var campaign = await dbContext.Set<Dekorras.Domain.Marketing.Campaign>().FirstAsync(c => c.Id == campaignId);
        Assert.Equal(0, campaign.UsageCount); // hiç değerlendirilmediği için kullanım sayacı artmamalı

        var coupon = await dbContext.Set<Coupon>().FirstAsync(c => c.Id == couponId);
        Assert.Equal(1, coupon.UsageCount);
    }

    [Fact]
    public async Task SepetOnizlemesi_UygunKampanyaVarsaCheckoutTamamlanmadanIndirimiOnizlerVeKuponVarsaGostermez()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-kamp-onizleme-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-kamp-onizleme-urun", "KAMPONIZ-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCampaignCommand(
            "Önizleme Kampanyası", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30),
            DiscountType.Percentage, 10m, UsageLimit: null, MinCartTotalTry: 100m));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));

        // Kupon YOK - sepet önizlemesi checkout'a hiç gidilmeden kampanyayı göstermeli.
        var cartPreview = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal("Önizleme Kampanyası", cartPreview.CampaignName);
        Assert.Equal(50m, cartPreview.DiscountTry); // 500 TRY'nin %10'u
        Assert.Equal(450m, cartPreview.GrandTotalTry);

        // Bir kupon uygulanınca önizleme kampanyayı GÖSTERMEMELİ (mutually exclusive - bkz. Campaign belgesi).
        await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCouponCommand(
            "kamponiz10", DiscountType.Percentage, 5m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), UsageLimit: 5));
        await sender.Send(new ApplyCouponCommand(sessionKey, "kamponiz10"));

        var cartPreviewWithCoupon = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Null(cartPreviewWithCoupon.CampaignName);
        Assert.Equal("KAMPONIZ10", cartPreviewWithCoupon.CouponCode);
        Assert.Equal(25m, cartPreviewWithCoupon.DiscountTry); // 500 TRY'nin %5'i - kampanya DEĞİL
    }

    [Fact]
    public async Task UrunAgirligiGirilmisse_KargoUcretiGercekAgirlikUzerindenHesaplanir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-agirlik-kategori", null, 0, "tr", "Test Kategori", null));
        // 3 kg'lık bir ürün - Admin'deki "Ağırlık (kg)" alanına GİRİLMİŞ, varsayılan 1kg KULLANILMAMALI.
        var productId = await sender.Send(new CreateProductCommand(
            "test-agirlik-urun", "AGIRLIK-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null,
            WeightKg: 3m));

        var product = await dbContext.Set<Product>().FirstAsync(p => p.Id == productId);
        Assert.Equal(3m, product.Weight);
        Assert.Equal("kg", product.WeightUnit);

        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 2));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Ağırlık Müşterisi", "agirlik@test.com", "5551112233",
            "TR", "Kayseri", "Ağırlık Test Adresi", "bank-transfer", "yurtici-kargo"));

        // YurticiKargoProvider: weightKg * 15 TRY; 2 adet x 3kg = 6kg -> 90 TRY (varsayılan 1kg
        // kullanılsaydı 2 x 1kg x 15 = 30 TRY olurdu - bu, ağırlığın GERÇEKTEN okunduğunu kanıtlar).
        Assert.Equal(90m, placeOrderResult.ShippingCostTry);
    }

    [Fact]
    public async Task HediyeCekiKuponVeKampanyaIleBirlikteUygulanabilirVeKalanBakiyeSonrakiSiparisteKullanilir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-hediyecek-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-hediyecek-urun", "HEDIYECEK-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // %10'luk bir kupon VE 100 TRY'lik bir hediye çeki - hediye çeki bir İNDİRİM değil bir
        // ÖDEME yöntemi olduğu için kupon ile BİRLİKTE kullanılabilmesi asıl regresyon senaryosu
        // (Campaign'in kuponla karşılıklı dışlanmasının AKSİNE).
        var couponId = await sender.Send(new Dekorras.Application.Ordering.Commands.CreateCouponCommand(
            "hediyecek10", DiscountType.Percentage, 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30), UsageLimit: 5));
        var voucherId = await sender.Send(new CreateGiftVoucherCommand("hediyecek100", 100m));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));
        await sender.Send(new ApplyCouponCommand(sessionKey, "hediyecek10"));
        await sender.Send(new ApplyGiftVoucherCommand(sessionKey, "hediyecek100"));

        // Sepet önizlemesi: 500 - %10 kupon (50) = 450, sonra 100 TRY hediye çeki düşülür = 350.
        var cartPreview = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal("HEDIYECEK10", cartPreview.CouponCode);
        Assert.Equal("HEDIYECEK100", cartPreview.GiftVoucherCode);
        Assert.Equal(100m, cartPreview.GiftVoucherAmountAppliedTry);
        Assert.Equal(350m, cartPreview.GrandTotalTry);

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Hediye Çeki Müşterisi", "hediyecek@test.com", "5551112233",
            "TR", "Kayseri", "Hediye Çeki Test Adresi", "bank-transfer", "yurtici-kargo"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        // 500 taban + 100 KDV (%20, indirim ÖNCESİ taban üzerinden) + 15 kargo - 50 kupon - 100 hediye çeki = 465.
        Assert.Equal(50m, order.DiscountTotalTry);
        Assert.Equal("HEDIYECEK10", order.CouponCode);
        Assert.Equal("HEDIYECEK100", order.GiftVoucherCode);
        Assert.Equal(100m, order.GiftVoucherAmountAppliedTry);
        Assert.Equal(465m, order.GrandTotalTry);

        var voucherAfterFirstOrder = await dbContext.Set<GiftVoucher>().FirstAsync(v => v.Id == voucherId);
        Assert.Equal(0m, voucherAfterFirstOrder.RemainingBalanceTry);

        var coupon = await dbContext.Set<Coupon>().FirstAsync(c => c.Id == couponId);
        Assert.Equal(1, coupon.UsageCount);
    }

    [Fact]
    public async Task HediyeCekiSepetTutarindanBuyukse_YalnizcaKalanBakiyeKadariDusulurVeGercekOdemeKalir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-hediyecek2-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-hediyecek2-urun", "HEDIYECEK2-001", 50m, 0m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        // 200 TRY'lik bir çek, yalnızca 65 TRY'lik (50 taban + 0 KDV + 15 kargo) bir siparişe
        // uygulanıyor - bakiyenin TAMAMI değil yalnızca SİPARİŞ TUTARI KADARI düşülmeli, kalan
        // 135 TRY bir SONRAKİ siparişte kullanılabilir kalmalı.
        var voucherId = await sender.Send(new CreateGiftVoucherCommand("buyukcek200", 200m));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));
        await sender.Send(new ApplyGiftVoucherCommand(sessionKey, "buyukcek200"));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Büyük Çek Müşterisi", "buyukcek@test.com", "5551112233",
            "TR", "Kayseri", "Büyük Çek Test Adresi", "bank-transfer", "yurtici-kargo"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(65m, order.GiftVoucherAmountAppliedTry); // bakiyenin TAMAMI (200) DEĞİL
        Assert.Equal(0m, order.GrandTotalTry);

        var voucher = await dbContext.Set<GiftVoucher>().FirstAsync(v => v.Id == voucherId);
        Assert.Equal(135m, voucher.RemainingBalanceTry); // 200 - 65 = 135, sonraki siparişte kullanılabilir
    }

    [Fact]
    public async Task IadeEdilenSiparis_OdemeSaglayicisinaGercekIadeIstegiGondererekPaymentiRefundedYapar()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-iade-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-iade-urun", "IADE-001", 400m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));
        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "İade Müşterisi", "iade@test.com", "5551112233",
            "TR", "Kayseri", "İade Test Adresi", "bank-transfer", "yurtici-kargo"));

        // Sipariş durum makinesi: Refunded yalnızca Shipped/Completed'ten erişilebilir.
        await sender.Send(new TransitionOrderStatusCommand(placeOrderResult.OrderId, OrderStatus.Preparing, null));
        await sender.Send(new TransitionOrderStatusCommand(placeOrderResult.OrderId, OrderStatus.Prepared, null));
        await sender.Send(new TransitionOrderStatusCommand(placeOrderResult.OrderId, OrderStatus.Shipped, null));

        // RefundOrderCommand'dan ÖNCE: yalnızca durum etiketi değişip GERÇEK bir iade işlemi hiç
        // tetiklenmediği senaryoyu (eski TransitionOrderStatusCommand davranışı) yanlışlıkla tekrar
        // ETMEDİĞİMİZİ kanıtlamak için Payment hâlâ Authorized olmalı.
        var paymentBeforeRefund = await dbContext.Set<Payment>().FirstAsync(p => p.OrderId == placeOrderResult.OrderId);
        Assert.Equal(PaymentStatus.Authorized, paymentBeforeRefund.Status);

        await sender.Send(new RefundOrderCommand(placeOrderResult.OrderId, "Müşteri talebi"));

        var order = await dbContext.Set<Order>().FirstAsync(o => o.Id == placeOrderResult.OrderId);
        Assert.Equal(OrderStatus.Refunded, order.Status);

        var payment = await dbContext.Set<Payment>().FirstAsync(p => p.OrderId == placeOrderResult.OrderId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        var transactions = await dbContext.Set<Transaction>().Where(t => t.PaymentId == payment.Id).ToListAsync();
        Assert.Contains(transactions, t => t.Type == TransactionType.Refund && t.IsSuccess);

        // Zaten iade edilmiş bir ödeme TEKRAR iade edilmeye çalışılamaz.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new RefundOrderCommand(placeOrderResult.OrderId, null)));
    }

    [Fact]
    public async Task SiparisTamamlaninaCaSiparisOnayEPostasiGondermeDenemesiKaydedilir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-eposta-kategori", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-eposta-urun", "EPOSTA-001", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 5,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null));

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));
        await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "E-posta Müşterisi", "eposta-musteri@test.com", "5551112233",
            "TR", "Kayseri", "E-posta Test Adresi", "bank-transfer", "yurtici-kargo"));

        // `IEmailSender`/`NotificationLog` Faz 0/1'den beri hazırdı ama PlaceOrderCommand'dan HİÇ
        // çağrılmıyordu - müşteri hiçbir zaman bir sipariş onayı e-postası ALMIYORDU. Artık her
        // checkout bir NotificationLog kaydı bırakmalı (gerçek SMTP olmasa bile - stub sender asla
        // hata fırlatmaz, bu yüzden Success=true beklenir).
        var log = await dbContext.Set<NotificationLog>().FirstOrDefaultAsync(l => l.Recipient == "eposta-musteri@test.com");
        Assert.NotNull(log);
        Assert.Equal(NotificationChannel.Email, log!.Channel);
        Assert.Equal("OrderConfirmation", log.TemplateKey);
        Assert.True(log.Success);
    }
}
