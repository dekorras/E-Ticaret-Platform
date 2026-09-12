using Dekorras.Application.Marketing.Commands;
using Dekorras.Domain.Marketing;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Content;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Marketing.NewsletterSubscriber` Faz 0/1'den
/// beri vardı ama Application katmanının HİÇBİR yerinden çağrılmıyordu (misafirler için hiç bülten
/// kayıt formu yoktu). E-posta gönderimi gerçek bir SMTP sağlayıcısına BAĞLI DEĞİL (bkz.
/// `SmtpEmailSender` - yalnızca loglar), bu yüzden onay linkindeki `SubscriberId` doğrudan DB'den
/// okunup test edilir - gerçek bir e-posta kutusuna erişim gerekmez.</summary>
public sealed class NewsletterSubscriptionRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasNewsletterSubscriberTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
    private const string ConfirmUrlTemplate = "https://example.com/Newsletter/Confirm?id={0}";

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
    public async Task AboneOlunurOnaylanirVeCiftKayitOlusmaz()
    {
        const string email = "abone@test.com";

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SubscribeToNewsletterCommandHandler(unitOfWork, new FakeEmailSender())
                .Handle(new SubscribeToNewsletterCommand(email, ConfirmUrlTemplate), CancellationToken.None);
        }

        Guid subscriberId;
        await using (var dbContext = CreateDbContext())
        {
            var subscriber = await dbContext.Set<NewsletterSubscriber>().SingleAsync(s => s.Email == email);
            Assert.False(subscriber.IsConfirmed);
            subscriberId = subscriber.Id;
        }

        // AYNI e-postayla tekrar kayıt olmaya çalışmak yinelenen bir satır OLUŞTURMAMALI - var olan
        // (henüz onaylanmamış) kaydın ID'si yeniden kullanılmalı.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            await new SubscribeToNewsletterCommandHandler(unitOfWork, new FakeEmailSender())
                .Handle(new SubscribeToNewsletterCommand(email, ConfirmUrlTemplate), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var count = await dbContext.Set<NewsletterSubscriber>().CountAsync(s => s.Email == email);
            Assert.Equal(1, count);
        }

        // Onay linkindeki ID ile onaylanır.
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var confirmed = await new ConfirmNewsletterSubscriptionCommandHandler(unitOfWork)
                .Handle(new ConfirmNewsletterSubscriptionCommand(subscriberId), CancellationToken.None);
            Assert.True(confirmed);
        }

        await using var finalContext = CreateDbContext();
        var finalSubscriber = await finalContext.Set<NewsletterSubscriber>().SingleAsync(s => s.Email == email);
        Assert.True(finalSubscriber.IsConfirmed);
    }

    [Fact]
    public async Task GecersizBirIdIleOnayBasarisizDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var confirmed = await new ConfirmNewsletterSubscriptionCommandHandler(unitOfWork)
            .Handle(new ConfirmNewsletterSubscriptionCommand(Guid.NewGuid()), CancellationToken.None);
        Assert.False(confirmed);
    }

    private sealed class FakeEmailSender : Dekorras.Application.Common.Interfaces.IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
