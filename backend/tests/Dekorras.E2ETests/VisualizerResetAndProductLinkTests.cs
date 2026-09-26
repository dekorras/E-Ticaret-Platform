using System.Globalization;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Müşteri geri bildirimleri: 1) görüntüleyicide "Ayarları sıfırla", 2) "Ürün sayfasına git" son seçimleri
/// taşır ve ürün sayfasında ölçü yeniden yazılınca görsel bozulmaz (kırpma şeride küçülmez), 3) üst satırdaki
/// "Duvarımda Dene" ve "Karşılaştır" bağlantıları da "Hesabım"/"Sepetim" gibi dolgulu buton görünümündedir.</summary>
[Collection("storefront")]
public sealed class VisualizerResetAndProductLinkTests(StorefrontFixture fx)
{
    private async Task<(IBrowserContext, IPage)> NewPageAsync()
    {
        var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 950 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        return (context, page);
    }

    [SkippableFact]
    public async Task AyarlariSifirla_TumSecimleriBaslangicaDondurur()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}&w_cm=520&h_cm=210&mirror=1&filter=sepia&align=left&crop=0.1,0.1,0.5,0.2");
        await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        await Assertions.Expect(page.Locator("[data-viz-w]")).ToHaveValueAsync("520");
        await Assertions.Expect(page.Locator("[data-viz-mirror]")).ToBeCheckedAsync();

        await page.Locator("[data-viz-reset]").ClickAsync();
        await Assertions.Expect(page.Locator("#wallToast")).ToContainTextAsync("sıfırlandı");
        Assert.NotEqual("520", await page.Locator("[data-viz-w]").InputValueAsync());
        await Assertions.Expect(page.Locator("[data-viz-mirror]")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#viz-filter-none")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#viz-align-center")).ToBeCheckedAsync();
        await page.WaitForFunctionAsync("() => !location.search.includes('crop=') && !location.search.includes('mirror=') && !location.search.includes('align=')");
    }

    [SkippableFact]
    public async Task UrunSayfasinaGit_SonSecimleriTasir_OlcuYazilincaGorselBozulmaz()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}");
        await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        await page.Locator("[data-viz-w]").FillAsync("420");
        await page.Locator("[data-viz-h]").FillAsync("260");
        await page.Locator("[data-viz-material]").SelectOptionAsync("textured");
        var link = page.Locator("a[data-viz-product-link]").Last;
        await Assertions.Expect(link).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("w_cm=420.*h_cm=260|h_cm=260.*w_cm=420"));
        await Assertions.Expect(link).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("material=textured"));

        await link.ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("/Product/Details"));
        await Assertions.Expect(page.Locator("[data-wall-width]")).ToHaveValueAsync("420");
        await Assertions.Expect(page.Locator("[data-wall-height]")).ToHaveValueAsync("260");
        await Assertions.Expect(page.Locator("#mat-textured")).ToBeCheckedAsync();

        // Ölçüyü karakter karakter yeniden yaz: ara değerler ("4", "40") baskı alanını şeride küçültmemeli.
        var width = page.Locator("[data-wall-width]");
        await width.FillAsync("");
        await width.PressSequentiallyAsync("400", new() { Delay = 150 });
        var height = page.Locator("[data-wall-height]");
        await height.FillAsync("");
        await height.PressSequentiallyAsync("300", new() { Delay = 150 });
        await page.WaitForFunctionAsync("() => new URLSearchParams(location.search).get('w_cm') === '400.0' && new URLSearchParams(location.search).get('h_cm') === '300.0'");
        var crop = await page.EvaluateAsync<string>("() => new URLSearchParams(location.search).get('crop') || document.querySelector('[data-wall-crop-value]').value");
        Assert.False(string.IsNullOrEmpty(crop));
        var c = crop.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        // Varsayılan (tam sığan) alan: kenarlardan biri görselin tamamıdır ve oran 400/300 olmalıdır.
        Assert.True(Math.Max(c[2], c[3]) > 0.99, $"Baskı alanı küçüldü: {crop}");
    }

    [SkippableFact]
    public async Task UstSatirBaglantilari_DolguluButonGorunumunde()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        await page.GotoAsync(fx.BaseUrl + "/");
        var styles = await page.Locator(".dk-header .navbar-nav > li.nav-item > a.nav-link").EvaluateAllAsync<string[]>(
            "els => els.map(e => getComputedStyle(e).backgroundColor + '|' + getComputedStyle(e).borderTopLeftRadius)");
        Assert.Equal(4, styles.Length); // Hesabım, Duvarımda Dene, Karşılaştır, Sepetim
        Assert.Single(styles.Distinct());
        Assert.DoesNotContain("rgba(0, 0, 0, 0)", styles[0]);
    }
}
