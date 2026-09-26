using System.Text.Json;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using Dekorras.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Dilim 10 admin ekranları gerçek tarayıcıda (Blazor Server etkileşimli devre): tüm sayfalar hatasız
/// açılır, sahne köşe düzenleyicisi sürüklemeyi .NET'e bildirir, etiket ekle/sil, ürün "Duvar Kağıdı" sekmesi,
/// sipariş detayındaki ölçüye özel baskı kartı ve özel indirme uçlarının yetki koruması.</summary>
[Collection("storefront")]
public sealed class AdminWallTests(StorefrontFixture fx)
{
    private static ApplicationDbContext Db()
    {
        var password = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD") ?? "";
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={password};TrustServerCertificate=True;").Options);
    }

    private async Task<(IBrowserContext Context, IPage Page)> LoginAsync()
    {
        var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1500, Height = 1000 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        await page.GotoAsync($"{fx.BaseUrl}/admin/login");
        await page.Locator("input[name=email]").FillAsync("admin@dekorras.com");
        await page.Locator("input[name=password]").FillAsync("Dekorras38*");
        await page.Locator("button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(u => !u.Contains("/admin/login"));
        Skip.If(page.Url.Contains("2fa"), "Yönetici hesabında iki faktörlü doğrulama açık.");
        return (context, page);
    }

    /// <summary>Blazor Server önce statik (prerender) HTML verir, devre sonra bağlanır; bu arada yapılan tıklama/yazma
    /// kaybolur. Eylem, beklenen etki görülene kadar (devre bağlanınca) birkaç kez yeniden denenir.</summary>
    private static async Task RetryUntilAsync(Func<Task> action, ILocator expectVisible)
    {
        for (var attempt = 0; ; attempt++)
        {
            await action();
            try
            {
                await expectVisible.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                return;
            }
            catch (TimeoutException) when (attempt < 6) { }
        }
    }

    private static async Task ExpectInteractiveAsync(IPage page, string heading)
    {
        await Assertions.Expect(page.Locator("h4.mb-sm-0").First).ToContainTextAsync(heading);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [SkippableFact]
    public async Task TumDuvarKagidiSayfalari_Acilir_SahneKoseSurukleVeEtiketEkleSil()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await LoginAsync();
        await using var _ = context;

        foreach (var (path, heading) in new[]
        {
            ("/admin/wall/materials", "Malzemeleri"), ("/admin/wall/settings", "Ayarları"), ("/admin/wall/bulk-upload", "Toplu Poster"),
            ("/admin/wall/design-requests", "Tasarım"), ("/admin/wall/embed-clients", "Embed"), ("/admin/wall/report", "Dönüşüm"),
            ("/admin/wall/tags", "Etiketleri"), ("/admin/wall/scenes", "Oda Sahneleri")
        })
        {
            await page.GotoAsync(fx.BaseUrl + path);
            await ExpectInteractiveAsync(page, heading);
        }
        await Assertions.Expect(page.Locator("a[href='/admin/wall/materials']")).ToHaveCountAsync(1); // menü bağlantısı

        // Sahne köşe düzenleyici: tutamaç sürüklenince .NET tarafındaki köşe metni değişir (kaydetmeden).
        var handle = page.Locator("#wallSceneEditor [data-corner='0'] circle");
        await RetryUntilAsync(() => page.Locator(".list-group-item").First.ClickAsync(), handle);
        var cornersText = page.Locator("text=Köşeler (px)");
        var before = await cornersText.TextContentAsync();
        var box = (await handle.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + 60, box.Y + 40, new() { Steps = 5 });
        await page.Mouse.UpAsync();
        await Assertions.Expect(cornersText).Not.ToHaveTextAsync(before!);

        // Etiket ekle → listede görünür → sil.
        await page.GotoAsync($"{fx.BaseUrl}/admin/wall/tags");
        await ExpectInteractiveAsync(page, "Etiketleri");
        var value = $"e2e{Guid.NewGuid():N}"[..12];
        var row = page.Locator("tr", new() { HasText = $"theme:{value}" });
        await RetryUntilAsync(async () =>
        {
            await page.Locator("select.form-select").First.SelectOptionAsync("Theme");
            await page.Locator("input[placeholder=green]").FillAsync(value);
            await page.Locator("input[placeholder='Yeşil']").FillAsync("E2E Tema");
            await page.Locator("button:has-text('Ekle')").ClickAsync();
        }, row);
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await row.Locator("button:has-text('Sil')").ClickAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task UrunDuvarKagidiSekmesi_VeSiparisBaskiKarti_IndirmeYetkiIster()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);

        // Oturumsuz: özel indirme ucu dosya vermez.
        using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
        {
            var anon = await http.GetAsync($"{fx.BaseUrl}/admin/wall/production-files/{Guid.NewGuid()}");
            Assert.NotEqual(System.Net.HttpStatusCode.OK, anon.StatusCode);
        }

        var (context, page) = await LoginAsync();
        await using var _ = context;
        var (_, productId) = await fx.AnyProductAsync(context.APIRequest);

        await page.GotoAsync($"{fx.BaseUrl}/admin/ecommerce/products/{productId}");
        await RetryUntilAsync(() => page.Locator("button.nav-link:has-text('Duvar Kağıdı')").ClickAsync(), page.Locator("#tab-duvar-kagidi h6"));
        await Assertions.Expect(page.Locator("#tab-duvar-kagidi h6").First).ToHaveTextAsync("Profil");
        await Assertions.Expect(page.Locator("#tab-duvar-kagidi table tbody tr")).Not.ToHaveCountAsync(0); // malzeme istisnaları
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        // Geçici sipariş + onay kaydı: admin sipariş detayında kart görünür ve oturumlu indirme 404 (dosya yok) döner.
        await using var db = Db();
        // Admin sipariş detayı müşteri + teslimat adresini okur: geçici bir adres eklenir (sonda silinir).
        var customer = await StorefrontFixture.CreateTemporaryCustomerAsync(db);
        var address = new Dekorras.Domain.Customers.Address(customer.Id, "E2E Alıcı", "TR", "İstanbul", "Test Sk. 1", "5550000000");
        db.Add(address);
        var order = new Order($"E2E-{Guid.NewGuid():N}"[..20], address.CustomerId, OrderSource.Web, address.Id, address.Id);
        var config = new WallConfiguration(300m, 200m, "plain");
        var snapshot = WallpaperPriceCalculator.Calculate(300m, 200m, 1, new MaterialPricing("plain", "Dokusuz", 699m, 100m, 5m, 1m));
        var item = order.AddConfiguredItem(Guid.Parse(productId), "E2E Baskı (300×200 cm, Dokusuz)", snapshot.UnitPrice, 1, 20m, config.ToJson(), JsonSerializer.Serialize(snapshot));
        var proof = new ProductionProof(order.Id, item.Id, DateTime.UtcNow.AddHours(24));
        db.Orders.Add(order);
        db.ProductionProofs.Add(proof);
        await db.SaveChangesAsync();
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/admin/ecommerce/orders/{order.Id}");
            var card = page.Locator(".card", new() { HasText = "Ölçüye özel baskılar" });
            await Assertions.Expect(card).ToBeVisibleAsync();
            await Assertions.Expect(card).ToContainTextAsync("Müşteri onayı bekleniyor");
            await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

            var authed = await context.APIRequest.GetAsync($"{fx.BaseUrl}/admin/wall/production-files/{Guid.NewGuid()}");
            Assert.Equal(404, authed.Status);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ProductionProofs.Where(p => p.OrderId == order.Id).ExecuteDeleteAsync();
            await db.ProductionFiles.Where(p => p.OrderId == order.Id).ExecuteDeleteAsync();
            await db.Set<OrderItem>().Where(i => i.OrderId == order.Id).ExecuteDeleteAsync();
            await db.Set<OrderStatusHistory>().Where(h => h.OrderId == order.Id).ExecuteDeleteAsync();
            await db.Orders.Where(o => o.Id == order.Id).ExecuteDeleteAsync();
            await db.Set<Dekorras.Domain.Customers.Address>().Where(a => a.Id == address.Id).ExecuteDeleteAsync();
            await db.Customers.Where(c => c.Id == customer.Id).ExecuteDeleteAsync();
        }
    }
}
