using System.Security.Cryptography;
using System.Text;
using Dekorras.Application.WallCovering;
using Microsoft.Extensions.Caching.Hybrid;

namespace Dekorras.Infrastructure.WallCovering;

/// <summary>Render önbelleği (spec 1.6.5): render çıktısı dosya depolamaya (/uploads/wall/renders/{özet}.jpg)
/// yazılır, anahtar → URL eşlemesi HybridCache'te etiketlerle tutulur ("scene:{id}", "product:{id}").
/// Anahtar sahne sürümünü ve ürün türevlerinin zamanını içerdiği için güncelleme yeni anahtar üretir;
/// etiketle silme ise eski eşlemeleri anında düşürür.</summary>
public sealed class WallRenderCache(HybridCache cache, IWallImageStore store) : IWallRenderCache
{
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromDays(7), LocalCacheExpiration = TimeSpan.FromHours(6) };

    public async Task<string> GetOrCreateAsync(string key, IReadOnlyCollection<string> tags, Func<CancellationToken, Task<byte[]>> factory, CancellationToken cancellationToken)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40].ToLowerInvariant();
        var relative = $"wall/renders/{fileName[..2]}/{fileName}.jpg";

        return await cache.GetOrCreateAsync(
            $"wall-render:{fileName}",
            (store, relative, factory),
            static async (state, ct) =>
            {
                var url = "/uploads/" + state.relative;
                if (state.store.PublicExists(url)) return url;
                var bytes = await state.factory(ct);
                return await state.store.SavePublicAsync(state.relative, bytes, ct);
            },
            Options,
            tags,
            cancellationToken);
    }

    public async Task InvalidateAsync(string tag, CancellationToken cancellationToken) =>
        await cache.RemoveByTagAsync(tag, cancellationToken);
}
