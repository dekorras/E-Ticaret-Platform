using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Ordering.Commands;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Shipping;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.Ordering;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Shipping.ShippingMethod`/`ShippingRate`/
/// `ShipmentTracking` `ICargoProvider`'ın GetRateAsync/CreateShipmentAsync/GetTrackingStatusAsync'iyle
/// işlevsel olarak ÇAKIŞTIĞI için kodtan tamamen kaldırıldı (redundant, muhtemelen Provider Registry
/// deseni benimsenmeden ÖNCEKİ bir tasarım kalıntısı). `CustomsDeclaration` ise GERÇEKTEN farklı bir
/// kavram (bkz. plan §7 - HS/GTİP kodu) ve bu turda gerçek bir Application/Admin akışına bağlandı:
/// Türkiye dışına giden bir sipariş checkout anında otomatik bir gümrük beyanı taslağı oluşturur.</summary>
public sealed class CustomsDeclarationRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCustomsTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
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

    private static async Task ActivateProvidersAsync(ISender sender)
    {
        await sender.Send(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null));

        await sender.Send(new ConfigureIntegrationProviderCommand(
            "dhl", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key", ["ApiSecret"] = "test-secret" },
            ActingIdentityUserId: null));
    }

    [Fact]
    public async Task UluslararasiSiparisGercekHsKoduIleGumrukBeyaniOlusturur()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-customs-kategori-1", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-customs-urun", "CUSTOMS-001", 500m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Gümrük Ürünü", Description: null,
            HsCode: "6913.90"));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await ActivateProvidersAsync(sender);

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 2));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Test Müşteri", "test@dekorras.com", "5551234567", "DE", "Berlin", "Test Adres 1",
            "bank-transfer", "dhl"));

        var declaration = await dbContext.Set<CustomsDeclaration>().FirstOrDefaultAsync(d => d.OrderId == placeOrderResult.OrderId);
        Assert.NotNull(declaration);
        Assert.Equal("6913.90", declaration!.HsCodeSummary);
        Assert.Equal(1000m, declaration.DeclaredValueTry); // 2 x 500 TRY - KDV/kargo HARİÇ
        Assert.Contains("Test Gümrük Ürünü", declaration.ContentDescription);
    }

    [Fact]
    public async Task YurticiSiparisIcinGumrukBeyaniOlusturulmaz()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-customs-kategori-2", null, 0, "tr", "Test Kategori", null));
        var productId = await sender.Send(new CreateProductCommand(
            "test-customs-yurtici-urun", "CUSTOMS-002", 300m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "Test Yurtiçi Ürün", Description: null));
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
            sessionKey, IdentityUserId: null, "Test Müşteri", "test@dekorras.com", "5551234567", "TR", "Kayseri", "Test Adres 1",
            "bank-transfer", "yurtici-kargo"));

        var declaration = await dbContext.Set<CustomsDeclaration>().FirstOrDefaultAsync(d => d.OrderId == placeOrderResult.OrderId);
        Assert.Null(declaration);
    }

    [Fact]
    public async Task HsKoduGirilmemisUrunIcinBelirtilmemisNotuYazilirVeAdminDuzeltebilir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await dbContext.SaveChangesAsync();

        var categoryId = await sender.Send(new CreateCategoryCommand("test-customs-kategori-3", null, 0, "tr", "Test Kategori", null));

        // HsCode BİLİNÇLİ OLARAK verilmedi.
        var productId = await sender.Send(new CreateProductCommand(
            "test-customs-hskodsuz-urun", "CUSTOMS-003", 100m, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 10,
            BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: "HS Kodsuz Ürün", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));

        await ActivateProvidersAsync(sender);

        var sessionKey = Guid.NewGuid().ToString("N");
        await sender.Send(new AddCartItemCommand(sessionKey, productId, Quantity: 1));

        var placeOrderResult = await sender.Send(new PlaceOrderCommand(
            sessionKey, IdentityUserId: null, "Test Müşteri", "test@dekorras.com", "5551234567", "FR", "Paris", "Test Adres 1",
            "bank-transfer", "dhl"));

        var declarationBefore = await dbContext.Set<CustomsDeclaration>().FirstAsync(d => d.OrderId == placeOrderResult.OrderId);
        Assert.Contains("Belirtilmemiş", declarationBefore.HsCodeSummary);

        // Admin gerçek GTİP kodunu elle girip düzeltir.
        await sender.Send(new UpdateCustomsDeclarationCommand(placeOrderResult.OrderId, "9403.30", 100m, "HS Kodsuz Ürün (düzeltildi)"));

        var declarationAfter = await dbContext.Set<CustomsDeclaration>().FirstAsync(d => d.OrderId == placeOrderResult.OrderId);
        Assert.Equal("9403.30", declarationAfter.HsCodeSummary);
        Assert.Equal("HS Kodsuz Ürün (düzeltildi)", declarationAfter.ContentDescription);
    }
}
