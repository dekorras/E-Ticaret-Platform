using Dekorras.Application.WallCovering;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Storefront.WallCovering;

/// <summary>`dotnet run -- backfill-wall-previews [--limit N]`: mevcut ürünler için tek seferlik geri
/// doldurma (spec 1.6.6-F). Adımlar: eksik profiller → varsayılan sahneler → eksik türev görseller →
/// eksik sahne küçük resimleri. Her adım yalnızca EKSİK olanı işler; kesilirse yeniden çalıştırıldığında
/// kaldığı yerden devam eder. Her ürün kendi DI kapsamında işlenir (bellek/EF izleyici şişmesin).</summary>
public static class WallBackfill
{
    public const string CommandName = "backfill-wall-previews";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, CancellationToken cancellationToken = default)
    {
        var limit = ReadLimit(args);

        using (var scope = services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            Console.WriteLine($"[backfill] Oluşturulan duvar kağıdı profili: {await sender.Send(new EnsureWallpaperProfilesCommand(), cancellationToken)}");
            Console.WriteLine($"[backfill] Oluşturulan varsayılan sahne: {await sender.Send(new EnsureDefaultRoomScenesCommand(), cancellationToken)}");
        }

        List<Guid> missingDerivatives, missingThumbs;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            missingDerivatives = await db.WallpaperProfiles.Where(w => w.DerivativesGeneratedAtUtc == null).OrderBy(w => w.CreatedAtUtc).Select(w => w.ProductId).Take(limit).ToListAsync(cancellationToken);
            missingThumbs = await db.WallpaperProfiles.Where(w => w.SceneThumbUrl == null).OrderBy(w => w.CreatedAtUtc).Select(w => w.ProductId).Take(limit).ToListAsync(cancellationToken);
        }

        var ok = 0; var failed = 0;
        foreach (var productId in missingDerivatives)
        {
            if (await TryAsync(services, s => s.Send(new GenerateProductDerivativesCommand(productId), cancellationToken), productId)) ok++; else failed++;
            if ((ok + failed) % 50 == 0) Console.WriteLine($"[backfill] türev: {ok + failed}/{missingDerivatives.Count}");
        }
        Console.WriteLine($"[backfill] Türev üretilen: {ok}, atlanan/başarısız: {failed}");

        ok = 0; failed = 0;
        foreach (var productId in missingThumbs.Union(missingDerivatives))
        {
            if (await TryAsync(services, async s => await s.Send(new GenerateSceneThumbCommand(productId), cancellationToken) is not null, productId)) ok++; else failed++;
            if ((ok + failed) % 50 == 0) Console.WriteLine($"[backfill] sahne: {ok + failed}");
        }
        Console.WriteLine($"[backfill] Sahne küçük resmi üretilen: {ok}, atlanan/başarısız: {failed}");
        return 0;
    }

    private static async Task<bool> TryAsync(IServiceProvider services, Func<ISender, Task<bool>> work, Guid productId)
    {
        try
        {
            using var scope = services.CreateScope();
            return await work(scope.ServiceProvider.GetRequiredService<ISender>());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[backfill] {productId}: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private static int ReadLimit(string[] args)
    {
        var index = Array.IndexOf(args, "--limit");
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var n) && n > 0 ? n : int.MaxValue;
    }
}
