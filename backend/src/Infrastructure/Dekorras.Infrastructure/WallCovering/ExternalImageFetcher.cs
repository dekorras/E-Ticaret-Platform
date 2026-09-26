using System.Net;
using System.Net.Sockets;
using Dekorras.Application.WallCovering;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>SSRF korumalı indirici. Katmanlar:
/// 1) yalnızca https, 2) host EmbedClient.AllowedImageHosts listesinde (tam eşleşme), 3) DNS'ten dönen TÜM
/// adresler herkese açık olmalı ve bağlantı, doğrulanan adrese kurulur (ConnectCallback - DNS rebinding'e
/// karşı), 4) yönlendirme izlenmez, 5) 10 sn zaman aşımı ve 10 MB boyut sınırı, 6) içerik türü image/*.</summary>
public sealed class ExternalImageFetcher : IExternalImageFetcher
{
    public const long MaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly HttpMessageHandler? _handlerOverride;

    public ExternalImageFetcher() : this((host, ct) => Dns.GetHostAddressesAsync(host, ct), null) { }

    /// <summary>Testler için: sahte DNS çözücü ve/veya sahte HTTP işleyici.</summary>
    public ExternalImageFetcher(Func<string, CancellationToken, Task<IPAddress[]>> resolve, HttpMessageHandler? handlerOverride)
    {
        _resolve = resolve;
        _handlerOverride = handlerOverride;
    }

    public async Task<byte[]> DownloadAsync(Uri url, IReadOnlyCollection<string> allowedHosts, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new ExternalImageRejectedException("Yalnızca https adreslerinden görsel alınabilir.");
        if (!url.IsDefaultPort) throw new ExternalImageRejectedException("Standart dışı port kullanılamaz.");
        var host = url.IdnHost.ToLowerInvariant();
        if (!allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new ExternalImageRejectedException($"'{host}' bu gömme anahtarı için izinli görsel alan adları arasında değil.");
        if (IPAddress.TryParse(host, out _))
            throw new ExternalImageRejectedException("IP adresiyle görsel alınamaz; alan adı kullanın.");

        var addresses = await _resolve(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new ExternalImageRejectedException("Görsel adresi özel/iç ağ adresine çözümleniyor; izin verilmez.");

        using var handler = _handlerOverride is null ? CreatePinnedHandler(addresses) : null;
        using var client = new HttpClient(_handlerOverride ?? handler!, disposeHandler: false) { Timeout = Timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DekorrasWallPreview/1.0");

        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalImageRejectedException("Görsel indirilemedi veya zaman aşımına uğradı.");
        }

        using (response)
        {
            if ((int)response.StatusCode is >= 300 and < 400) throw new ExternalImageRejectedException("Yönlendirmeler izlenmez; görselin doğrudan adresini kullanın.");
            if (!response.IsSuccessStatusCode) throw new ExternalImageRejectedException($"Görsel sunucusu {(int)response.StatusCode} döndü.");
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new ExternalImageRejectedException("Adres bir görsel döndürmüyor.");
            if (response.Content.Headers.ContentLength > MaxBytes) throw new ExternalImageRejectedException("Görsel 10 MB'dan büyük.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                // Content-Length yalan söyleyebilir: okunan bayt sayısı ayrıca sınırlanır.
                if (buffer.Length + read > MaxBytes) throw new ExternalImageRejectedException("Görsel 10 MB'dan büyük.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    /// <summary>Bağlantı YALNIZCA önceden doğrulanmış adreslere kurulur (ikinci bir DNS sorgusu yapılmaz).</summary>
    private static SocketsHttpHandler CreatePinnedHandler(IPAddress[] verified) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(verified, context.DnsEndPoint.Port, ct);
                if (socket.RemoteEndPoint is IPEndPoint ep && IsPrivate(ep.Address)) throw new ExternalImageRejectedException("İç ağ adresine bağlantı engellendi.");
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 when b[1] is >= 64 and <= 127 => true,          // CGNAT 100.64/10
                169 when b[1] == 254 => true,                        // link-local / bulut metadata
                172 when b[1] is >= 16 and <= 31 => true,
                192 when b[1] == 168 => true,
                192 when b[1] == 0 && b[2] is 0 or 2 => true,
                198 when b[1] is 18 or 19 => true,                   // benchmark
                >= 224 => true,                                      // multicast + ayrılmış
                _ => false
            };
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
               || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;   // fc00::/7 unique local
    }
}
