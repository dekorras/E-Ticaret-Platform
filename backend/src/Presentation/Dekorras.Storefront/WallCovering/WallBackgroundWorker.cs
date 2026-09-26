using Dekorras.Application.WallCovering;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Storefront.WallCovering;

/// <summary>Duvar kağıdı modülünün arka plan işleri (spec 1.6.5 / 1.7). Kalıcı iş durumu veritabanındadır
/// (eksik türev = DerivativesGeneratedAtUtc null, eksik sahne küçük resmi = SceneThumbUrl null, bekleyen
/// üretim dosyası = ProductionFile.Pending ...); işçi periyodik olarak yoklar, süreç yeniden başlasa da iş
/// kaybolmaz. VARSAYIM: Hangfire sunucusu yalnızca Dekorras.Api'de çalıştığı için bu işler Storefront
/// sürecinde (yüklenen dosyaların ve sahne katmanlarının bulunduğu yer) BackgroundService ile yürütülür.
/// `WallCovering:BackgroundWorker:Enabled=false` ile kapatılabilir.</summary>
public sealed class WallBackgroundWorker(IServiceScopeFactory scopeFactory, ILogger<WallBackgroundWorker> logger, IConfiguration configuration) : BackgroundService
{
    private const int BatchSize = 20;

    // Başarısız (ör. görseli olmayan) ürünler 1 saat ertelenir; aksi halde her turda kuyruğun başına gelip
    // diğer ürünlerin işlenmesini engellerdi.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _retryAfter = new();

    /// <summary>Diğer işler (ör. üretim dosyası kuyruğu) bu listeye kayıt olur; her turda sırayla çalışır.</summary>
    public static readonly List<Func<IServiceProvider, CancellationToken, Task<int>>> AdditionalJobs = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("WallCovering:BackgroundWorker:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(configuration.GetValue("WallCovering:BackgroundWorker:IntervalSeconds", 30));

        // Uygulama açılışını (migration/seed) geciktirmesin.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await RunOnceAsync(stoppingToken);
                // İş varsa hemen bir sonraki partiye geç, yoksa bekle.
                if (worked == 0) await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Duvar kağıdı arka plan işi başarısız oldu.");
                await Task.Delay(interval, stoppingToken);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var worked = 0;
        using (var scope = scopeFactory.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            worked += await sender.Send(new EnsureWallpaperProfilesCommand(), cancellationToken);
            worked += await sender.Send(new EnsureDefaultRoomScenesCommand(), cancellationToken);
        }

        List<Guid> needDerivatives, needThumbs;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            needDerivatives = await db.WallpaperProfiles.Where(w => w.DerivativesGeneratedAtUtc == null && w.IsEnabled)
                .OrderBy(w => w.CreatedAtUtc).Select(w => w.ProductId).Take(BatchSize + _retryAfter.Count).ToListAsync(cancellationToken);
            needThumbs = await db.WallpaperProfiles.Where(w => w.SceneThumbUrl == null && w.DerivativesGeneratedAtUtc != null && w.IsEnabled)
                .OrderBy(w => w.CreatedAtUtc).Select(w => w.ProductId).Take(BatchSize + _retryAfter.Count).ToListAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        needDerivatives = needDerivatives.Where(id => !_retryAfter.TryGetValue(id, out var until) || until < now).Take(BatchSize).ToList();
        needThumbs = needThumbs.Where(id => !_retryAfter.TryGetValue(id, out var until) || until < now).Take(BatchSize).ToList();

        foreach (var productId in needDerivatives)
            worked += await RunIsolatedAsync(sp => sp.GetRequiredService<ISender>().Send(new GenerateProductDerivativesCommand(productId), cancellationToken), productId, cancellationToken) ? 1 : 0;
        foreach (var productId in needThumbs)
            worked += await RunIsolatedAsync(async sp => await sp.GetRequiredService<ISender>().Send(new GenerateSceneThumbCommand(productId), cancellationToken) is not null, productId, cancellationToken) ? 1 : 0;

        foreach (var job in AdditionalJobs)
        {
            using var scope = scopeFactory.CreateScope();
            worked += await job(scope.ServiceProvider, cancellationToken);
        }

        return worked;
    }

    private async Task<bool> RunIsolatedAsync(Func<IServiceProvider, Task<bool>> work, Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var ok = await work(scope.ServiceProvider);
            if (ok) _retryAfter.TryRemove(productId, out _);
            else _retryAfter[productId] = DateTime.UtcNow.AddHours(1);
            return ok;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tek bir bozuk görsel tüm kuyruğu durdurmasın; ürün 1 saat sonra tekrar denenir.
            logger.LogWarning(ex, "Ürün {ProductId} için duvar görseli işlenemedi.", productId);
            _retryAfter[productId] = DateTime.UtcNow.AddHours(1);
            return false;
        }
    }
}
