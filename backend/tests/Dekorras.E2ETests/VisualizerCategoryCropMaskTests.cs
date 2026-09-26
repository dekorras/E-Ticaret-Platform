using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Dekorras.E2ETests;

/// <summary>Müşteri geri bildirimleri: 1) görüntüleyicide kategori bazında ürün seçimi (Posterler dahil),
/// 2) kendi oda fotoğrafında duvarın önündeki eşyalar işaretlenince ürün onların ARKASINDA kalır (tam opak maske),
/// 3) sipariş ölçüsü oranında baskı alanı seçimi (çerçeve + duvarda sürükleme) sepete kadar taşınır.</summary>
[Collection("storefront")]
public sealed class VisualizerCategoryCropMaskTests(StorefrontFixture fx)
{
    private static readonly string ScreenshotDir = Path.Combine(Path.GetTempPath(), "dekorras-e2e");

    private async Task<(IBrowserContext, IPage)> NewPageAsync()
    {
        var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 950 } });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        return (context, page);
    }

    [SkippableFact]
    public async Task Goruntuleyici_KategoriSecimi_UrununKokKategorisiyleAcilir_VeListeyiSuzer()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;

        var categories = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/categories")).TextAsync()).RootElement.GetProperty("categories");
        var slugs = categories.EnumerateArray().Select(c => c.GetProperty("slug").GetString()!).ToList();
        Assert.Contains("posterler-141", slugs);
        Assert.Contains("duvar-kagitlari-129", slugs);
        var sub = categories.EnumerateArray().First(c => c.GetProperty("depth").GetInt32() > 0);
        var subSlug = sub.GetProperty("slug").GetString()!;
        var subCount = sub.GetProperty("productCount").GetInt32();

        // Posterler kategorisindeki bir ürünle açılınca liste varsayılan olarak "Posterler"dir.
        var poster = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/products?category=posterler-141&pageSize=1")).TextAsync())
            .RootElement.GetProperty("items")[0].GetProperty("slug").GetString()!;
        var root = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/categories?product={poster}")).TextAsync())
            .RootElement.GetProperty("productCategorySlug").GetString();
        Assert.NotNull(root);

        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={poster}");
        await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        var select = page.Locator("[data-viz-category]");
        await Assertions.Expect(select).ToHaveValueAsync(root!);
        await Assertions.Expect(page.Locator("[data-viz-pick]").First).ToBeVisibleAsync();

        // Alt kategoriye geçince liste yalnızca o kategorinin ürünleriyle yenilenir.
        await select.SelectOptionAsync(subSlug);
        var expected = Math.Min(subCount, 24);
        await Assertions.Expect(page.Locator("[data-viz-pick]")).ToHaveCountAsync(expected);
        var apiSlugs = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/products?category={subSlug}&pageSize=24")).TextAsync())
            .RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()).ToHashSet();
        var shown = await page.Locator("[data-viz-pick]").EvaluateAllAsync<string[]>("els => els.map(e => e.dataset.vizPick)");
        Assert.All(shown, s => Assert.Contains(s, apiSlugs));

        // /duvar-kagitlari kataloğunda da kategori filtresi var.
        await page.GotoAsync($"{fx.BaseUrl}/duvar-kagitlari?category={subSlug}");
        await Assertions.Expect(page.Locator("[data-wall-category]")).ToHaveValueAsync(subSlug);
    }

    [SkippableFact]
    public async Task BaskiAlaniSecimi_CerceveVeDuvardaSurukleme_SepeteKirpmaOlarakGider()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);

        // Varsayılan sahnenin duvarından büyük ölçü → "duvara eşitle" bağlantısı çıkar.
        await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}&w_cm=900&h_cm=250");
        await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
        var fitWall = page.Locator("[data-viz-fit-wall]");
        await Assertions.Expect(fitWall).ToBeVisibleAsync();
        await fitWall.ClickAsync();

        // Pencere açılır; ölçü duvara eşitlenmiştir, çerçeve küçültülüp klavyeyle kaydırılır.
        var dialog = page.Locator("dialog.wall-crop-dialog[open]");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        Assert.NotEqual("900", await page.Locator("[data-viz-w]").InputValueAsync());
        await dialog.Locator("[data-crop-zoom]").FillAsync("250");
        var frame = dialog.Locator("[data-crop-frame]");
        var before = await frame.GetAttributeAsync("style");
        await frame.FocusAsync();
        for (var i = 0; i < 5; i++) await page.Keyboard.PressAsync("Shift+ArrowRight");
        Assert.NotEqual(before, await frame.GetAttributeAsync("style"));
        Directory.CreateDirectory(ScreenshotDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "baski-alani.png") });
        await dialog.Locator("[data-crop-apply]").ClickAsync();
        await Assertions.Expect(dialog).ToHaveCountAsync(0);
        await page.WaitForURLAsync(u => u.Contains("crop="));
        var cropAfterDialog = new Uri(page.Url).Query;

        // Duvar önizlemesinde ürünü sürüklemek baskı alanını kaydırır.
        var canvas = page.Locator("[data-viz-canvas]");
        var box = (await canvas.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height * 0.45f);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 80, box.Y + box.Height * 0.45f, new() { Steps = 6 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync("q => location.search !== q", cropAfterDialog);
        var crop = System.Web.HttpUtility.ParseQueryString(new Uri(page.Url).Query)["crop"];
        Assert.False(string.IsNullOrEmpty(crop));

        // Sepete eklenen kalem bu kırpmayı taşır (üretim dosyası bu alandan basılır).
        await page.Locator("[data-viz-add-to-cart]").ClickAsync();
        await Assertions.Expect(page.Locator("#wallToast")).ToContainTextAsync("Sepete eklendi");
        var cart = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/cart")).TextAsync()).RootElement;
        var item = cart.GetProperty("items").EnumerateArray().First(i => i.GetProperty("configuration").ValueKind == JsonValueKind.Object);
        Assert.Equal(crop, item.GetProperty("configuration").GetProperty("crop").GetString());
    }

    [SkippableFact]
    public async Task KendiOdam_KoltukOtomatikBulunur_UrunArkasindaKalir_GolgeLekesiOlusmaz()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var photoPath = Path.Combine(Path.GetTempPath(), $"oda-koltuk-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(photoPath, TestImages.RoomWithChairJpeg(1200, 800));
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}");
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
            await page.Locator("[data-viz-upload-room]").ClickAsync();
            await page.Locator("[data-room-file]").SetInputFilesAsync(photoPath);
            await page.Locator("[data-room-next]").ClickAsync();
            await Assertions.Expect(page.Locator(".room-handle")).ToHaveCountAsync(4);
            await page.Locator("[data-room-width]").FillAsync("400");
            await page.Locator("[data-room-next]").ClickAsync();

            // Adım 3 açılır açılmaz koltuk otomatik bulunur (müşteri hiçbir şey çizmez).
            await Assertions.Expect(page.Locator("[data-room-auto-status]")).ToContainTextAsync("otomatik işaretlendi");
        Directory.CreateDirectory(ScreenshotDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "koltuk-otomatik.png") });
            await page.Locator("[data-room-next]").ClickAsync();
            await page.WaitForFunctionAsync("() => !document.querySelector('[data-room-mask-canvas]') || !!document.querySelector('[data-room-error]:not([hidden])')");
            var roomError = page.Locator("[data-room-error]:not([hidden])");
            if (await roomError.CountAsync() > 0) Assert.Fail("Maske kaydedilemedi: " + await roomError.TextContentAsync());
            await Assertions.Expect(page.Locator(".wall-viz-scene.is-active span")).ToHaveTextAsync("Odam");

            var mine = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement
                .EnumerateArray().Single(s => s.GetProperty("isUserScene").GetBoolean());
            using (var mask = Image.Load<Rgba32>(await (await context.APIRequest.GetAsync(fx.BaseUrl + mine.GetProperty("foregroundMaskUrl").GetString())).BodyAsync()))
            {
                // Koltuğun duvar dörtgeni içindeki kısmı tam opak; boş duvar saydam.
                Assert.Equal(255, mask[(int)(mask.Width * 0.65), (int)(mask.Height * 0.65)].A);
                Assert.Equal(0, mask[(int)(mask.Width * 0.30), (int)(mask.Height * 0.30)].A);
                Assert.Equal(0, mask[(int)(mask.Width * 0.45), (int)(mask.Height * 0.65)].A);
            }
            using (var shadow = Image.Load<L8>(await (await context.APIRequest.GetAsync(fx.BaseUrl + mine.GetProperty("shadowMapUrl").GetString())).BodyAsync()))
            {
                // Koltuğun hemen üstünde ve yanında posterde koyu leke yok (eski yöntemde ~90–150'ye düşüyordu).
                Assert.True(shadow[(int)(shadow.Width * 0.65), (int)(shadow.Height * 0.44)].PackedValue > 225);
                Assert.True(shadow[(int)(shadow.Width * 0.50), (int)(shadow.Height * 0.65)].PackedValue > 225);
            }
            await page.WaitForTimeoutAsync(800);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "koltuk-onunde.png") });

            // "Ayarları sıfırla" kendi odasındaki eşya işaretlemesini de (onayla) siler; gölge haritası yenilenir.
            string? dialogText = null;
            page.Dialog += async (_, d) => { dialogText = d.Message; await d.AcceptAsync(); };
            var shadowBefore = mine.GetProperty("shadowMapUrl").GetString();
            await page.Locator("[data-viz-reset]").ClickAsync();
            await Assertions.Expect(page.Locator("#wallToast")).ToContainTextAsync("eşya işaretlemesi sıfırlandı");
            Assert.Contains("işaretleme de silinecek", dialogText);
            var afterReset = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement
                .EnumerateArray().Single(s => s.GetProperty("isUserScene").GetBoolean());
            Assert.Equal(JsonValueKind.Null, afterReset.GetProperty("foregroundMaskUrl").ValueKind);
            Assert.NotEqual(shadowBefore, afterReset.GetProperty("shadowMapUrl").GetString());
        }
        finally
        {
            File.Delete(photoPath);
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
    public async Task EsyaIsaretleme_TamEkran_DuzenlemedeOtomatikBulGorunur_YakinlastirVeKaydir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var photoPath = Path.Combine(Path.GetTempPath(), $"oda-zoom-{Guid.NewGuid():N}.jpg");
        Directory.CreateDirectory(ScreenshotDir);
        await File.WriteAllBytesAsync(photoPath, TestImages.RoomWithChairJpeg(1200, 800));
        try
        {
            // Odayı işaretlemeden kaydet ("eşya yok, devam et").
            await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}");
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
            await page.Locator("[data-viz-upload-room]").ClickAsync();
            await page.Locator("[data-room-file]").SetInputFilesAsync(photoPath);
            await page.Locator("[data-room-next]").ClickAsync();
            await page.Locator("[data-room-width]").FillAsync("400");
            await page.Locator("[data-room-next]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-room-auto-status]")).ToContainTextAsync("otomatik işaretlendi");
            await page.Locator("[data-room-skip]").ClickAsync();
            await Assertions.Expect(page.Locator(".wall-viz-scene.is-active span")).ToHaveTextAsync("Odam");

            // Sonradan düzenleme: pencere tam ekran; "Eşyaları otomatik bul" turuncu alanı GÖRÜNÜR biçimde çizer.
            await page.Locator("[data-viz-edit-mask]").ClickAsync();
            await Assertions.Expect(page.Locator(".modal.show .modal-fullscreen")).ToBeVisibleAsync();
            var viewport = page.Locator("[data-room-viewport]");
            var vpBox = (await viewport.BoundingBoxAsync())!;
            Assert.True(vpBox.Height > 400, $"Görüş alanı ekranı kaplamıyor: {vpBox.Height}px");
            await page.WaitForFunctionAsync("() => document.querySelector('[data-room-mask-canvas]').width > 0");
            await page.Locator("[data-room-auto]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-room-auto-status]")).ToContainTextAsync("otomatik işaretlendi");
            var orange = await page.EvaluateAsync<int>(@"() => {
                const c = document.querySelector('[data-room-mask-canvas]'); const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
                let n = 0; for (let i = 3; i < d.length; i += 4) if (d[i] > 200) n++; return n; }");
            Assert.True(orange > 1000, $"Turuncu işaret yok: {orange}");
            var canvasBox = (await page.Locator("[data-room-mask-canvas]").BoundingBoxAsync())!;
            Assert.True(canvasBox.Width > 300 && canvasBox.Height > 200, "Maske katmanı fotoğrafın üstünü kaplamıyor.");
        Directory.CreateDirectory(ScreenshotDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "isaretleme-tam-ekran.png") });

            // Yakınlaştır (tekerlek) → etiket büyür; El aracıyla kaydır → sahne yer değiştirir.
            await page.Mouse.MoveAsync(vpBox.X + vpBox.Width / 2, vpBox.Y + vpBox.Height / 2);
            for (var i = 0; i < 5; i++) await page.Mouse.WheelAsync(0, -120);
            var label = await page.Locator("[data-room-zoom-label]").TextContentAsync();
            Assert.True(int.Parse(label!.TrimEnd('%')) > 150, $"Yakınlaşmadı: {label}");
            await page.Locator("label[for=roomToolPan]").ClickAsync();
            var before = await page.Locator("[data-room-mask-editor]").GetAttributeAsync("style");
            await page.Mouse.MoveAsync(vpBox.X + vpBox.Width / 2, vpBox.Y + vpBox.Height / 2);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(vpBox.X + vpBox.Width / 2 + 150, vpBox.Y + vpBox.Height / 2 + 80, new() { Steps = 5 });
            await page.Mouse.UpAsync();
            Assert.NotEqual(before, await page.Locator("[data-room-mask-editor]").GetAttributeAsync("style"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "isaretleme-yakinlastirma.png") });

            // Yakınlaştırılmışken fırça fotoğrafta doğru yere boyar: ekran merkezindeki fotoğraf noktası işaretlenir.
            await page.Locator("[data-room-clear]").ClickAsync();
            await page.Locator("label[for=roomToolPaint]").ClickAsync();
            var cx = vpBox.X + vpBox.Width / 2; var cy = vpBox.Y + vpBox.Height / 2;
            await page.Mouse.ClickAsync(cx, cy);
            // Tıklanan ekran noktasının fotoğraftaki karşılığı (yakınlaştırma/kaydırma dahil) boyanmış olmalı.
            var alpha = await page.EvaluateAsync<int>(@"(p) => {
                const c = document.querySelector('[data-room-mask-canvas]'); const r = c.getBoundingClientRect();
                const x = Math.round((p.x - r.left) * c.width / r.width), y = Math.round((p.y - r.top) * c.height / r.height);
                return c.getContext('2d').getImageData(x, y, 1, 1).data[3]; }", new { x = (double)cx, y = (double)cy });
            Assert.Equal(255, alpha);
            await page.Locator("[data-room-zoom-fit]").ClickAsync();
            await Assertions.Expect(page.Locator("[data-room-zoom-label]")).ToHaveTextAsync("100%");
        }
        finally
        {
            File.Delete(photoPath);
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
    public async Task KendiOdam_EsyaCevresiniCiz_MaskeTamOpak_SonradanDuzenlenebilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        var (context, page) = await NewPageAsync();
        await using var _ = context;
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var photoPath = Path.Combine(Path.GetTempPath(), $"oda-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(photoPath, TestImages.SolidJpeg(1200, 800));
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/duvarinda-gor?product={slug}");
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();
            await page.Locator("[data-viz-upload-room]").ClickAsync();
            await page.Locator("[data-room-file]").SetInputFilesAsync(photoPath);
            await page.Locator("[data-room-next]").ClickAsync();
            await Assertions.Expect(page.Locator(".room-handle")).ToHaveCountAsync(4);
            await page.Locator("[data-room-width]").FillAsync("400");
            await page.Locator("[data-room-next]").ClickAsync();

            // Adım 3: "Çevresini çiz" (varsayılan) ile bir "koltuk" alanı: 4 nokta + ilk noktaya tıklayarak kapat.
            var maskCanvas = page.Locator("[data-room-mask-canvas]");
            await Assertions.Expect(maskCanvas).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('[data-room-mask-canvas]').width > 0");
            var mb = (await maskCanvas.BoundingBoxAsync())!;
            (float X, float Y) P(float fx, float fy) => (mb.X + mb.Width * fx, mb.Y + mb.Height * fy);
            foreach (var (x, y) in new[] { P(0.30f, 0.55f), P(0.60f, 0.55f), P(0.60f, 0.95f), P(0.30f, 0.95f), P(0.30f, 0.55f) })
                await page.Mouse.ClickAsync(x, y);
        Directory.CreateDirectory(ScreenshotDir);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "esya-isaretleme.png") });
            await page.Locator("[data-room-next]").ClickAsync();
            // Kaydedince pencere kapanır ve sayfa yeni odayla (etkin sahne) yeniden yüklenir; hata varsa pencerede görünür.
            var roomError = page.Locator("[data-room-error]:not([hidden])");
            await page.WaitForFunctionAsync("() => !document.querySelector('[data-room-mask-canvas]') || !!document.querySelector('[data-room-error]:not([hidden])')");
            if (await roomError.CountAsync() > 0) Assert.Fail("Maske kaydedilemedi: " + await roomError.TextContentAsync());
            await Assertions.Expect(page.Locator(".wall-viz-scene.is-active span")).ToHaveTextAsync("Odam");
            await page.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();

            // Kaydedilen maske: işaretli alan TAM OPAK (ürün eşyanın arkasında kalır), dışı tamamen saydam.
            var scenes = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement;
            var mine = scenes.EnumerateArray().Single(s => s.GetProperty("isUserScene").GetBoolean());
            var maskUrl = mine.GetProperty("foregroundMaskUrl").GetString();
            Assert.False(string.IsNullOrEmpty(maskUrl));
            var maskBytes = await (await context.APIRequest.GetAsync(fx.BaseUrl + maskUrl)).BodyAsync();
            using (var mask = Image.Load<Rgba32>(maskBytes))
            {
                Assert.Equal(255, mask[(int)(mask.Width * 0.45), (int)(mask.Height * 0.75)].A);
                Assert.Equal(0, mask[(int)(mask.Width * 0.85), (int)(mask.Height * 0.2)].A);
            }
            await page.WaitForTimeoutAsync(800);
            await page.ScreenshotAsync(new() { Path = Path.Combine(ScreenshotDir, "esya-onunde.png") });

            // Sonradan düzenleme: görüntüleyicideki düğme mevcut maskeyle açılır; "tümünü temizle" maskeyi kaldırır.
            var edit = page.Locator("[data-viz-edit-mask]");
            await Assertions.Expect(edit).ToBeVisibleAsync();
            await edit.ClickAsync();
            await Assertions.Expect(page.Locator("#roomUploadTitle")).ToHaveTextAsync("Duvarın önündeki eşyaları işaretle");
            await page.Locator("[data-room-clear]").ClickAsync();
            await page.Locator("[data-room-next]").ClickAsync();
            await Assertions.Expect(page.Locator("#wallToast")).ToContainTextAsync("işaretlendi");
            var after = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement
                .EnumerateArray().Single(s => s.GetProperty("isUserScene").GetBoolean()).GetProperty("foregroundMaskUrl").GetString();
            using (var cleared = Image.Load<Rgba32>(await (await context.APIRequest.GetAsync(fx.BaseUrl + after)).BodyAsync()))
                Assert.Equal(0, cleared[(int)(cleared.Width * 0.45), (int)(cleared.Height * 0.75)].A);
        }
        finally
        {
            File.Delete(photoPath);
            var scenes = JsonDocument.Parse(await (await context.APIRequest.GetAsync($"{fx.BaseUrl}/api/v1/scenes")).TextAsync()).RootElement;
            foreach (var s in scenes.EnumerateArray().Where(s => s.GetProperty("isUserScene").GetBoolean()))
            {
                var token = await page.Locator("input[name='__RequestVerificationToken']").First.GetAttributeAsync("value");
                await context.APIRequest.DeleteAsync($"{fx.BaseUrl}/api/v1/room-previews/{s.GetProperty("id").GetString()}",
                    new() { Headers = new Dictionary<string, string> { ["RequestVerificationToken"] = token ?? "" } });
            }
        }
    }
}
