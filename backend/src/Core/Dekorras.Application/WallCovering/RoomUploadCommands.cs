using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Common;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>Oda önizleme kotası aşıldığında veya misafir yüksek kaliteli render istediğinde.</summary>
public sealed class RoomPreviewQuotaException(string message, bool requiresLogin) : Exception(message)
{
    public bool RequiresLogin { get; } = requiresLogin;
}

public sealed record RoomQuotaDto(bool IsMember, int Used, int Limit, int Remaining, bool RequiresLogin);

/// <summary>Kendi oda fotoğrafında önizleme kotası (spec 1.6.4). Yalnızca sunucuda üretilen yüksek
/// kaliteli render/indirme sayılır; istemcide poster değiştirmek kotadan düşmez.
/// VARSAYIM: kota üyelik ömrü boyunca toplamdır (aylık sıfırlanmaz) ve misafire sunucu render'ı verilmez.</summary>
public sealed record GetRoomQuotaQuery(string OwnerKey) : IRequest<RoomQuotaDto>;

public sealed class GetRoomQuotaQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetRoomQuotaQuery, RoomQuotaDto>
{
    public Task<RoomQuotaDto> Handle(GetRoomQuotaQuery request, CancellationToken cancellationToken)
    {
        var limit = WallCoveringSettings.Load(unitOfWork).RoomPreviewFreeQuota;
        var isMember = request.OwnerKey.StartsWith("c:");
        var used = isMember ? unitOfWork.Repository<RoomPreviewRender>().Query().Count(r => r.OwnerKey == request.OwnerKey) : 0;
        return Task.FromResult(new RoomQuotaDto(isMember, used, limit, Math.Max(0, limit - used), !isMember));
    }
}

/// <summary>Kullanıcının oda fotoğrafını özel sahne olarak kaydeder. Fotoğraf içerikten doğrulanır
/// (JPG/PNG/WebP), EXIF temizlenip yeniden kodlanır; köşeler yeniden ölçeklenen fotoğrafa uyarlanır ve
/// fotoğraftan otomatik gölge haritası üretilir.</summary>
public sealed record CreateUserRoomSceneCommand(
    string OwnerKey,
    Stream Photo,
    double[] Corners,
    int ClientImageWidthPx,
    int ClientImageHeightPx,
    decimal RealWallWidthCm,
    decimal? RealWallHeightCm,
    string? Name) : IRequest<SceneDto>;

