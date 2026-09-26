using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record SceneDto(
    Guid Id,
    string Name,
    string RoomType,
    string BaseImageUrl,
    string? ShadowMapUrl,
    string? ForegroundMaskUrl,
    int ImageWidthPx,
    int ImageHeightPx,
    double[] WallQuad,
    decimal RealWallWidthCm,
    decimal RealWallHeightCm,
    bool IsDefault,
    bool IsUserScene,
    int Version);

/// <summary>Aktif hazır sahneler (varsayılan önce) + (OwnerKey verilirse) kullanıcının kendi sahneleri.</summary>
public sealed record GetRoomScenesQuery(string? OwnerKey = null) : IRequest<IReadOnlyList<SceneDto>>;

public sealed class GetRoomScenesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetRoomScenesQuery, IReadOnlyList<SceneDto>>
{
    public Task<IReadOnlyList<SceneDto>> Handle(GetRoomScenesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<SceneDto> scenes = unitOfWork.Repository<RoomScene>().Query()
            .Where(s => s.IsActive && (s.OwnerKey == null || (request.OwnerKey != null && s.OwnerKey == request.OwnerKey)))
            .OrderBy(s => s.OwnerKey != null).ThenByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToList()
            .Select(ToDto)
            .ToList();
        return Task.FromResult(scenes);
    }

    public static SceneDto ToDto(RoomScene s) => new(
        s.Id, s.Name, s.RoomType.ToString(), s.BaseImageUrl, s.ShadowMapUrl, s.ForegroundMaskUrl, s.ImageWidthPx, s.ImageHeightPx,
        [s.TopLeftX, s.TopLeftY, s.TopRightX, s.TopRightY, s.BottomRightX, s.BottomRightY, s.BottomLeftX, s.BottomLeftY],
        s.RealWallWidthCm, s.RealWallHeightCm, s.IsDefault, s.IsUserScene, s.Version);
}

/// <summary>Hiç hazır sahne yoksa prosedürel varsayılan sahneleri üretip kaydeder (ilki varsayılan).</summary>
public sealed record EnsureDefaultRoomScenesCommand : IRequest<int>;

