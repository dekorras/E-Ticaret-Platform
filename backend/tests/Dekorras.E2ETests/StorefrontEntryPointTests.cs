using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Müşterinin konfigüratöre/"Duvarında Gör"e ULAŞTIĞI giriş noktaları: anasayfa ürün blokları ve eski
/// kategori sayfalarındaki genel ürün kartı (_ProductGrid), üst menüdeki "Duvarımda Dene" bağlantısı,
/// kategori tanıtım şeridi ve düz "Sepete Ekle"nin ölçüye özel üründe ürün sayfasına yönlendirmesi.</summary>
[Collection("storefront")]
public sealed class StorefrontEntryPointTests(StorefrontFixture fx)
{
    private static readonly string ScreenshotDir = Path.Combine(Path.GetTempPath(), "dekorras-e2e");

    [SkippableFact]
    public async Task Anasayfa_KarttaDuvarindaGorVeOlcunuSec_TiklayincaGoruntuleyiciAcilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        await using var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        await page.GotoAsync(fx.BaseUrl + "/");

        await Assertions.Expect(page.Locator("a[data-wall-nav]")).ToBeVisibleAsync();
        var card = page.Locator(".dk-product-card", new() { Has = page.Locator("a[data-wall-preview]") }).First;
        await card.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(card.Locator("a:has-text('Ölçünü Seç')")).ToBeVisibleAsync();
        await Assertions.Expect(card.Locator("button:has-text('Sepete Ekle')")).ToHaveCountAsync(0);
        await Assertions.Expect(card).ToContainTextAsync("/m²");
        Directory.CreateDirectory(ScreenshotDir);
        await card.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "anasayfa-kart.png") });

        await card.Locator("a[data-wall-preview]").ClickAsync();
        // wall-preview-link.js görüntüleyiciyi modal (iframe) olarak açar.
        await Assertions.Expect(page.Locator("dialog[open] iframe")).ToBeVisibleAsync();
        // İframe içindeki görüntüleyici gerçekten açıldı ve render motoru (WebGL/2D) çalıştı.
        var frame = page.FrameLocator("dialog.wall-dialog iframe");
        await frame.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        await Assertions.Expect(frame.Locator("[data-viz-title]")).Not.ToBeEmptyAsync();
        await page.WaitForTimeoutAsync(1500); // sahne görseli yüklensin (yalnızca ekran görüntüsü için)
        await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "anasayfa-duvarinda-gor.png") });
    }

    [SkippableFact]
    public async Task KategoriSayfasi_TanitimSeridiVar_DuzSepeteEkleUrunSayfasinaYonlendirir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        await using var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);

        await page.GotoAsync(fx.BaseUrl + "/Category/Index?slug=posterler-141");
        await Assertions.Expect(page.Locator("[data-wall-category-banner]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".dk-product-card a[data-wall-preview]").First).ToBeVisibleAsync();
        Directory.CreateDirectory(ScreenshotDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "kategori.png") });

        // Eski bir sayfadan/yer imi formundan gelen düz "Sepete Ekle" POST'u: ürün sayfasındaki konfigüratöre gider,
        // sepete ölçüsüz kalem EKLENMEZ.
        var (slug, productId) = await fx.AnyProductAsync(context.APIRequest);
        var html = await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/Category/Index?slug=posterler-141")).TextAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Skip.If(string.IsNullOrEmpty(token), "Kategori sayfasında antiforgery belirteci bulunamadı.");
        var response = await context.APIRequest.PostAsync($"{fx.BaseUrl}/Cart/Add", new()
        {
            Form = context.APIRequest.CreateFormData().Set("productId", productId).Set("quantity", "1").Set("__RequestVerificationToken", token),
            MaxRedirects = 0
        });
        Assert.Equal(302, response.Status);
        Assert.Contains(slug, Uri.UnescapeDataString(response.Headers["location"]));

        var cart = await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/cart")).TextAsync();
        Assert.DoesNotContain(productId, cart, StringComparison.OrdinalIgnoreCase);
    }
}