public sealed class CreateUserRoomSceneCommandHandler(IUnitOfWork unitOfWork, IImageDerivativeService images, IWallImageStore store)
    : IRequestHandler<CreateUserRoomSceneCommand, SceneDto>
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxScenesPerOwner = 10;
    private const int MaxLongEdgePx = 2400;

    public async Task<SceneDto> Handle(CreateUserRoomSceneCommand request, CancellationToken cancellationToken)
    {
        if (request.Corners.Length != 8 || request.Corners.Any(double.IsNaN))
            throw new WallConfigurationException(new Dictionary<string, string> { ["corners"] = "Duvarın 4 köşesini işaretleyin." });
        if (request.RealWallWidthCm < WallDimensions.MinSideCm || request.RealWallWidthCm > WallDimensions.MaxWidthCm)
            throw new WallConfigurationException(new Dictionary<string, string> { ["width"] = "Duvar genişliği 10–2000 cm arasında olmalıdır." });

        var existing = unitOfWork.Repository<RoomScene>().Query().Count(s => s.OwnerKey == request.OwnerKey && s.IsActive);
        if (existing >= MaxScenesPerOwner)
            throw new WallConfigurationException(new Dictionary<string, string> { ["photo"] = $"En fazla {MaxScenesPerOwner} oda fotoğrafı kaydedebilirsiniz. Eskilerden birini silin." });

        using var buffer = new MemoryStream();
        await request.Photo.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxBytes)
            throw new WallConfigurationException(new Dictionary<string, string> { ["photo"] = "Fotoğraf en fazla 10 MB olabilir." });
        buffer.Position = 0;
        var inspection = images.Inspect(buffer);
        if (!inspection.IsValid)
            throw new WallConfigurationException(new Dictionary<string, string> { ["photo"] = inspection.Error ?? "Geçersiz görsel." });

        buffer.Position = 0;
        var (jpeg, width, height) = await images.SanitizeAsync(buffer, MaxLongEdgePx, cancellationToken);

        // İstemcinin işaretlediği köşeler (kendi gösterdiği görsel boyutunda) yeniden kodlanmış fotoğrafa ölçeklenir.
        var sx = width / (double)Math.Max(1, request.ClientImageWidthPx);
        var sy = height / (double)Math.Max(1, request.ClientImageHeightPx);
        var c = request.Corners;
        var quad = new WallQuad(new Point2(c[0] * sx, c[1] * sy), new Point2(c[2] * sx, c[3] * sy), new Point2(c[4] * sx, c[5] * sy), new Point2(c[6] * sx, c[7] * sy));
        if (!quad.IsConvex())
            throw new WallConfigurationException(new Dictionary<string, string> { ["corners"] = "Köşeler bir dörtgen oluşturmalı (sol üst, sağ üst, sağ alt, sol alt sırasıyla)." });

        // Yükseklik girilmediyse dörtgenin en-boy oranından tahmin edilir.
        // VARSAYIM: işaretlenen dörtgenin ortalama genişlik/yükseklik oranı gerçek duvar oranına yakındır.
        var realHeight = request.RealWallHeightCm is >= WallDimensions.MinSideCm
            ? request.RealWallHeightCm.Value
            : Math.Round(request.RealWallWidthCm * (decimal)(quad.AverageHeightPx / quad.AverageWidthPx), 1);

        var folder = $"wall/rooms/{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.OwnerKey)))[..16].ToLowerInvariant()}";
        var id = Guid.NewGuid().ToString("N");
        var baseUrl = await store.SavePublicAsync($"{folder}/{id}.jpg", jpeg, cancellationToken);
        var shadowUrl = await store.SavePublicAsync($"{folder}/{id}-shadow.png", await images.CreateShadowMapAsync(jpeg, quad, null, cancellationToken), cancellationToken);

        RoomScene scene;
        try
        {
            scene = new RoomScene(string.IsNullOrWhiteSpace(request.Name) ? "Odam" : request.Name.Trim()[..Math.Min(60, request.Name.Trim().Length)],
                RoomType.Other, baseUrl, width, height, quad, request.RealWallWidthCm, realHeight, request.OwnerKey);
        }
        catch (DomainException ex)
        {
            await store.DeletePublicAsync(baseUrl, cancellationToken);
            await store.DeletePublicAsync(shadowUrl, cancellationToken);
            throw new WallConfigurationException(new Dictionary<string, string> { ["corners"] = ex.Message });
        }
        scene.SetImages(baseUrl, width, height, shadowUrl, null);

        await unitOfWork.Repository<RoomScene>().AddAsync(scene, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return GetRoomScenesQueryHandler.ToDto(scene);
    }
}

/// <summary>"Fırça ile maske boya" aracının çıktısı: kullanıcının kendi sahnesine ön plan maskesi.</summary>
public sealed record SetUserRoomMaskCommand(string OwnerKey, Guid SceneId, Stream MaskPng) : IRequest<SceneDto>;

/// <summary>Kullanıcının kendi sahnesini siler (hazır sahnelere dokunulamaz).</summary>
public sealed record DeleteUserRoomSceneCommand(string OwnerKey, Guid SceneId) : IRequest<Unit>;

/// <summary>"Ayarları sıfırla": kendi odasındaki eşya işaretlemesini (ön plan maskesi) kaldırır; gölge haritası
/// maskesiz yeniden üretilir.</summary>
public sealed record ClearUserRoomMaskCommand(string OwnerKey, Guid SceneId) : IRequest<SceneDto>;

