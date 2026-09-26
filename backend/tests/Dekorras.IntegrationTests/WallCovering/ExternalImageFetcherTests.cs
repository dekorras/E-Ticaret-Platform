using System.Net;
using System.Net.Http.Headers;
using Dekorras.Application.WallCovering;
using Dekorras.Infrastructure.WallCovering;

namespace Dekorras.IntegrationTests.WallCovering;

/// <summary>Harici görselde SSRF engeli (spec 1.12 - "özel IP, izinsiz alan adı, aşırı boyut").
/// Ağ kullanılmaz: sahte DNS çözücü ve sahte HTTP işleyici enjekte edilir.</summary>
public sealed class ExternalImageFetcherTests
{
    private static readonly string[] Allowed = ["cdn.magaza.com"];

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static ExternalImageFetcher Fetcher(string ip, FakeHandler handler) =>
        new((_, _) => Task.FromResult(new[] { IPAddress.Parse(ip) }), handler);

    private static HttpResponseMessage Image(byte[] body, string type = "image/jpeg")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return response;
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.5", true)]
    [InlineData("172.32.0.5", false)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.169.254", true)] // bulut metadata
    [InlineData("100.64.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void OzelAdresSiniflandirmasi(string ip, bool isPrivate)
    {
        Assert.Equal(isPrivate, ExternalImageFetcher.IsPrivate(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task IzinliHostVeHerkeseAcikIp_GorselIndirilir()
    {
        var handler = new FakeHandler(_ => Image([0xFF, 0xD8, 0xFF, 1, 2, 3]));
        var bytes = await Fetcher("93.184.216.34", handler).DownloadAsync(new Uri("https://cdn.magaza.com/a.jpg"), Allowed, CancellationToken.None);
        Assert.Equal(6, bytes.Length);
    }

    [Theory]
    [InlineData("http://cdn.magaza.com/a.jpg")]          // https değil
    [InlineData("https://baska-site.com/a.jpg")]         // izinsiz host
    [InlineData("https://cdn.magaza.com.evil.com/a.jpg")] // son ek hilesi
    [InlineData("https://cdn.magaza.com:8443/a.jpg")]    // standart dışı port
    public async Task IzinsizAdres_Reddedilir_AgaHicCikilmaz(string url)
    {
        var handler = new FakeHandler(_ => Image([1]));
        await Assert.ThrowsAsync<ExternalImageRejectedException>(() =>
            Fetcher("93.184.216.34", handler).DownloadAsync(new Uri(url), Allowed, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task IzinliHostOzelIpyeCozumlenirse_Reddedilir()
    {
        var handler = new FakeHandler(_ => Image([1]));
        await Assert.ThrowsAsync<ExternalImageRejectedException>(() =>
            Fetcher("169.254.169.254", handler).DownloadAsync(new Uri("https://cdn.magaza.com/a.jpg"), Allowed, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Yonlendirme_Izlenmez()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://169.254.169.254/latest") } });
        var ex = await Assert.ThrowsAsync<ExternalImageRejectedException>(() =>
            Fetcher("93.184.216.34", handler).DownloadAsync(new Uri("https://cdn.magaza.com/a.jpg"), Allowed, CancellationToken.None));
        Assert.Contains("Yönlendirme", ex.Message);
    }

    [Fact]
    public async Task AsiriBoyut_ContentLengthYalanSoylesaBile_Reddedilir()
    {
        var big = new byte[ExternalImageFetcher.MaxBytes + 10];
        var handler = new FakeHandler(_ =>
        {
            var r = Image(big);
            r.Content.Headers.ContentLength = null; // sunucu boyut bildirmiyor
            return r;
        });
        await Assert.ThrowsAsync<ExternalImageRejectedException>(() =>
            Fetcher("93.184.216.34", handler).DownloadAsync(new Uri("https://cdn.magaza.com/a.jpg"), Allowed, CancellationToken.None));
    }

    [Fact]
    public async Task GorselOlmayanIcerikTuru_Reddedilir()
    {
        var handler = new FakeHandler(_ => Image("<html>"u8.ToArray(), "text/html"));
        await Assert.ThrowsAsync<ExternalImageRejectedException>(() =>
            Fetcher("93.184.216.34", handler).DownloadAsync(new Uri("https://cdn.magaza.com/a.jpg"), Allowed, CancellationToken.None));
    }
}
