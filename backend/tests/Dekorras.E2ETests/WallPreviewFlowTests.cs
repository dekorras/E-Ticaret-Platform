using System.Text.Json;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Spec 1.12 - Playwright uçtan uca: katalogdan "Duvarımda Dene" → görüntüleyicide listeden
/// poster değiştir → ölçü gir → sepete ekle; "Duvarında Gör" modal aç/kapat + geri tuşu; konfigürasyonun
/// ürün sayfasına geri aktarımı; JS'siz tam sayfa açılış; return parametresinde açık yönlendirme engeli.</summary>
[Collection("storefront")]
public sealed class WallPreviewFlowTests(StorefrontFixture fx)
{
    private async Task<(IBrowserContext Context, IPage Page)> NewPageAsync(bool javaScript = true)
    {
        var context = await fx.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            JavaScriptEnabled = javaScript,
            Locale = "tr-TR"
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        return (context, page);
    }

    [SkippableFact]
    public async Task Katalog_DuvarimdaDene_GoruntuleyicideListedenDegistir_OlcuGir_SepeteEkle()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;

        await page.GotoAsync($"{fx.BaseUrl}/duvar-kagitlari");
        var cards = page.Locator("[data-wall-card]");
        await cards.First.WaitForAsync();

        // İki posteri "Duvarımda Dene" listesine ekle → alt tepside görünür.
        await cards.Nth(0).Locator("[data-tryon-toggle]").ClickAsync();
        await page.Locator("#wallTray .wall-tray-item").First.WaitForAsync();
        await cards.Nth(1).Locator("[data-tryon-toggle]").ClickAsync();
        await Assertions.Expect(page.Locator("#wallTray .wall-tray-item")).ToHaveCountAsync(2);
        var secondTitle = await cards.Nth(1).GetAttributeAsync("data-title");

        // Tepsiden görüntüleyiciyi aç → tam ekran modal (dialog + iframe), URL güncellenir.
        await page.Locator("#wallTray .wall-tray-tools a[data-wall-preview]").ClickAsync();
        await Assertions.Expect(page.Locator("dialog.wall-dialog[open]")).ToBeVisibleAsync();
        Assert.Contains("/duvarinda-gor", page.Url);

        var frame = page.FrameLocator("dialog.wall-dialog iframe");
        await frame.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        await Assertions.Expect(frame.Locator("[data-viz-loading]")).ToBeHiddenAsync();

        // "Denediklerim" sekmesi açık gelir; ikinci postere geç → başlık değişir.
        var items = frame.Locator("[data-viz-items] [data-viz-pick]");
        await Assertions.Expect(items).ToHaveCountAsync(2);
        await items.Nth(1).ClickAsync();
        await Assertions.Expect(frame.Locator("[data-viz-title]")).ToHaveTextAsync(secondTitle!);

        // Ölçü gir → fiyat özeti güncellenir.
        await frame.Locator("[data-viz-w]").FillAsync("250");
        await frame.Locator("[data-viz-h]").FillAsync("200");
        await Assertions.Expect(frame.Locator("[data-viz-sum='billed']")).ToHaveTextAsync("5,23 m²"); // (250+5)×(200+5) = 5,2275

        await frame.Locator("[data-viz-add-to-cart]").ClickAsync();
        await Assertions.Expect(frame.Locator("#wallToast")).ToContainTextAsync("Sepete eklendi");

        // Modalı kapat → önceki URL'ye dönülür.
        await frame.Locator("[data-viz-close]").ClickAsync();
        await Assertions.Expect(page.Locator("dialog.wall-dialog[open]")).ToHaveCountAsync(0);
        await page.WaitForURLAsync(u => u.EndsWith("/duvar-kagitlari")); // history.back() eşzamansız

        var cart = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/cart")).TextAsync()).RootElement;
        var line = cart.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(250m, line.GetProperty("configuration").GetProperty("widthCm").GetDecimal());
        Assert.Equal(secondTitle, line.GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task UrunSayfasi_ModalGeriTusuylaKapanir_AyarlarKonfiguratoreGeriAktarilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        await page.GotoAsync($"{fx.BaseUrl}/Product/Details?slug={slug}");
        var productUrl = page.Url;
        var link = page.Locator("[data-wall-configurator] a[data-wall-preview-source='konfigurator']");

        await link.ClickAsync();
        await Assertions.Expect(page.Locator("dialog.wall-dialog[open]")).ToBeVisibleAsync();
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("dialog.wall-dialog[open]")).ToHaveCountAsync(0);
        Assert.StartsWith(productUrl.Split('?')[0], page.Url);

        await link.ClickAsync();
        var frame = page.FrameLocator("dialog.wall-dialog iframe");
        await frame.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        await frame.Locator("[data-viz-w]").FillAsync("320");
        await frame.Locator("[data-viz-h]").FillAsync("210");
        await frame.Locator("[data-viz-mirror]").CheckAsync();
        await frame.Locator("[data-viz-apply]").ClickAsync();

