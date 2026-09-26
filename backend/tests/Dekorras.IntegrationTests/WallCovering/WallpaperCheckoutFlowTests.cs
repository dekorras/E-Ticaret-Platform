using System.Text.Json;
using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Ordering.Commands;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.SystemAdmin;
using Dekorras.Domain.WallCovering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.WallCovering;

/// <summary>Gerçek SQL Server'a karşı: ölçüye özel duvar kağıdı teklif → sepete ekle → checkout
/// zinciri. Fiyatın sunucuda (checkout ANINDA güncel malzeme fiyatıyla) yeniden hesaplandığını,
/// aynı konfigürasyonun tek satırda birleştiğini, ücretsiz tutkal/kargo eşiklerini, kırılımın
/// siparişe dondurulduğunu ve kuponun kişi başı limitini doğrular.</summary>
public sealed class WallpaperCheckoutFlowTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWallpaperFlowTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
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
        await dbContext.Database.EnsureDeletedAsync();
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

    private sealed record Fixture(Guid ProductId, Guid GlueProductId);

    private static async Task<Fixture> ArrangeCatalogAsync(ISender sender, ApplicationDbContext dbContext)
    {
        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();
        await WallCoveringSeed.SeedAsync(dbContext);

        var wallpaperCategory = await sender.Send(new CreateCategoryCommand("duvar-kagitlari-129", null, 0, "tr", "Duvar Kağıtları", null));
        var childCategory = await sender.Send(new CreateCategoryCommand("orman-posterleri", wallpaperCategory, 0, "tr", "Orman", null));
        var accessoryCategory = await sender.Send(new CreateCategoryCommand("aksesuar", null, 0, "tr", "Aksesuar", null));

        var productId = await sender.Send(new CreateProductCommand(
            "orman-manzarasi", "WP-001", 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
            BrandId: null, CategoryIds: [childCategory], LanguageCode: "tr", Name: "Orman Manzarası", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        var glueProductId = await sender.Send(new CreateProductCommand(
            "duvar-kagidi-tutkali", "GLUE-01", 150m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [accessoryCategory], LanguageCode: "tr", Name: "Duvar Kağıdı Tutkalı", Description: null));
        await sender.Send(new SetProductPublishedCommand(glueProductId, Published: true));

        foreach (var material in await dbContext.Materials.Where(m => m.RequiresGlue).ToListAsync())
            material.LinkProducts(null, glueProductId);
        await dbContext.SaveChangesAsync();

        // Alt kategorideki ürün de profillenmeli; aksesuar kategorisindeki tutkal profillenmemeli.
        Assert.Equal(1, await sender.Send(new EnsureWallpaperProfilesCommand()));
        Assert.Equal(0, await sender.Send(new EnsureWallpaperProfilesCommand())); // idempotent

        Assert.True((await sender.Send(new ConfigureIntegrationProviderCommand("bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" }, null))).Success);
        Assert.True((await sender.Send(new ConfigureIntegrationProviderCommand("yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" }, null))).Success);

        return new Fixture(productId, glueProductId);
    }

    [Fact]
    public async Task TeklifSepetCheckout_FiyatSunucudaYenidenHesaplanir_KirilimDondurulur_TutkalVeKargoEsikleriUygulanir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var f = await ArrangeCatalogAsync(sender, dbContext);
        var sessionKey = Guid.NewGuid().ToString("N");

        // 1) Teklif: 400×200 Dokusuz → 405×205 = 8,3025 m² × 699 = 5803,45; 5 panel.
        var plain = new WallConfiguration(400m, 200m, "plain");
        var quote = await sender.Send(new QuoteWallpaperQuery(f.ProductId, plain, 1, sessionKey));
        Assert.True(quote.Gecerli);
        Assert.Equal(8.3025m, quote.FaturaM2);
        Assert.Equal(5, quote.PanelSayisi);
        Assert.Equal(5803.45m, quote.BirimFiyat);
        Assert.Equal(1160.69m, quote.Kdv);
        Assert.True(quote.TutkalGerekir);
        Assert.True(quote.TutkalUcretsiz); // 5803 ≥ 1000
        Assert.Equal(0m, quote.Tutkal);
        Assert.Null(quote.UcretsizKargoIcinKalan); // eşik varsayılan kapalı

        var invalid = await sender.Send(new QuoteWallpaperQuery(f.ProductId, new WallConfiguration(5m, 200m, "plain"), 1, sessionKey));
        Assert.False(invalid.Gecerli);
        Assert.Contains("width", invalid.Hatalar.Keys);

        var notConfigurable = await sender.Send(new QuoteWallpaperQuery(f.GlueProductId, plain, 1, sessionKey));
        Assert.False(notConfigurable.Gecerli);

        // Geçersiz konfigürasyon sepete eklenemez.
        await Assert.ThrowsAsync<WallConfigurationException>(() =>
            sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, new WallConfiguration(400m, 331m, "plain"), 1)));

        // 2) Aynı konfigürasyon iki kez → tek satır ×2; m ile girilen farklı malzeme → ayrı satır.
        await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, plain, 1));
        await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, new WallConfiguration(400m, 200m, "plain", LengthUnit.M), 1));
        await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId,
            new WallConfiguration(WallDimensions.ToCm(3m, LengthUnit.M), WallDimensions.ToCm(2.5m, LengthUnit.M), "textured", LengthUnit.M, mirror: true, filter: ImageFilter.Grayscale), 1));
        await sender.Send(new AddCartItemCommand(sessionKey, f.GlueProductId, Quantity: 3));

        var cart = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal(3, cart.Items.Count);
        var plainLine = cart.Items.Single(i => i.Configuration?.MaterialCode == "plain");
        Assert.Equal(2, plainLine.Quantity);
        Assert.Equal("Dokusuz", plainLine.Configuration!.MaterialName);
        Assert.Equal(5, plainLine.Configuration.PanelCount);
        Assert.Equal(450m, cart.GlueDiscountTry); // 3 tutkal gerektiren adet → 3 tutkal ücretsiz
        Assert.Empty(cart.GlueSuggestions!); // tutkal zaten sepette

        // 3) Sepetteki fiyat checkout'ta kullanılmaz: malzeme fiyatı değişir, sepet satırı kurcalanır.
        var plainMaterial = await dbContext.Materials.SingleAsync(m => m.Code == "plain");
        plainMaterial.Update(plainMaterial.Name, null, null, 750m, plainMaterial.PanelWidthCm, plainMaterial.MaxHeightCm, plainMaterial.WeightGsm,
            plainMaterial.FireRating, plainMaterial.IsSelfAdhesive, plainMaterial.RequiresGlue, plainMaterial.BleedCm, plainMaterial.MinBillableAreaM2, plainMaterial.SortOrder);
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE CartItem SET UnitPriceTry = 1 WHERE ConfigHash IS NOT NULL");

        // Ücretsiz kargo eşiği admin ayarıyla açılır.
        dbContext.Settings.Add(new Setting(WallCoveringSettingKeys.FreeShippingThresholdTry, "5000"));
        await dbContext.SaveChangesAsync();

        var result = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Duvar Müşterisi", "duvar@dekorras.com", "5551234567", "TR", "Kayseri", "Adres 1",
            "bank-transfer", "yurtici-kargo"));

        Assert.Equal(0m, result.ShippingCostTry);

        dbContext.ChangeTracker.Clear();
        var order = await dbContext.Orders.SingleAsync(o => o.Id == result.OrderId);
        var items = await dbContext.Set<OrderItem>().Where(i => i.OrderId == order.Id).ToListAsync();

        // 400×200 Dokusuz yeni fiyatla: 8,3025 × 750 = 6226,875 → 6226,88; 300×250 Dokulu: 7,7775 × 849 = 6603,10
        var plainItem = items.Single(i => i.ConfigurationJson != null && i.ConfigurationJson.Contains("\"plain\""));
        Assert.Equal(6226.88m, plainItem.UnitPriceTry);
        Assert.Equal(2, plainItem.Quantity);
        Assert.Contains("400×200 cm", plainItem.ProductName);
        var frozen = JsonSerializer.Deserialize<WallpaperLinePrice>(plainItem.PricingSnapshotJson!)!;
        Assert.Equal("Dokusuz", frozen.MaterialName);
        Assert.Equal(750m, frozen.UnitPricePerM2);
        Assert.Equal(8.3025m, frozen.BilledAreaM2);
        Assert.Equal(5, frozen.PanelCount);
        Assert.Equal(12453.76m, frozen.LineTotal);

        var texturedItem = items.Single(i => i.ConfigurationJson != null && i.ConfigurationJson.Contains("\"textured\""));
        Assert.Equal(6603.10m, texturedItem.UnitPriceTry);
        Assert.True(WallConfiguration.FromJson(texturedItem.ConfigurationJson)!.Mirror);

        var glueItem = items.Single(i => i.ProductId == f.GlueProductId);
        Assert.Equal(0m, glueItem.UnitPriceTry);
        Assert.Equal(3, glueItem.Quantity);
        Assert.Contains("ücretsiz", glueItem.ProductName);

        Assert.Equal(12453.76m + 6603.10m, order.SubTotalTry);
        Assert.Equal(order.SubTotalTry * 0.20m, order.TaxTotalTry);
        Assert.Equal(0m, order.ShippingTotalTry);

        // Fiziksel ürün (tutkal) stoktan düşer; konfigüre ürün stok takibi yapmaz.
        Assert.Equal(7, (await dbContext.Products.SingleAsync(p => p.Id == f.GlueProductId)).StockQuantity);
    }

    [Fact]
    public async Task EskiOlcusuzDuvarKagidiSatiri_ToplamaKatilmaz_SipariseDonusturulemez()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var f = await ArrangeCatalogAsync(sender, dbContext);
        var sessionKey = Guid.NewGuid().ToString("N");

        // Konfigüratör öncesi sepette kalmış satır: ölçüsüz, eski sabit fiyat (ör. 840 ₺). Artık düz "Sepete Ekle"
        // bunu üretemez; veritabanında doğrudan oluşturulur.
        var cart = new Cart(sessionKey);
        cart.AddOrUpdateItem(f.ProductId, null, 1, 840m);
        dbContext.Carts.Add(cart);
        await dbContext.SaveChangesAsync();
        await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, new WallConfiguration(400m, 250m, "plain"), 1));

        var dto = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        var legacy = Assert.Single(dto.Items, i => i.Configuration is null);
        Assert.True(legacy.RequiresConfiguration);
        var configured = Assert.Single(dto.Items, i => i.Configuration is not null);
        Assert.False(configured.RequiresConfiguration);
        Assert.Equal(configured.LineTotalTry, dto.SubTotalTry); // 840 ₺'lik eski satır toplama katılmaz

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new PlaceOrderCommand(
            sessionKey, null, "Ayşe", "eski-satir@dekorras.com", "5551234567", "TR", "Kayseri", "Adres", "bank-transfer", "yurtici-kargo")));
        Assert.Contains("ölçüsü seçilmemiş", ex.Message);
        Assert.Contains("Orman Manzarası", ex.Message);
        Assert.False(await dbContext.Orders.AnyAsync(o => o.CustomerId == dbContext.Customers.Where(c => c.Email == "eski-satir@dekorras.com").Select(c => c.Id).FirstOrDefault()));
    }

    [Fact]
    public async Task TutkalEsiginAltinda_TutkalUcretliKalir_VeSepetOneriUretir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var f = await ArrangeCatalogAsync(sender, dbContext);
        var sessionKey = Guid.NewGuid().ToString("N");

        // 50×50 → 1 m² minimum × 699 = 699 < 1.000 eşik
        await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, new WallConfiguration(50m, 50m, "plain"), 1));

        var cart = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        var suggestion = Assert.Single(cart.GlueSuggestions!);
        Assert.Equal(f.GlueProductId, suggestion.GlueProductId);
        Assert.False(suggestion.WillBeFree);

        await sender.Send(new AddCartItemCommand(sessionKey, f.GlueProductId, Quantity: 1));
        cart = await sender.Send(new GetCartQuery(sessionKey, "tr"));
        Assert.Equal(0m, cart.GlueDiscountTry);

        var result = await sender.Send(new PlaceOrderCommand(
            sessionKey, null, "Ali Veli", "ali@dekorras.com", "5551234567", "TR", "Kayseri", "Adres", "bank-transfer", "yurtici-kargo"));
        var glueItem = await dbContext.Set<OrderItem>().SingleAsync(i => i.OrderId == result.OrderId && i.ProductId == f.GlueProductId);
        Assert.Equal(150m, glueItem.UnitPriceTry);
        Assert.True(result.ShippingCostTry > 0); // ücretsiz kargo eşiği kapalı
    }

    [Fact]
    public async Task SepetSatiri_KimlikleGuncellenirVeSilinir_KuponKisiBasiLimitiUygulanir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var f = await ArrangeCatalogAsync(sender, dbContext);

        var coupon = new Coupon("TEKSEFER", DiscountType.Percentage, 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        coupon.SetPerUserLimit(1);
        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync();

        async Task<Order> PlaceAsync()
        {
            var sessionKey = Guid.NewGuid().ToString("N");
            var added = await sender.Send(new AddConfiguredCartItemCommand(sessionKey, f.ProductId, new WallConfiguration(100m, 100m, "plain"), 1));
            await sender.Send(new UpdateCartItemQuantityByIdCommand(sessionKey, added.CartItemId, 2));
            await sender.Send(new ApplyCouponCommand(sessionKey, "TEKSEFER"));
            var r = await sender.Send(new PlaceOrderCommand(sessionKey, null, "Ayşe", "ayse@dekorras.com", "5551234567", "TR", "Kayseri", "Adres", "bank-transfer", "yurtici-kargo"));
            dbContext.ChangeTracker.Clear();
            return await dbContext.Orders.SingleAsync(o => o.Id == r.OrderId);
        }

        var first = await PlaceAsync();
        Assert.Equal("TEKSEFER", first.CouponCode);
        Assert.Equal(first.SubTotalTry * 0.10m, first.DiscountTotalTry);
        Assert.Equal(2, (await dbContext.Set<OrderItem>().SingleAsync(i => i.OrderId == first.Id)).Quantity);

        var second = await PlaceAsync();
        Assert.Null(second.CouponCode); // aynı e-postayla ikinci kullanım reddedildi
        Assert.Equal(0m, second.DiscountTotalTry);

        // Kimlikle silme: yalnızca o satır gider.
        var session = Guid.NewGuid().ToString("N");
        var a = await sender.Send(new AddConfiguredCartItemCommand(session, f.ProductId, new WallConfiguration(100m, 100m, "plain"), 1));
        await sender.Send(new AddConfiguredCartItemCommand(session, f.ProductId, new WallConfiguration(200m, 100m, "plain"), 1));
        await sender.Send(new RemoveCartItemByIdCommand(session, a.CartItemId));
        var cart = await sender.Send(new GetCartQuery(session, "tr"));
        Assert.Equal(200m, Assert.Single(cart.Items).Configuration!.WidthCm);
    }
}
