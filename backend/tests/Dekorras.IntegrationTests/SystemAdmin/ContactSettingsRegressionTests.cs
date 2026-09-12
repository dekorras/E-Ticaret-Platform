using Dekorras.Application.Common;
using Dekorras.Application.SystemAdmin.Commands;
using Dekorras.Application.SystemAdmin.Queries;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.SystemAdmin;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.SystemAdmin.Setting` Faz 0/1'den beri vardı
/// ama hiçbir tanımlı kullanım senaryosu olmadığı için bilinçli olarak orphaned bırakılmıştı (bkz.
/// devamı 22/43/45). Plan §2.1'in "İletişim kanalları" maddesi bu turda İLK gerçek kullanım
/// senaryosunu sağladı.</summary>
public sealed class ContactSettingsRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasSettingsTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task AyarIlkKezOlusturulurIkinciKezGuncellenirYineleneSatirOlusturmaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        await new UpsertSettingCommandHandler(unitOfWork)
            .Handle(new UpsertSettingCommand(ContactSettingKeys.PhoneNumber, "+90 352 000 00 00"), CancellationToken.None);

        var afterCreate = await new GetSettingsQueryHandler(unitOfWork)
            .Handle(new GetSettingsQuery([ContactSettingKeys.PhoneNumber]), CancellationToken.None);
        Assert.Single(afterCreate);
        Assert.Equal("+90 352 000 00 00", afterCreate.Single().Value);

        // Aynı anahtar İKİNCİ kez ayarlanınca GÜNCELLENMELİ, yeni bir satır EKLENMEMELİ.
        await new UpsertSettingCommandHandler(unitOfWork)
            .Handle(new UpsertSettingCommand(ContactSettingKeys.PhoneNumber, "+90 352 111 11 11"), CancellationToken.None);

        var afterUpdate = await new GetSettingsQueryHandler(unitOfWork)
            .Handle(new GetSettingsQuery([ContactSettingKeys.PhoneNumber]), CancellationToken.None);
        Assert.Single(afterUpdate);
        Assert.Equal("+90 352 111 11 11", afterUpdate.Single().Value);

        var rawCount = await dbContext.Set<Dekorras.Domain.SystemAdmin.Setting>().CountAsync(s => s.Key == ContactSettingKeys.PhoneNumber);
        Assert.Equal(1, rawCount);
    }

    [Fact]
    public async Task HicAyarlanmamisAnahtarSonuctaHicGorunmez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        await new UpsertSettingCommandHandler(unitOfWork)
            .Handle(new UpsertSettingCommand(ContactSettingKeys.InstagramUrl, "https://instagram.com/dekorras"), CancellationToken.None);

        // WhatsAppUrl hiç ayarlanmadı - sonuçta hiç görünmemeli (boş bir değerle değil, TAMAMEN yok).
        var result = await new GetSettingsQueryHandler(unitOfWork)
            .Handle(new GetSettingsQuery(ContactSettingKeys.All), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(ContactSettingKeys.InstagramUrl, result.Single().Key);
        Assert.DoesNotContain(result, s => s.Key == ContactSettingKeys.WhatsAppUrl);
    }
}