public sealed class UserRoomSceneCommandHandler(IUnitOfWork unitOfWork, IImageDerivativeService images, IWallImageStore store, IWallRenderCache renderCache) :
    IRequestHandler<SetUserRoomMaskCommand, SceneDto>,
    IRequestHandler<ClearUserRoomMaskCommand, SceneDto>,
    IRequestHandler<DeleteUserRoomSceneCommand, Unit>
{
    public async Task<SceneDto> Handle(ClearUserRoomMaskCommand request, CancellationToken cancellationToken)
    {
        var scene = Owned(request.OwnerKey, request.SceneId);
        if (scene.ForegroundMaskUrl is null) return GetRoomScenesQueryHandler.ToDto(scene);

        var shadowUrl = await RegenerateShadowAsync(scene, null, DateTime.UtcNow.Ticks.ToString("x"), cancellationToken);
        await store.DeletePublicAsync(scene.ForegroundMaskUrl, cancellationToken);
        scene.SetImages(scene.BaseImageUrl, scene.ImageWidthPx, scene.ImageHeightPx, shadowUrl, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await renderCache.InvalidateAsync($"scene:{scene.Id}", cancellationToken);
        return GetRoomScenesQueryHandler.ToDto(scene);
    }

    /// <summary>Gölge haritasını (işaretli eşyalar hariç) yeniden üretir; eskisini siler, yeni URL'yi döner.</summary>
    private async Task<string?> RegenerateShadowAsync(RoomScene scene, byte[]? maskPng, string stamp, CancellationToken cancellationToken)
    {
        await using var photo = store.OpenPublic(scene.BaseImageUrl);
        if (photo is null) return scene.ShadowMapUrl;
        using var photoBytes = new MemoryStream();
        await photo.CopyToAsync(photoBytes, cancellationToken);
        var shadow = await images.CreateShadowMapAsync(photoBytes.ToArray(), scene.WallQuad, maskPng, cancellationToken);
        var url = await store.SavePublicAsync(scene.BaseImageUrl.Replace("/uploads/", "").Replace(".jpg", $"-shadow-{stamp}.png"), shadow, cancellationToken);
        if (scene.ShadowMapUrl is not null) await store.DeletePublicAsync(scene.ShadowMapUrl, cancellationToken);
        return url;
    }

    public async Task<SceneDto> Handle(SetUserRoomMaskCommand request, CancellationToken cancellationToken)
    {
        var scene = Owned(request.OwnerKey, request.SceneId);
        using var buffer = new MemoryStream();
        await request.MaskPng.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > CreateUserRoomSceneCommandHandler.MaxBytes)
            throw new WallConfigurationException(new Dictionary<string, string> { ["mask"] = "Maske çok büyük." });
        buffer.Position = 0;

        byte[] png;
        try { png = await images.NormalizeMaskAsync(buffer, scene.ImageWidthPx, scene.ImageHeightPx, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WallConfigurationException(new Dictionary<string, string> { ["mask"] = "Maske PNG görseli olmalıdır." });
        }

        var stamp = DateTime.UtcNow.Ticks.ToString("x");
        var url = await store.SavePublicAsync(scene.BaseImageUrl.Replace("/uploads/", "").Replace(".jpg", $"-mask-{stamp}.png"), png, cancellationToken);
        if (scene.ForegroundMaskUrl is not null) await store.DeletePublicAsync(scene.ForegroundMaskUrl, cancellationToken);

        // Gölge haritası işaretli eşyalar HARİÇ yeniden üretilir (eşya posterin üstüne koyu leke olarak yayılmasın).
        var shadowUrl = await RegenerateShadowAsync(scene, png, stamp, cancellationToken);
        scene.SetImages(scene.BaseImageUrl, scene.ImageWidthPx, scene.ImageHeightPx, shadowUrl, url);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await renderCache.InvalidateAsync($"scene:{scene.Id}", cancellationToken);
        return GetRoomScenesQueryHandler.ToDto(scene);
    }

    public async Task<Unit> Handle(DeleteUserRoomSceneCommand request, CancellationToken cancellationToken)
    {
        var scene = Owned(request.OwnerKey, request.SceneId);
        scene.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var url in new[] { scene.BaseImageUrl, scene.ShadowMapUrl, scene.ForegroundMaskUrl }.Where(u => u is not null))
            await store.DeletePublicAsync(url!, cancellationToken);
        await renderCache.InvalidateAsync($"scene:{scene.Id}", cancellationToken);
        return Unit.Value;
    }

    private RoomScene Owned(string ownerKey, Guid sceneId) =>
        unitOfWork.Repository<RoomScene>().Query().FirstOrDefault(s => s.Id == sceneId && s.OwnerKey == ownerKey && s.IsActive)
        ?? throw new KeyNotFoundException("Oda bulunamadı.");
}

/// <summary>Misafirin yüklediği odalar girişte üyeye taşınır (spec 1.6.2'deki liste birleştirmesiyle aynı an).</summary>
public sealed record MergeUserRoomScenesCommand(string GuestOwnerKey, string CustomerOwnerKey) : IRequest<int>;

public sealed class MergeUserRoomScenesCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MergeUserRoomScenesCommand, int>
{
    public async Task<int> Handle(MergeUserRoomScenesCommand request, CancellationToken cancellationToken)
    {
        var scenes = unitOfWork.Repository<RoomScene>().Query().Where(s => s.OwnerKey == request.GuestOwnerKey).ToList();
        foreach (var scene in scenes) scene.SetOwner(request.CustomerOwnerKey);
        if (scenes.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return scenes.Count;
    }
}
