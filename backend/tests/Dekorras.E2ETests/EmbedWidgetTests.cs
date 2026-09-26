using Dekorras.Domain.WallCovering;
using Dekorras.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Gömülebilir widget (spec 1.12 - Embed): izinli origin'de iframe açılır ve postMessage ile ana siteye
/// "sepete ekle" iletilir; izinsiz origin'de iframe yüklenmez (frame-ancestors); CORS yalnızca izinli origine açık.
/// Harici mağaza sayfası test içindeki yerel bir HTTP dinleyicisinden (ayrı port = ayrı origin) sunulur.</summary>
[Collection("storefront")]
public sealed class EmbedWidgetTests(StorefrontFixture fx) : IAsyncLifetime
{
    // Mağaza sayfaları test içindeki gerçek bir yerel HTTP dinleyicisinden (farklı port = farklı origin) sunulur.
    // Playwright'ın sahte yanıtları (route) IP taşımadığından tarayıcı onları "genel" sayar ve Private Network
    // Access kuralı gereği loopback'teki test sunucusundan script yüklemesini engellerdi.
    private static readonly int AllowedPort = FreePort();
    private static readonly int ForbiddenPort = FreePort();
    private static readonly string AllowedOrigin = $"http://localhost:{AllowedPort}";
    private static readonly string ForbiddenOrigin = $"http://localhost:{ForbiddenPort}";
    private readonly List<System.Net.HttpListener> _listeners = [];
    private string _publicKey = "";
    private Guid _clientId;

    private static ApplicationDbContext Db()
    {
        var password = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD") ?? "";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={password};TrustServerCertificate=True;").Options;
        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        if (!fx.IsAvailable) return;
        await using var db = Db();
        var client = new EmbedClient("E2E Mağaza", [AllowedOrigin], ["cdn.magaza.test"], 50);
        db.EmbedClients.Add(client);
        await db.SaveChangesAsync();
        _publicKey = client.PublicKey;
        _clientId = client.Id;
    }

    public async Task DisposeAsync()
    {
        foreach (var l in _listeners) { l.Stop(); l.Close(); }
        if (!fx.IsAvailable) return;
        await using var db = Db();
        await db.EmbedClients.Where(c => c.Id == _clientId).ExecuteDeleteAsync();
    }

    private async Task<IPage> StorePageAsync(IBrowserContext context, string origin, string slug)
    {
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Mağaza</title></head><body>
            <h1>Poster ürünü</h1>
            <a href="#" id="dg" data-duvarinda-gor data-product="{{slug}}">Duvarında Gör</a>
            <script>window.__events = []; document.addEventListener('duvarindagor:add-to-cart', function (e) { window.__events.push(e.detail); });</script>
            <script src="{{fx.BaseUrl}}/embed/duvarinda-gor.js" data-api-key="{{_publicKey}}" defer></script>
            </body></html>
            """;
        Serve(origin, html);
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(20_000);
        await page.GotoAsync($"{origin}/urun");
        // Script gerçekten yüklenmeli - aksi halde "iframe yüklenmedi" testleri yanlış nedenle geçerdi.
        await page.WaitForFunctionAsync("() => !!window.DuvarindaGor");
        return page;
    }

    private void Serve(string origin, string html)
    {
        var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(origin + "/");
        listener.Start();
        _listeners.Add(listener);
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    private static int FreePort()
    {
        var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        socket.Start();
        var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    [SkippableFact]
    public async Task IzinliOriginde_IframeAcilir_SepeteEkleAnaSiteyeIletilir()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        await using var context = await fx.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1400, Height = 900 } });
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var page = await StorePageAsync(context, AllowedOrigin, slug);

        await page.Locator("#dg").ClickAsync();
        var frame = page.FrameLocator("iframe[title='Duvarında Gör']");
        await frame.Locator("[data-wall-visualizer][data-renderer]").WaitForAsync();

        await frame.Locator("[data-viz-add-to-cart]").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__events.length > 0");
        var product = await page.EvaluateAsync<string>("() => window.__events[0].product");
        Assert.Equal(slug, product);

        await frame.Locator("[data-viz-close]").ClickAsync();
        await Assertions.Expect(page.Locator("iframe[title='Duvarında Gör']")).ToBeHiddenAsync();
    }

    [SkippableFact]
    public async Task IzinsizOriginde_IframeYuklenmez_VeCorsKapali()
    {
        Skip.IfNot(fx.IsAvailable, fx.UnavailableReason);
        await using var context = await fx.Browser.NewContextAsync();
        var (slug, _) = await fx.AnyProductAsync(context.APIRequest);
        var page = await StorePageAsync(context, ForbiddenOrigin, slug);

        await page.Locator("#dg").ClickAsync();
        await page.WaitForTimeoutAsync(2500);
        Assert.Equal(1, await page.Locator("iframe[title='Duvarında Gör']").CountAsync()); // iframe açıldı ama içerik engellendi
        // Sunucu 403 verir ve yanıt frame-ancestors 'self' taşır - görüntüleyici hiç yüklenmez.
        Assert.Equal(0, await page.FrameLocator("iframe[title='Duvarında Gör']").Locator("[data-wall-visualizer]").CountAsync());

        var direct = await context.APIRequest.GetAsync($"{fx.BaseUrl}/embed/duvarinda-gor?key={_publicKey}&origin={ForbiddenOrigin}&product={slug}");
        Assert.Equal(403, direct.Status);
        Assert.Contains("frame-ancestors 'self'", direct.Headers["content-security-policy"]);

        var allowed = await context.APIRequest.GetAsync($"{fx.BaseUrl}/embed/duvarinda-gor?key={_publicKey}&origin={AllowedOrigin}&product={slug}");
        Assert.Equal(200, allowed.Status);
        Assert.Equal($"frame-ancestors 'self' {AllowedOrigin}", allowed.Headers["content-security-policy"]);
        Assert.False(allowed.Headers.ContainsKey("x-frame-options"));

        // CORS: izinsiz origin'e ACAO verilmez, izinliye verilir.
        var bad = await context.APIRequest.PostAsync($"{fx.BaseUrl}/api/v1/embed/external-images", new()
        {
            Headers = new Dictionary<string, string> { ["Origin"] = ForbiddenOrigin },
            DataObject = new { key = _publicKey, imageUrl = "https://cdn.magaza.test/a.jpg" }
        });
        Assert.Equal(403, bad.Status);
        Assert.False(bad.Headers.ContainsKey("access-control-allow-origin"));

        var ssrf = await context.APIRequest.PostAsync($"{fx.BaseUrl}/api/v1/embed/external-images", new()
        {
            Headers = new Dictionary<string, string> { ["Origin"] = AllowedOrigin },
            DataObject = new { key = _publicKey, imageUrl = "https://169.254.169.254/latest/meta-data" }
        });
        Assert.Equal(422, ssrf.Status);
        Assert.Equal(AllowedOrigin, ssrf.Headers["access-control-allow-origin"]);
    }
}