        await Assertions.Expect(page.Locator("dialog.wall-dialog[open]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-wall-width]")).ToHaveValueAsync("320");
        await Assertions.Expect(page.Locator("[data-wall-height]")).ToHaveValueAsync("210");
        await Assertions.Expect(page.Locator("[data-wall-mirror]")).ToBeCheckedAsync();
        Assert.Contains("w_cm=320.0", page.Url);
    }

    [SkippableFact]
    public async Task JavaScriptOlmadan_TamSayfaSunucuRenderiylaAcilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync(javaScript: false);
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        var response = await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}&w_cm=200&h_cm=150");
        Assert.Equal(200, response!.Status);
        var src = await page.Locator("img[src*='/render?']").First.GetAttributeAsync("src");
        Assert.NotNull(src);

        var image = await context.APIRequest.GetAsync(fx.BaseUrl + System.Net.WebUtility.HtmlDecode(src));
        Assert.Equal(200, image.Status);
        Assert.Equal("image/jpeg", image.Headers["content-type"]);
    }

    [SkippableFact]
    public async Task ReturnParametresi_YalnizcaSiteIciYolKabulEdilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync(javaScript: false);
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}&return=//evil.example/steal");
        Assert.Equal(0, await page.Locator("a[href*='evil.example']").CountAsync());
        await Assertions.Expect(page.Locator("a", new() { HasText = "Ürüne dön" })).ToBeVisibleAsync();

        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}&return=/Product/Details?slug={slug}");
        await Assertions.Expect(page.Locator("a", new() { HasText = "Geri dön" })).ToHaveAttributeAsync("href", $"/Product/Details?slug={slug}");
    }

    [SkippableFact]
    public async Task KendiOdami_Yukle_KoseleriAyarla_KaydetVeSahneOlarakSec_MisafirIndirmedeGirisUyarisiAlir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        // Oda fotoğrafı yerine 1200×800 düz renkli JPEG; sunucu dosyayı içerikten (sihirli baytlar) doğrular.
        var photoPath = Path.Combine(Path.GetTempPath(), $"oda-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(photoPath, TestImages.SolidJpeg(1200, 800));
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}");
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
            var sceneCountBefore = await page.Locator("[data-viz-scene]").CountAsync();

            await page.Locator("[data-viz-upload-room]").ClickAsync();
            await page.Locator("[data-room-file]").SetInputFilesAsync(photoPath);
            await page.Locator("[data-room-next]").ClickAsync();
            await Assertions.Expect(page.Locator(".room-handle")).ToHaveCountAsync(4);

            // Klavye ile köşe ayarı: 1. köşeye odaklanıp sağa taşı.
            await page.Locator(".room-handle").First.FocusAsync();
            await page.Keyboard.PressAsync("Shift+ArrowRight");
            await page.Locator("[data-room-width]").FillAsync("420");
            await page.Locator("[data-room-name]").FillAsync("E2E Odam");
            await page.Locator("[data-room-next]").ClickAsync();

            // Adım 3 (isteğe bağlı maske) → atla → sayfa yeni sahneyle yüklenir.
            await page.Locator("[data-room-skip]").ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("scene="));
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
            Assert.Equal(sceneCountBefore + 1, await page.Locator("[data-viz-scene]").CountAsync());
            await Assertions.Expect(page.Locator(".wall-viz-scene.is-active span")).ToHaveTextAsync("E2E Odam");

            // Misafir kendi odasında yüksek kaliteli görsel indiremez → giriş uyarısı.
            await page.Locator("[data-viz-download]").ClickAsync();
            await Assertions.Expect(page.Locator("#wallToast")).ToContainTextAsync("giriş");
        }
        finally
        {
            File.Delete(photoPath);
            // Test sahnesini temizle.
            var scenes = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement;
            foreach (var s in scenes.EnumerateArray().Where(s => s.GetProperty("isUserScene").GetBoolean()))
            {
                var token = await page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
                await context.APIRequest.DeleteAsync($"{fx.BaseUrl}/api/v1/room-previews/{s.GetProperty("id").GetString()}",
                    new() { Headers = new Dictionary<string, string> { ["RequestVerificationToken"] = token ?? "" } });
            }
        }
    }

    [SkippableFact]
    public async Task GecersizUrun_OnerilerleBulunamadiSayfasi()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;

        var response = await page.GotoAsync($"{fx.BaseUrl}/p/olmayan-bir-poster/duvarinda-gor");
        Assert.Equal(404, response!.Status);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Ürün bulunamadı");
        Assert.True(await page.Locator("[data-wall-card]").CountAsync() > 0);
    }
}
