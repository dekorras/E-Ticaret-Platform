using System.Text.Json;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using Dekorras.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Sipariş sonrası müşteri akışları: onay önizlemesi sayfası (e-postadaki bağlantı, giriş gerekmez,
/// revizyon açıklaması zorunlu) ve ürün sayfasındaki "Tasarım değişikliği iste" formu (dosya ekli).</summary>
[Collection("storefront")]
public sealed class OrderFollowUpTests(StorefrontFixture fx)
{
    private static ApplicationDbContext Db()
    {
        var password = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD") ?? "";
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={password};TrustServerCertificate=True;").Options);
    }

    [SkippableFact]
    public async Task OnayOnizlemesiSayfasi_RevizyonAciklamaIster_OnaylaninceDurumGuncellenir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);

        // Geliştirme veritabanına geçici bir sipariş + ölçüye özel kalem + onay kaydı eklenir (sonda silinir).
        await using var db = Db();
        var customer = await StorefrontFixture.CreateTemporaryCustomerAsync(db);
        var order = new Order($"E2E-{Guid.NewGuid():N}"[..20], customer.Id, OrderSource.Web, Guid.NewGuid(), Guid.NewGuid());
        var config = new WallConfiguration(300m, 200m, "plain");
        var snapshot = WallpaperPriceCalculator.Calculate(300m, 200m, 1, new MaterialPricing("plain", "Dokusuz", 699m, 100m, 5m, 1m));
        var item = order.AddConfiguredItem(Guid.NewGuid(), "E2E Poster (300×200 cm, Dokusuz)", snapshot.UnitPrice, 1, 20m, config.ToJson(), JsonSerializer.Serialize(snapshot));
        var proof = new ProductionProof(order.Id, item.Id, DateTime.UtcNow.AddHours(24));
        proof.SetPreview("/uploads/wall/scenes/olmayan-onizleme.jpg");
        db.Orders.Add(order);
        db.ProductionProofs.Add(proof);
        await db.SaveChangesAsync();

        try
        {
            await using var context = await fx.Browser.NewContextAsync(new() { JavaScriptEnabled = false });
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{fx.BaseUrl}/siparis-onay/{proof.Token}");
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Baskı onay önizlemesi");
            await Assertions.Expect(page.Locator("text=300 × 200 cm")).ToBeVisibleAsync();

            // JS kapalıyken de düz form POST'u çalışır (boş revizyonun reddi entegrasyon testinde doğrulanıyor).
            await page.Locator("button:has-text('Onaylıyorum')").ClickAsync();
            await Assertions.Expect(page.Locator(".alert-success").First).ToContainTextAsync("onaylandı");
            await Assertions.Expect(page.Locator("button:has-text('Onaylıyorum')")).ToHaveCountAsync(0);

            db.ChangeTracker.Clear();
            Assert.Equal(ProofStatus.Onaylandi, (await db.ProductionProofs.SingleAsync(p => p.Id == proof.Id)).Status);

            var missing = await context.APIRequest.GetAsync($"{fx.BaseUrl}/siparis-onay/yok-boyle-bir-token");
            Assert.Equal(404, missing.Status);

            // API: token başka bir siparişin kimliğiyle kullanılamaz.
            var wrong = await context.APIRequest.PostAsync($"{fx.BaseUrl}/api/v1/orders/{Guid.NewGuid()}/proofs/{proof.Id}/approve",
                new() { DataObject = new { token = proof.Token } });
            Assert.Equal(404, wrong.Status);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ProductionProofs.Where(p => p.Id == proof.Id).ExecuteDeleteAsync();
            await db.Set<OrderItem>().Where(i => i.OrderId == order.Id).ExecuteDeleteAsync();
            await db.Set<OrderStatusHistory>().Where(h => h.OrderId == order.Id).ExecuteDeleteAsync();
            await db.Orders.Where(o => o.Id == order.Id).ExecuteDeleteAsync();
            await db.Customers.Where(c => c.Id == customer.Id).ExecuteDeleteAsync();
        }
    }

    [SkippableFact]
    public async Task TasarimDegisikligiFormu_DosyaEkliGonderilir_VeritabaninaKaydedilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        await using var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        var email = $"tasarim-{Guid.NewGuid():N}@dekorras.test";
        var photo = Path.Combine(Path.GetTempPath(), $"ornek-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(photo, TestImages.SolidJpeg(640, 480));

        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/Product/Details?slug={slug}&w_cm=320&h_cm=240");
            await page.Locator("[data-wall-design-request]").ClickAsync();
            await page.Locator("#drName").FillAsync("E2E Müşteri");
            await page.Locator("#drEmail").FillAsync(email);
            await page.Locator("#drType").SelectOptionAsync("OzelOlcu");
            await page.Locator("#drMessage").FillAsync("Pencere boşluğunu hesaba katın.");
            await page.Locator("#drFiles").SetInputFilesAsync(photo);
            await page.Locator(".modal button[type=submit]").ClickAsync();
            await Assertions.Expect(page.Locator(".modal .alert-success")).ToContainTextAsync("Talebiniz alındı");

            await using var db = Db();
            var saved = await db.DesignRequests.Include(r => r.Attachments).SingleAsync(r => r.Email == email);
            Assert.Equal(DesignRequestType.OzelOlcu, saved.RequestType);
            Assert.Single(saved.Attachments);
            Assert.Equal(320m, WallConfiguration.FromJson(saved.ConfigurationJson)!.WidthCm);
            Assert.NotNull(saved.ProductId);
        }
        finally
        {
            File.Delete(photo);
            await using var db = Db();
            foreach (var r in await db.DesignRequests.Where(r => r.Email == email).ToListAsync()) db.DesignRequests.Remove(r);
            await db.SaveChangesAsync();
        }
    }
}
