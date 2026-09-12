using Dekorras.Application.Common.Interfaces;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Domain.Common;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.SystemAdmin;
using FluentAssertions;
using NSubstitute;

namespace Dekorras.Application.Tests.Integrations;

public class ConfigureIntegrationProviderCommandTests
{
    /// <summary>Kabul kriteri: hiçbir sağlayıcı varsayılan aktif değildir; admin panelden form
    /// doldurulup bağlantı testi başarılı olduğunda sağlayıcı Aktif duruma geçer (bkz. plan §4.1).</summary>
    [Fact]
    public async Task GecerliYapilandirma_SaglayiciyiAktifYapar()
    {
        var connector = Substitute.For<IPaymentGateway>();
        connector.ProviderKey.Returns("iyzico");
        connector.DisplayName.Returns("iyzico");
        connector.TestConnectionAsync(Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectorHealthCheckResult(true, "Bağlantı başarılı."));

        var registry = Substitute.For<IProviderRegistry>();
        registry.GetByKey("iyzico").Returns(connector);

        var providers = new List<IntegrationProvider>();
        var providerRepository = FakeRepository(providers);

        var auditLogs = new List<AuditLog>();
        var auditRepository = FakeRepository(auditLogs);

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Repository<IntegrationProvider>().Returns(providerRepository);
        unitOfWork.Repository<AuditLog>().Returns(auditRepository);

        var secretProtector = Substitute.For<ISecretProtector>();
        secretProtector.Protect(Arg.Any<string>()).Returns(ci => $"encrypted:{ci.Arg<string>()}");

        var handler = new ConfigureIntegrationProviderCommandHandler(registry, unitOfWork, secretProtector);

        var command = new ConfigureIntegrationProviderCommand(
            "iyzico", ProviderCategory.Payment,
            new Dictionary<string, string> { ["ApiKey"] = "abc", ["SecretKey"] = "xyz" },
            ActingIdentityUserId: "user-1");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Status.Should().Be(ProviderStatus.Active);
        providers.Should().ContainSingle(p => p.ProviderKey == "iyzico" && p.Status == ProviderStatus.Active);
    }

    private static IRepository<T> FakeRepository<T>(List<T> backingList) where T : BaseEntity
    {
        var repository = Substitute.For<IRepository<T>>();
        repository.Query().Returns(_ => backingList.AsQueryable());
        repository.AddAsync(Arg.Any<T>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                backingList.Add(callInfo.Arg<T>());
                return Task.CompletedTask;
            });
        return repository;
    }
}
