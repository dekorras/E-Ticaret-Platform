using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>Bir poster için türev görselleri üretir (spec 1.2/1.6.6-F): orijinal görsel özel depoya
/// kopyalanır, 400/800/2000(filigranlı) px türevler + LQIP + baskın renkler üretilir ve baskın renklere
/// en yakın renk etiketleri otomatik atanır. Kaynak: profilde orijinal yoksa ürünün ana görseli.
/// VARSAYIM: içe aktarılan mevcut ürünlerin ana görselleri zaten /uploads altında herkese açıktı; bunlar
/// geriye dönük olarak gizlenemez - yeni yüklemelerde orijinal yalnızca özel depoda tutulur.</summary>
public sealed record GenerateProductDerivativesCommand(Guid ProductId, bool Force = false) : IRequest<bool>;

public sealed class GenerateProductDerivativesCommandHandler(IUnitOfWork unitOfWork, IWallImageStore store, IImageDerivativeService derivatives, IWallRenderCache renderCache)
    : IRequestHandler<GenerateProductDerivativesCommand, bool>
{
    // VARSAYIM: baskın renk ile renk etiketi arasındaki RGB mesafesi 90'ın altındaysa etiket atanır (en fazla 2 etiket).
    private const double ColorTagThreshold = 90d;

    public async Task<bool> Handle(GenerateProductDerivativesCommand request, CancellationToken cancellationToken)
    {
        var profile = unitOfWork.Repository<WallpaperProfile>().Query().FirstOrDefault(w => w.ProductId == request.ProductId);
        if (profile is null) return false;
        if (profile.DerivativesGeneratedAtUtc is not null && !request.Force) return false;

        var key = profile.OriginalImageKey;
        if (key is null)
        {
            var source = unitOfWork.Repository<ProductImage>().Query()
                .Where(i => i.ProductId == request.ProductId).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                .Select(i => i.Url).FirstOrDefault();
            await using var publicStream = source is null ? null : store.OpenPublic(source);
            if (publicStream is null) return false;

            using var buffer = new MemoryStream();
            await publicStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            var inspection = derivatives.Inspect(buffer);
            if (!inspection.IsValid) return false;

            key = $"wall/originals/{request.ProductId:N}{inspection.Extension}";
            buffer.Position = 0;
            await store.SavePrivateAsync(key, buffer, cancellationToken);
        }

        await using var original = store.OpenPrivate(key);
        if (original is null) return false;

        var result = await derivatives.CreateDerivativesAsync(original, "products", request.ProductId.ToString("N"), cancellationToken);
        profile.SetOriginalImage(key, result.WidthPx, result.HeightPx, result.Dpi);
        profile.SetDerivatives(result.ThumbUrl, result.ListUrl, result.PreviewUrl, result.LqipBase64, string.Join(",", result.DominantColors));
        profile.SetSceneThumb(null); // eski sahne küçük resmi yeni görselle yeniden üretilecek

        await AssignColorTagsAsync(request.ProductId, result.DominantColors, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await renderCache.InvalidateAsync($"product:{request.ProductId}", cancellationToken);
        return true;
    }

    private async Task AssignColorTagsAsync(Guid productId, IReadOnlyList<string> colors, CancellationToken cancellationToken)
    {
        var colorTags = unitOfWork.Repository<Tag>().Query().Where(t => t.Group == TagGroup.Color && t.Hex != null).ToList();
        if (colorTags.Count == 0 || colors.Count == 0) return;

        var existing = unitOfWork.Repository<ProductTag>().Query().Where(pt => pt.ProductId == productId).Select(pt => pt.TagId).ToHashSet();
        var matches = colors
            .Select(c => GetWallCatalogQueryHandler.TryParseHex(c))
            .Where(c => c is not null)
            .Select(c => colorTags
                .Select(t => (Tag: t, Distance: GetWallCatalogQueryHandler.MinDistance(c!.Value, t.Hex)))
                .OrderBy(x => x.Distance)
                .First())
            .Where(x => x.Distance < ColorTagThreshold)
            .Select(x => x.Tag.Id)
            .Distinct()
            .Take(2);

        foreach (var tagId in matches.Where(id => !existing.Contains(id)))
            await unitOfWork.Repository<ProductTag>().AddAsync(new ProductTag(productId, tagId), cancellationToken);
    }
}

/// <summary>Katalog kartındaki hover görünümü: ürün × varsayılan sahne, 800 px (spec 1.6.1/1.6.5).</summary>
public sealed record GenerateSceneThumbCommand(Guid ProductId) : IRequest<string?>;

public sealed class GenerateSceneThumbCommandHandler(IUnitOfWork unitOfWork, ISender sender) : IRequestHandler<GenerateSceneThumbCommand, string?>
{
    public async Task<string?> Handle(GenerateSceneThumbCommand request, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        var scenes = unitOfWork.Repository<RoomScene>().Query().Where(s => s.IsActive && s.OwnerKey == null);
        var scene = (settings.DefaultSceneId is Guid id ? scenes.FirstOrDefault(s => s.Id == id) : null)
                    ?? scenes.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).FirstOrDefault();
        var profile = unitOfWork.Repository<WallpaperProfile>().Query().FirstOrDefault(w => w.ProductId == request.ProductId);
        var material = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).OrderBy(m => m.SortOrder).FirstOrDefault();
        if (scene is null || profile is null || material is null) return null;

        // Posterin oranını koruyan, sahne duvarına sığan ölçü (duvar genişliğinin %80'i).
        var ratio = profile.AspectRatio > 0 ? profile.AspectRatio : 1.3333m;
        var width = Math.Round(scene.RealWallWidthCm * 0.8m, 1);
        var height = Math.Round(width / ratio, 1);
        if (height > scene.RealWallHeightCm * 0.9m)
        {
            height = Math.Round(scene.RealWallHeightCm * 0.9m, 1);
            width = Math.Round(height * ratio, 1);
        }
        height = Math.Clamp(height, WallDimensions.MinSideCm, material.MaxHeightCm);

        var result = await sender.Send(new RenderWallPreviewQuery(scene.Id, request.ProductId,
            new WallConfiguration(width, height, material.Code), WallAlign.Center, 800, Watermark: false), cancellationToken);

        profile.SetSceneThumb(result.Url);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Url;
    }
}
