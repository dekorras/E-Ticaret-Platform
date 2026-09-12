using Dekorras.Application;
using Dekorras.Application.Accounting.Commands;
using Dekorras.Application.Accounting.Queries;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>Gerçek SQL Server'a karşı çalışır - bkz. LedgerAccountRegressionTests'teki aynı desen.</summary>
public sealed class QuoteRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasQuoteTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task IkinciKalemEklemek_ToplamiDogruGunceller_VeHataVermez()
    {
        Guid accountId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var account = new LedgerAccount("Test Teklif Cari", LedgerAccountType.Customer);
            await unitOfWork.Repository<LedgerAccount>().AddAsync(account, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            accountId = account.Id;
        }

        Guid quoteId;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new CreateQuoteCommandHandler(new UnitOfWork(dbContext));
            quoteId = await handler.Handle(new CreateQuoteCommand(accountId, DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        }

        // İlk kalem.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddQuoteLineCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new AddQuoteLineCommand(quoteId, "Ürün A", 100m, 2), CancellationToken.None);
        }

        // İkinci kalem: Quote artık ZATEN bir kaleme sahip - Lines koleksiyonunun doğru yüklenip
        // yeni kalemin "Modified" değil "Added" olarak işaretlendiğini doğrulayan asıl regresyon
        // senaryosu.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddQuoteLineCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new AddQuoteLineCommand(quoteId, "Ürün B", 50m, 3), CancellationToken.None);
        }

        await using var verifyContext = CreateDbContext();
        var quote = await verifyContext.Set<Quote>().FirstAsync(q => q.Id == quoteId);
        Assert.Equal(350m, quote.TotalAmountTry); // (100x2) + (50x3)

        var queryHandler = new GetQuoteDetailQueryHandler(new UnitOfWork(verifyContext));
        var detail = await queryHandler.Handle(new GetQuoteDetailQuery(quoteId), CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Lines.Count);
        Assert.Equal("Test Teklif Cari", detail.LedgerAccountName);
        Assert.False(detail.IsConverted);
    }

    private static ServiceProvider BuildServiceProvider()
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
        return services.BuildServiceProvider();
    }

    private static async Task ConfigureProvidersAsync(IUnitOfWork unitOfWork, IProviderRegistry registry, ISecretProtector secretProtector)
    {
        var configureHandler = new ConfigureIntegrationProviderCommandHandler(registry, unitOfWork, secretProtector);
        await configureHandler.Handle(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null), CancellationToken.None);
        await configureHandler.Handle(new ConfigureIntegrationProviderCommand(
            "yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "12345", ["ApiKey"] = "test-key" },
            ActingIdentityUserId: null), CancellationToken.None);
    }

    [Fact]
    public async Task TeklifSipariseDonusturulurVeCariHesabaMusteriBaglanir()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registry = scope.ServiceProvider.GetRequiredService<IProviderRegistry>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        await ConfigureProvidersAsync(unitOfWork, registry, secretProtector);

        var kurumsalGroup = new CustomerGroup("Kurumsal");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(kurumsalGroup, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var ledgerAccount = new LedgerAccount("Dönüşüm Test Cari", LedgerAccountType.Customer);
        await unitOfWork.Repository<LedgerAccount>().AddAsync(ledgerAccount, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var quoteId = await new CreateQuoteCommandHandler(unitOfWork).Handle(new CreateQuoteCommand(ledgerAccount.Id, DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        await new AddQuoteLineCommandHandler(unitOfWork).Handle(new AddQuoteLineCommand(quoteId, "Özel Üretim Kalem", 500m, 2), CancellationToken.None);

        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var convertHandler = new ConvertQuoteToOrderCommandHandler(unitOfWork, registry, secretProtector, emailSender);
        var result = await convertHandler.Handle(new ConvertQuoteToOrderCommand(
            quoteId, "B2B Yetkili", "b2b@test.com", "5551112233", "TR", "İstanbul", "Test Cadde No:5",
            "bank-transfer", "yurtici-kargo"), CancellationToken.None);

        Assert.True(result.PaymentAuthorized);
        Assert.StartsWith("TEKLIF-", result.OrderNumber);

        var order = await unitOfWork.Repository<Order>().GetByIdAsync(result.OrderId, CancellationToken.None);
        Assert.NotNull(order);
        Assert.Equal(OrderSource.AdminQuote, order!.Source);
        Assert.Equal(1000m, order.SubTotalTry); // 500 x 2, KDV'siz

        var quoteDetail = await new GetQuoteDetailQueryHandler(unitOfWork).Handle(new GetQuoteDetailQuery(quoteId), CancellationToken.None);
        Assert.True(quoteDetail!.IsConverted);

        var reloadedLedgerAccount = await unitOfWork.Repository<LedgerAccount>().GetByIdAsync(ledgerAccount.Id, CancellationToken.None);
        Assert.NotNull(reloadedLedgerAccount!.LinkedCustomerId);
        Assert.Equal(order.CustomerId, reloadedLedgerAccount.LinkedCustomerId);

        // AYNI cari hesap için İKİNCİ bir teklif dönüştürülünce YENİ bir müşteri OLUŞTURULMAMALI -
        // az önce bağlanan müşteri yeniden kullanılmalı.
        var secondQuoteId = await new CreateQuoteCommandHandler(unitOfWork).Handle(new CreateQuoteCommand(ledgerAccount.Id, DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        await new AddQuoteLineCommandHandler(unitOfWork).Handle(new AddQuoteLineCommand(secondQuoteId, "İkinci Kalem", 200m, 1), CancellationToken.None);
        var secondResult = await convertHandler.Handle(new ConvertQuoteToOrderCommand(
            secondQuoteId, "B2B Yetkili", "b2b@test.com", "5551112233", "TR", "İstanbul", "Test Cadde No:5",
            "bank-transfer", "yurtici-kargo"), CancellationToken.None);

        var secondOrder = await unitOfWork.Repository<Order>().GetByIdAsync(secondResult.OrderId, CancellationToken.None);
        Assert.Equal(order.CustomerId, secondOrder!.CustomerId);
    }

    [Fact]
    public async Task DonusturulmusVeyaSuresiDolmusTeklifTekrarDonusturulemez()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registry = scope.ServiceProvider.GetRequiredService<IProviderRegistry>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        await ConfigureProvidersAsync(unitOfWork, registry, secretProtector);

        var kurumsalGroup = new CustomerGroup("Kurumsal");
        await unitOfWork.Repository<CustomerGroup>().AddAsync(kurumsalGroup, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var ledgerAccount = new LedgerAccount("Tekrar Dönüşüm Test Cari", LedgerAccountType.Customer);
        await unitOfWork.Repository<LedgerAccount>().AddAsync(ledgerAccount, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var convertHandler = new ConvertQuoteToOrderCommandHandler(unitOfWork, registry, secretProtector, emailSender);

        var quoteId = await new CreateQuoteCommandHandler(unitOfWork).Handle(new CreateQuoteCommand(ledgerAccount.Id, DateTime.UtcNow.AddDays(14)), CancellationToken.None);
        await new AddQuoteLineCommandHandler(unitOfWork).Handle(new AddQuoteLineCommand(quoteId, "Kalem", 100m, 1), CancellationToken.None);
        await convertHandler.Handle(new ConvertQuoteToOrderCommand(
            quoteId, "Yetkili", "yetkili@test.com", "5550001122", "TR", "Ankara", "Adres 1",
            "bank-transfer", "yurtici-kargo"), CancellationToken.None);

        // Zaten dönüştürülmüş bir teklifi TEKRAR dönüştürmeye çalışmak reddedilmeli.
        await Assert.ThrowsAsync<InvalidOperationException>(() => convertHandler.Handle(new ConvertQuoteToOrderCommand(
            quoteId, "Yetkili", "yetkili@test.com", "5550001122", "TR", "Ankara", "Adres 1",
            "bank-transfer", "yurtici-kargo"), CancellationToken.None));

        // Süresi dolmuş bir teklif de dönüştürülemez.
        var expiredQuoteId = await new CreateQuoteCommandHandler(unitOfWork).Handle(new CreateQuoteCommand(ledgerAccount.Id, DateTime.UtcNow.AddDays(-1)), CancellationToken.None);
        await new AddQuoteLineCommandHandler(unitOfWork).Handle(new AddQuoteLineCommand(expiredQuoteId, "Kalem", 100m, 1), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => convertHandler.Handle(new ConvertQuoteToOrderCommand(
            expiredQuoteId, "Yetkili", "yetkili@test.com", "5550001122", "TR", "Ankara", "Adres 1",
            "bank-transfer", "yurtici-kargo"), CancellationToken.None));
    }
}