public sealed class EnsureDefaultRoomScenesCommandHandler(IUnitOfWork unitOfWork, IDefaultSceneGenerator generator) : IRequestHandler<EnsureDefaultRoomScenesCommand, int>
{
    public async Task<int> Handle(EnsureDefaultRoomScenesCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<RoomScene>();
        if (repository.Query().Any(s => s.OwnerKey == null)) return 0;

        var generated = await generator.GenerateAsync(cancellationToken);
        var order = 0;
        foreach (var g in generated)
        {
            var scene = new RoomScene(g.Name, g.RoomType, g.BaseImageUrl, g.WidthPx, g.HeightPx, g.WallQuad, g.RealWallWidthCm, g.RealWallHeightCm);
            scene.SetImages(g.BaseImageUrl, g.WidthPx, g.HeightPx, g.ShadowMapUrl, g.ForegroundMaskUrl);
            scene.Update(g.Name, g.RoomType, order);
            scene.SetDefault(order == 0);
            order++;
            await repository.AddAsync(scene, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return generated.Count;
    }
}

public sealed record RenderWallPreviewResult(string Url, bool Clipped);

/// <summary>Sunucu render'ı (spec 1.9 - GET /scenes/{id}/render). Sonuç önbelleklenir; anahtar ürün
/// türev zamanı + sahne sürümü + konfigürasyon özeti + hiza + boyut + filigran içerir.</summary>
public sealed record RenderWallPreviewQuery(
    Guid SceneId,
    Guid ProductId,
    WallConfiguration Configuration,
    WallAlign Align,
    int OutputWidthPx,
    bool Watermark = true,
    bool PanelLines = false,
    string? OwnerKey = null) : IRequest<RenderWallPreviewResult>;

public sealed class RenderWallPreviewQueryHandler(IUnitOfWork unitOfWork, IPricingService pricingService, IWallPreviewRenderer renderer, IWallRenderCache cache)
    : IRequestHandler<RenderWallPreviewQuery, RenderWallPreviewResult>
{
    public async Task<RenderWallPreviewResult> Handle(RenderWallPreviewQuery request, CancellationToken cancellationToken)
    {
        var scene = unitOfWork.Repository<RoomScene>().Query()
            .FirstOrDefault(s => s.Id == request.SceneId && s.IsActive && (s.OwnerKey == null || s.OwnerKey == request.OwnerKey))
            ?? throw new KeyNotFoundException("Sahne bulunamadı.");

        // Ölçü/malzeme kuralları fiyatlamayla aynı yerden doğrulanır.
        var quote = pricingService.QuoteLine(request.ProductId, request.Configuration, 1);
        if (!quote.IsValid) throw new WallConfigurationException(quote.Errors);

        var poster = LoadPosterSource(unitOfWork, request.ProductId) ?? throw new KeyNotFoundException("Poster görseli bulunamadı.");
        var width = Math.Clamp(request.OutputWidthPx, 320, 2400);
        var placement = WallLayout.Place((double)scene.RealWallWidthCm, (double)scene.RealWallHeightCm,
            (double)request.Configuration.WidthCm, (double)request.Configuration.HeightCm, request.Align);

        var key = string.Join("|", "v1", request.ProductId, poster.Stamp, scene.Id, scene.Version, request.Configuration.Hash(),
            request.Align, width, request.Watermark, request.PanelLines);
        var sceneData = ToRenderData(scene);

        // Kendi oda fotoğrafı: yüksek kaliteli sunucu render'ı üyelere ve kota dahilinde verilir (spec 1.6.4).
        var quota = scene.IsUserScene ? CheckQuota(request.OwnerKey!) : null;

        var url = await cache.GetOrCreateAsync(key, [$"scene:{scene.Id}", $"product:{request.ProductId}"], ct =>
            renderer.RenderAsync(new WallRenderRequest(sceneData, poster.Source, request.Configuration, request.Align,
                quote.Line!.PanelWidthCm, quote.Line.BleedCm, request.PanelLines, request.Watermark, width), ct), cancellationToken);

        if (quota is not null)
        {
            var renders = unitOfWork.Repository<RoomPreviewRender>();
            // Aynı sonuç ikinci kez istenirse (ör. tekrar indirme) kotadan tekrar düşülmez.
            if (!renders.Query().Any(r => r.OwnerKey == request.OwnerKey && r.ResultUrl == url))
            {
                if (quota.Remaining <= 0)
                    throw new RoomPreviewQuotaException($"Ücretsiz oda önizleme hakkınız ({quota.Limit}) doldu.", false);
                await renders.AddAsync(new RoomPreviewRender(request.OwnerKey!, scene.Id, request.ProductId, url), cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        return new RenderWallPreviewResult(url, placement.IsClipped);
    }

    private RoomQuotaDto CheckQuota(string ownerKey)
    {
        if (!ownerKey.StartsWith("c:"))
            throw new RoomPreviewQuotaException("Kendi odanızda yüksek kaliteli görsel indirmek için giriş yapın.", true);
        var limit = WallCoveringSettings.Load(unitOfWork).RoomPreviewFreeQuota;
        var used = unitOfWork.Repository<RoomPreviewRender>().Query().Count(r => r.OwnerKey == ownerKey);
        return new RoomQuotaDto(true, used, limit, limit - used, false);
    }

    public static SceneRenderData ToRenderData(RoomScene s) => new(
        s.Id, s.Version, s.BaseImageUrl, s.ShadowMapUrl, s.ForegroundMaskUrl, s.ImageWidthPx, s.ImageHeightPx, s.WallQuad, s.RealWallWidthCm, s.RealWallHeightCm);

    /// <summary>Render kaynağı: özel orijinal (varsa) → filigransız liste türevi değil, ürün görseli.
    /// Stamp, türevler yeniden üretildiğinde önbellek anahtarını değiştirir.</summary>
    public static (PosterSource Source, long Stamp)? LoadPosterSource(IUnitOfWork unitOfWork, Guid productId)
    {
        var row = unitOfWork.Repository<WallpaperProfile>().Query()
            .Where(w => w.ProductId == productId)
            .Select(w => new
            {
                w.OriginalImageKey,
                w.ProductType,
                w.RepeatWidthCm,
                w.RepeatHeightCm,
                w.RepeatType,
                w.DerivativesGeneratedAtUtc,
                FallbackImage = unitOfWork.Repository<ProductImage>().Query()
                    .Where(i => i.ProductId == productId).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault()
            })
            .FirstOrDefault();
        if (row is null || (row.OriginalImageKey is null && row.FallbackImage is null)) return null;

        return (new PosterSource(row.OriginalImageKey, row.FallbackImage, row.ProductType, row.RepeatWidthCm, row.RepeatHeightCm, row.RepeatType),
            row.DerivativesGeneratedAtUtc?.Ticks ?? 0);
    }
}
