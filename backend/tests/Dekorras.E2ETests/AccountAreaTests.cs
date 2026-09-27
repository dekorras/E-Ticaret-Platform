using Dekorras.Domain.Ordering;
using Dekorras.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>"Hesabım" alanı: yeni üye olan müşteri sol menüdeki her başlığa gider, sayfa başlığı menüdekiyle
/// birebir aynıdır ve aktif menü öğesi işaretlenir; siparişlerde filtre çipleri ve kart açılır/kapanır.
/// Geliştirme veritabanına geçici müşteri + sipariş eklenir, sonda silinir.</summary>
[Collection("storefront")]
public sealed class AccountAreaTests(StorefrontFixture fx)
{
    private static readonly string ScreenshotDir = Path.Combine(Path.GetTempPath(), "dekorras-e2e");

    private static ApplicationDbContext Db()
    {
        var password = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD") ?? "";
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={password};TrustServerCertificate=True;").Options);
    }

    [SkippableFact]
    public async Task Hesabim_SolMenudekiHerBaslik_AyniBaslikliSayfayiAcar_SiparisKartiAcilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var email = $"e2e-hesabim-{Guid.NewGuid():N}@dekorras.test";
        await using var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        Order? order = null;
        try
        {
            await page.GotoAsync(fx.BaseUrl + "/Account/Register");
            await page.FillAsync("input[name=FullName]", "E2E Hesabım");
            await page.FillAsync("input[name=Email]", email);
            await page.FillAsync("input[name=Password]", "Test12345!");
            await page.Locator("button:has-text('Üye Ol')").ClickAsync();
            await page.WaitForURLAsync(u => !u.Contains("/Account/Register"));

            // Siparişlerim kartları için geçici sipariş (ilk yayındaki ürün).
            await using (var db = Db())
            {
                var customer = await db.Customers.SingleAsync(c => c.Email == email);
                var product = await db.Products.Where(p => p.PublishedAtUtc != null).Select(p => p.Id).FirstAsync();
                order = new Order($"E2E-{Guid.NewGuid():N}"[..20], customer.Id, OrderSource.Web, Guid.NewGuid(), Guid.NewGuid());
                order.AddItem(product, "E2E Ürün", 1000m, 1, 20m);
                order.TransitionTo(OrderStatus.Preparing);
                db.Orders.Add(order);
                await db.SaveChangesAsync();
            }

            string[] headings = ["Siparişlerim", "Sana Özel Fırsatlar", "Soru ve Taleplerim", "Değerlendirmelerim", "Kuponlarım",
                "Kullanıcı bilgilerim", "Beğendiklerim", "Tüm listelerim", "Müşteri Hizmetleri"];
            await page.GotoAsync(fx.BaseUrl + "/Account");
            await Assertions.Expect(page.Locator(".dk-acc-name")).ToHaveTextAsync("E2E Hesabım");
            await Assertions.Expect(page.Locator(".dk-acc-promo")).ToContainTextAsync("DUVARIMDA DENE");
            await Assertions.Expect(page.Locator(".dk-acc-nav")).Not.ToContainTextAsync("Hepsipay");
            Directory.CreateDirectory(ScreenshotDir);
            for (var i = 0; i < headings.Length; i++)
            {
                await page.Locator(".dk-acc-nav a", new() { HasTextString = headings[i] }).First.ClickAsync();
                await Assertions.Expect(page.Locator("h1.dk-acc-title")).ToHaveTextAsync(headings[i]);
                await Assertions.Expect(page.Locator(".dk-acc-link.is-active")).ToContainTextAsync(headings[i]);
                await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, $"hesabim-{i + 1}.png"), FullPage = true });
            }

            // Siparişlerim: kart görünür, "Devam edenler" filtresinde kalır, "İptaller"de boş; kart açılınca ürün listesi görünür.
            await page.GotoAsync(fx.BaseUrl + "/Account/Orders?filter=Ongoing");
            var card = page.Locator("article[data-order]", new() { HasTextString = order.OrderNumber });
            await Assertions.Expect(card).ToBeVisibleAsync();
            await card.Locator("[data-order-toggle]").ClickAsync();
            await page.WaitForTimeoutAsync(300); // ok animasyonu bitsin (yalnızca ekran görüntüsü için)
            await Assertions.Expect(card).ToContainTextAsync("E2E Ürün");
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "hesabim-siparis-acik.png"), FullPage = true });
            await page.GotoAsync(fx.BaseUrl + "/Account/Orders?filter=Cancelled");
            await Assertions.Expect(page.Locator("article[data-order]")).ToHaveCountAsync(0);

            // Düzen Bootstrap'e dayanır: account.css hiç yüklenmese de menü dikey liste, sipariş bir kart olarak kalır.
            await page.RouteAsync("**/css/account.css*", r => r.AbortAsync());
            await page.GotoAsync(fx.BaseUrl + "/Account/Orders");
            var links = page.Locator(".dk-acc-nav a");
            var first = await links.Nth(0).BoundingBoxAsync();
            var second = await links.Nth(1).BoundingBoxAsync();
            Assert.True(second!.Y > first!.Y + first.Height - 1, "Menü başlıkları alt alta olmalı");
            await Assertions.Expect(page.Locator("article[data-order].card")).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "hesabim-cssiz.png"), FullPage = true });
        }
        finally
        {
            await using var db = Db();
            if (order is not null)
            {
                await db.Set<OrderItem>().Where(i => i.OrderId == order.Id).ExecuteDeleteAsync();
                await db.Set<OrderStatusHistory>().Where(h => h.OrderId == order.Id).ExecuteDeleteAsync();
                await db.Orders.Where(o => o.Id == order.Id).ExecuteDeleteAsync();
            }
            await db.Customers.Where(c => c.Email == email).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }
    }
}
