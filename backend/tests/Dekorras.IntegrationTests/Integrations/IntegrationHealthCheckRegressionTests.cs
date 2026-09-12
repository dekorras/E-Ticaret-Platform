using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Integrations.Queries;
using Dekorras.Domain.Integrations;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.Integrations;

/// <summary>Gerçek SQL Server'a karşı çalışır - `IntegrationHealthCheckLog` Faz 0/1'den beri EF'e
/// kayıtlıydı ama hiçbir yerden hiç eklenmiyordu (yalnızca IntegrationProvider'ın SON kontrolü
/// tutuluyordu, geçmiş yoktu). Her yapılandırma/yeniden test çağrısının bir log satırı eklediğini
/// ve yeniden test etmenin TÜM formu doldurmadan mevcut (şifresi çözülmüş) değerlerle çalıştığını
/// kanıtlar.</summary>
public sealed class IntegrationHealthCheckRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasIntegrationHealthCheckTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task YapilandirmaVeYenidenTestEtmeGecmisLogSatiriEklerVeMevcutDegerlerleCalisir()
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
        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Dekorras.Application.Common.Interfaces.IUnitOfWork>();
        var registry = scope.ServiceProvider.GetRequiredService<Dekorras.Application.Common.Interfaces.IProviderRegistry>();
        var secretProtector = scope.ServiceProvider.GetRequiredService<Dekorras.Application.Common.Interfaces.ISecretProtector>();

        var configureHandler = new ConfigureIntegrationProviderCommandHandler(registry, unitOfWork, secretProtector);
        var configureResult = await configureHandler.Handle(new ConfigureIntegrationProviderCommand(
            "bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "Test Bank", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "Dekorras" },
            ActingIdentityUserId: null), CancellationToken.None);
        Assert.True(configureResult.Success);

        var providerId = unitOfWork.Repository<IntegrationProvider>().Query().First(p => p.ProviderKey == "bank-transfer").Id;

        var logsAfterConfigure = await new GetIntegrationHealthCheckLogsQueryHandler(unitOfWork)
            .Handle(new GetIntegrationHealthCheckLogsQuery(providerId), CancellationToken.None);
        Assert.Single(logsAfterConfigure);
        Assert.True(logsAfterConfigure.Single().Success);

        // Yeniden test etme, TÜM formu doldurmaya GEREK KALMADAN yalnızca sağlayıcı ID'siyle
        // çalışmalı - mevcut (şifresi çözülmüş) config değerlerini kendi okur.
        var retestHandler = new TestIntegrationProviderConnectionCommandHandler(registry, unitOfWork, secretProtector);
        var retestResult = await retestHandler.Handle(new TestIntegrationProviderConnectionCommand(providerId), CancellationToken.None);
        Assert.True(retestResult.Success);

        var logsAfterRetest = await new GetIntegrationHealthCheckLogsQueryHandler(unitOfWork)
            .Handle(new GetIntegrationHealthCheckLogsQuery(providerId), CancellationToken.None);
        Assert.Equal(2, logsAfterRetest.Count); // ikinci test YENİ bir log satırı eklemeli, öncekini SİLMEMELİ
    }
}
