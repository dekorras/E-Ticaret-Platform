using Dekorras.Application.Notifications.Commands;
using Dekorras.Application.Notifications.Queries;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Notifications;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Notifications.EmailTemplate` Faz 0/1'den
/// beri vardı ama hiç Application/UI katmanı yoktu. Aynı anahtar+dil çiftinin ikinci kez
/// oluşturulamadığını ve güncellemenin doğru çalıştığını kanıtlar.</summary>
public sealed class EmailTemplateRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasEmailTemplateTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task SablonOlusturulurGuncellenirVeAyniAnahtarDilIkinciKezOlusturulamaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var templateId = await new CreateEmailTemplateCommandHandler(unitOfWork).Handle(new CreateEmailTemplateCommand(
            "OrderConfirmation", "tr", "Siparişiniz Alındı - {{OrderNumber}}", "<p>{{CustomerName}}</p>"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateEmailTemplateCommandHandler(unitOfWork)
            .Handle(new CreateEmailTemplateCommand("OrderConfirmation", "tr", "Başka Konu", "<p>İçerik</p>"), CancellationToken.None));

        await new UpdateEmailTemplateCommandHandler(unitOfWork).Handle(new UpdateEmailTemplateCommand(
            templateId, "Güncellenmiş Konu", "<p>Güncellenmiş içerik</p>"), CancellationToken.None);

        var detail = await new GetEmailTemplateByIdQueryHandler(unitOfWork).Handle(new GetEmailTemplateByIdQuery(templateId), CancellationToken.None);
        Assert.Equal("Güncellenmiş Konu", detail!.Subject);

        var list = await new GetEmailTemplatesQueryHandler(unitOfWork).Handle(new GetEmailTemplatesQuery(), CancellationToken.None);
        Assert.Single(list);
    }
}
