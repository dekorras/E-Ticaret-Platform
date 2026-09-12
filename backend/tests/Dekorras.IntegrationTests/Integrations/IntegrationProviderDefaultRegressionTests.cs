using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Integrations.Queries;
using Dekorras.Domain.Integrations;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.Integrations;

/// <summary>Gerçek SQL Server'a karşı çalışır - `IntegrationProvider.MarkAsDefaultForCategory`/
/// `UnmarkAsDefaultForCategory` Faz 0/1'den beri vardı ama hiçbir yerden çağrılamıyordu/okunmuyordu.
/// Aynı anda bir kategoride yalnızca BİR sağlayıcının varsayılan kalabildiğini ve checkout'un
/// aktif sağlayıcı sorgusunun bu bayrağı doğru yansıttığını doğrular.</summary>
public sealed class IntegrationProviderDefaultRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasProviderDefaultTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task VarsayilanTekBirSaglayicidaKalirVeCheckoutSorgusunaYansir()
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
        services.AddPersistence(configuration);
        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var registry = scope.ServiceProvider.GetRequiredService<IProviderRegistry>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var configureHandler = new ConfigureIntegrationProviderCommandHandler(registry, unitOfWork, secretProtector);

        await configureHandler.Handle(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null), CancellationToken.None);
        await configureHandler.Handle(new ConfigureIntegrationProviderCommand(
            "paytr", ProviderCategory.Payment,
            new Dictionary<string, string> { ["MerchantId"] = "1", ["MerchantKey"] = "k", ["MerchantSalt"] = "s" },
            ActingIdentityUserId: null), CancellationToken.None);

        var bankTransferId = unitOfWork.Repository<IntegrationProvider>().Query().First(p => p.ProviderKey == "bank-transfer").Id;
        var paytrId = unitOfWork.Repository<IntegrationProvider>().Query().First(p => p.ProviderKey == "paytr").Id;

        var setDefaultHandler = new SetIntegrationProviderDefaultCommandHandler(unitOfWork);
        await setDefaultHandler.Handle(new SetIntegrationProviderDefaultCommand(bankTransferId), CancellationToken.None);

        var afterFirst = await new GetIntegrationProvidersQueryHandler(registry, unitOfWork).Handle(new GetIntegrationProvidersQuery(ProviderCategory.Payment), CancellationToken.None);
        Assert.True(afterFirst.Single(p => p.ProviderKey == "bank-transfer").IsDefaultForCategory);
        Assert.False(afterFirst.Single(p => p.ProviderKey == "paytr").IsDefaultForCategory);

        // Şimdi paytr varsayılan yapılır - bank-transfer'in bayrağı OTOMATİK kalkmalı.
        await setDefaultHandler.Handle(new SetIntegrationProviderDefaultCommand(paytrId), CancellationToken.None);

        var afterSecond = await new GetIntegrationProvidersQueryHandler(registry, unitOfWork).Handle(new GetIntegrationProvidersQuery(ProviderCategory.Payment), CancellationToken.None);
        Assert.False(afterSecond.Single(p => p.ProviderKey == "bank-transfer").IsDefaultForCategory);
        Assert.True(afterSecond.Single(p => p.ProviderKey == "paytr").IsDefaultForCategory);

        // Checkout'un kullandığı GetActiveProvidersQuery de AYNI bayrağı yansıtmalı.
        var activeProviders = await new GetActiveProvidersQueryHandler(unitOfWork).Handle(new GetActiveProvidersQuery(ProviderCategory.Payment), CancellationToken.None);
        Assert.True(activeProviders.Single(p => p.ProviderKey == "paytr").IsDefault);
        Assert.False(activeProviders.Single(p => p.ProviderKey == "bank-transfer").IsDefault);
    }
}
