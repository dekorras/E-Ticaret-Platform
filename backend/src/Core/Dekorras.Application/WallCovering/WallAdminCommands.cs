using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

// ============================ Malzemeler ============================

public sealed record MaterialAdminDto(Guid Id, string Code, string Name, string? Description, string? Features, decimal PricePerM2, decimal PanelWidthCm,
    decimal MaxHeightCm, int WeightGsm, string? FireRating, bool IsSelfAdhesive, bool RequiresGlue, decimal BleedCm, decimal MinBillableAreaM2,
    int SortOrder, bool IsActive, Guid? SampleProductId, Guid? GlueProductId);

public sealed record GetMaterialsAdminQuery : IRequest<IReadOnlyList<MaterialAdminDto>>;

public sealed record SaveMaterialCommand(Guid? Id, string Code, string Name, string? Description, string? Features, decimal PricePerM2, decimal PanelWidthCm,
    decimal MaxHeightCm, int WeightGsm, string? FireRating, bool IsSelfAdhesive, bool RequiresGlue, decimal BleedCm, decimal MinBillableAreaM2, int SortOrder,
    Guid? SampleProductId, Guid? GlueProductId) : IRequest<Guid>;

public sealed record SetMaterialActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

/// <summary>Numune (A4, sabit fiyat, stoklu) ve tutkal ürünlerini katalogda oluşturup malzemelere bağlar.
/// Zaten bağlı olanlara dokunmaz. VARSAYIM: numune 200 ₺, tutkal 150 ₺ (KDV hariç), stok 100; admin sonradan değiştirir.</summary>
public sealed record CreateSampleAndGlueProductsCommand(decimal SamplePriceTry = 200m, decimal GluePriceTry = 150m, int Stock = 100) : IRequest<int>;

public sealed class MaterialAdminHandler(IUnitOfWork unitOfWork, ISender sender) :
    IRequestHandler<GetMaterialsAdminQuery, IReadOnlyList<MaterialAdminDto>>,
    IRequestHandler<SaveMaterialCommand, Guid>,
    IRequestHandler<SetMaterialActiveCommand, Unit>,
    IRequestHandler<CreateSampleAndGlueProductsCommand, int>
{
    public Task<IReadOnlyList<MaterialAdminDto>> Handle(GetMaterialsAdminQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaterialAdminDto> list = unitOfWork.Repository<Material>().Query().OrderBy(m => m.SortOrder)
            .Select(m => new MaterialAdminDto(m.Id, m.Code, m.Name, m.Description, m.Features, m.PricePerM2, m.PanelWidthCm, m.MaxHeightCm, m.WeightGsm,
                m.FireRating, m.IsSelfAdhesive, m.RequiresGlue, m.BleedCm, m.MinBillableAreaM2, m.SortOrder, m.IsActive, m.SampleProductId, m.GlueProductId))
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<Guid> Handle(SaveMaterialCommand r, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Material>();
        var code = r.Code.Trim().ToLowerInvariant();
        if (repository.Query().Any(m => m.Code == code && m.Id != r.Id))
            throw new InvalidOperationException($"'{code}' kodlu bir malzeme zaten var.");

        Material material;
        if (r.Id is Guid id)
        {
            material = repository.Query().FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException("Malzeme bulunamadı.");
            material.Update(r.Name, r.Description, r.Features, r.PricePerM2, r.PanelWidthCm, r.MaxHeightCm, r.WeightGsm, r.FireRating,
                r.IsSelfAdhesive, r.RequiresGlue, r.BleedCm, r.MinBillableAreaM2, r.SortOrder);
        }
        else
        {
            material = new Material(code, r.Name, r.PricePerM2, r.PanelWidthCm, r.MaxHeightCm, r.WeightGsm, r.FireRating, r.IsSelfAdhesive, r.RequiresGlue,
                r.SortOrder, r.BleedCm, r.MinBillableAreaM2);
            material.Update(r.Name, r.Description, r.Features, r.PricePerM2, r.PanelWidthCm, r.MaxHeightCm, r.WeightGsm, r.FireRating,
                r.IsSelfAdhesive, r.RequiresGlue, r.BleedCm, r.MinBillableAreaM2, r.SortOrder);
            await repository.AddAsync(material, cancellationToken);
        }
        material.LinkProducts(r.SampleProductId, r.GlueProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return material.Id;
    }

    public async Task<Unit> Handle(SetMaterialActiveCommand request, CancellationToken cancellationToken)
    {
        var material = unitOfWork.Repository<Material>().Query().FirstOrDefault(m => m.Id == request.Id) ?? throw new KeyNotFoundException("Malzeme bulunamadı.");
        if (request.IsActive) material.Activate(); else material.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<int> Handle(CreateSampleAndGlueProductsCommand request, CancellationToken cancellationToken)
    {
        var materials = unitOfWork.Repository<Material>().Query().OrderBy(m => m.SortOrder).ToList();
        // Ürün en az bir kategori ister. VARSAYIM: mevcut "aksesuar..." kategorisi (canlıda aksesuarlar-128) kullanılır;
        // yoksa "aksesuarlar" oluşturulur. Konfigüratör kategorileri KULLANILMAZ (numune/tutkal ölçüye özel ürün değildir).
        var categoryId = unitOfWork.Repository<Category>().Query().Where(c => c.Slug.StartsWith("aksesuar")).Select(c => (Guid?)c.Id).FirstOrDefault()
            ?? await sender.Send(new CreateCategoryCommand("aksesuarlar", null, 99, "tr", "Aksesuarlar", null), cancellationToken);
        var created = 0;

        Guid? glueId = materials.Select(m => m.GlueProductId).FirstOrDefault(g => g is not null);
        if (glueId is null && materials.Any(m => m.RequiresGlue))
        {
            glueId = await CreateProductAsync("duvar-kagidi-tutkali", "TUTKAL-01", "Duvar Kağıdı Tutkalı (1 kg)", request.GluePriceTry, request.Stock, categoryId, cancellationToken);
            created++;
        }

        foreach (var material in materials)
        {
            var sampleId = material.SampleProductId;
            if (sampleId is null)
            {
                sampleId = await CreateProductAsync($"numune-{material.Code}", $"NUMUNE-{material.Code.ToUpperInvariant()}", $"{material.Name} Numunesi (A4)",
                    request.SamplePriceTry, request.Stock, categoryId, cancellationToken);
                created++;
            }
            // Handler'ın izlediği örnek yeniden okunur (CreateProductCommand ayrı bir kaydetme yaptı).
            var fresh = unitOfWork.Repository<Material>().Query().First(m => m.Id == material.Id);
            fresh.LinkProducts(sampleId, fresh.RequiresGlue ? glueId : fresh.GlueProductId);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task<Guid> CreateProductAsync(string slug, string code, string name, decimal price, int stock, Guid? categoryId, CancellationToken cancellationToken)
    {
        var existing = unitOfWork.Repository<Product>().Query().Where(p => p.Slug == slug).Select(p => (Guid?)p.Id).FirstOrDefault();
        if (existing is Guid id) return id;
        var productId = await sender.Send(new CreateProductCommand(slug, code, price, 20m, UnitOfMeasure.Piece, 1, StockQuantity: stock,
            BrandId: null, CategoryIds: categoryId is Guid c ? [c] : [], LanguageCode: "tr", Name: name, Description: null), cancellationToken);
        await sender.Send(new SetProductPublishedCommand(productId, Published: true), cancellationToken);
        return productId;
    }
}

// ============================ Etiketler ============================

public sealed record TagAdminDto(Guid Id, TagGroup Group, string Value, string Label, string? Hex, int ProductCount);

public sealed record GetTagsAdminQuery : IRequest<IReadOnlyList<TagAdminDto>>;
public sealed record SaveTagCommand(Guid? Id, TagGroup Group, string Value, string Label, string? Hex) : IRequest<Guid>;
public sealed record DeleteTagCommand(Guid Id) : IRequest<Unit>;
public sealed record SetProductTagsCommand(Guid ProductId, IReadOnlyCollection<Guid> TagIds) : IRequest<Unit>;
public sealed record GetProductTagIdsQuery(Guid ProductId) : IRequest<IReadOnlyList<Guid>>;

public sealed class TagAdminHandler(IUnitOfWork unitOfWork) :
    IRequestHandler<GetTagsAdminQuery, IReadOnlyList<TagAdminDto>>,
    IRequestHandler<SaveTagCommand, Guid>,
    IRequestHandler<DeleteTagCommand, Unit>,
    IRequestHandler<SetProductTagsCommand, Unit>,
    IRequestHandler<GetProductTagIdsQuery, IReadOnlyList<Guid>>
{
    public Task<IReadOnlyList<TagAdminDto>> Handle(GetTagsAdminQuery request, CancellationToken cancellationToken)
    {
        var productTags = unitOfWork.Repository<ProductTag>().Query();
        IReadOnlyList<TagAdminDto> list = unitOfWork.Repository<Tag>().Query().OrderBy(t => t.Group).ThenBy(t => t.Label)
            .Select(t => new TagAdminDto(t.Id, t.Group, t.Value, t.Label, t.Hex, productTags.Count(pt => pt.TagId == t.Id)))
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<Guid> Handle(SaveTagCommand r, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Tag>();
        var value = Tag.Normalize(r.Value);
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(r.Label)) throw new InvalidOperationException("Değer ve etiket adı zorunludur.");
        if (r.Hex is not null && !System.Text.RegularExpressions.Regex.IsMatch(r.Hex, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidOperationException("Renk #RRGGBB biçiminde olmalıdır.");
        if (repository.Query().Any(t => t.Group == r.Group && t.Value == value && t.Id != r.Id))
            throw new InvalidOperationException("Bu grupta aynı değerde bir etiket zaten var.");

        if (r.Id is Guid id)
        {
            var tag = repository.Query().FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException("Etiket bulunamadı.");
            tag.Update(r.Label, r.Hex);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return id;
        }
        var created = new Tag(r.Group, value, r.Label, r.Hex);
        await repository.AddAsync(created, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Id;
    }

    public async Task<Unit> Handle(DeleteTagCommand request, CancellationToken cancellationToken)
    {
        foreach (var pt in unitOfWork.Repository<ProductTag>().Query().Where(pt => pt.TagId == request.Id).ToList())
            unitOfWork.Repository<ProductTag>().Remove(pt);
        var tag = unitOfWork.Repository<Tag>().Query().FirstOrDefault(t => t.Id == request.Id);
        if (tag is not null) unitOfWork.Repository<Tag>().Remove(tag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(SetProductTagsCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ProductTag>();
        var current = repository.Query().Where(pt => pt.ProductId == request.ProductId).ToList();
        foreach (var pt in current.Where(pt => !request.TagIds.Contains(pt.TagId))) repository.Remove(pt);
        foreach (var tagId in request.TagIds.Where(id => current.All(pt => pt.TagId != id)).Distinct())
            await repository.AddAsync(new ProductTag(request.ProductId, tagId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public Task<IReadOnlyList<Guid>> Handle(GetProductTagIdsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = unitOfWork.Repository<ProductTag>().Query().Where(pt => pt.ProductId == request.ProductId).Select(pt => pt.TagId).ToList();
        return Task.FromResult(ids);
    }
}

// ============================ Ürün profili ============================

public sealed record MaterialOverrideDto(Guid MaterialId, string MaterialCode, string MaterialName, decimal BasePricePerM2, decimal? OverridePricePerM2, bool IsAllowed);

public sealed record WallpaperProfileAdminDto(Guid? ProfileId, bool Exists, bool IsEnabled, WallProductType ProductType, decimal? RepeatWidthCm,
    decimal? RepeatHeightCm, RepeatType RepeatType, int PopularityScore, int ImageWidthPx, int ImageHeightPx, int? Dpi, string? ThumbUrl,
    string? SceneThumbUrl, string? DominantColors, DateTime? DerivativesGeneratedAtUtc, IReadOnlyList<MaterialOverrideDto> Overrides);

public sealed record GetWallpaperProfileAdminQuery(Guid ProductId) : IRequest<WallpaperProfileAdminDto>;

public sealed record SaveWallpaperProfileCommand(Guid ProductId, bool IsEnabled, WallProductType ProductType, decimal? RepeatWidthCm, decimal? RepeatHeightCm,
    RepeatType RepeatType, int PopularityScore) : IRequest<Unit>;

public sealed record SetMaterialOverrideCommand(Guid ProductId, Guid MaterialId, decimal? PricePerM2, bool IsAllowed) : IRequest<Unit>;

/// <summary>Türevleri + sahne küçük resmini yeniden üretmek için profili kuyruğa alır (arka plan işçisi üretir).</summary>
public sealed record QueueDerivativesCommand(Guid ProductId, bool ReloadOriginal = false) : IRequest<Unit>;

public sealed class WallpaperProfileAdminHandler(IUnitOfWork unitOfWork, IWallRenderCache renderCache) :
    IRequestHandler<GetWallpaperProfileAdminQuery, WallpaperProfileAdminDto>,
    IRequestHandler<SaveWallpaperProfileCommand, Unit>,
    IRequestHandler<SetMaterialOverrideCommand, Unit>,
    IRequestHandler<QueueDerivativesCommand, Unit>
{
    public Task<WallpaperProfileAdminDto> Handle(GetWallpaperProfileAdminQuery request, CancellationToken cancellationToken)
    {
        var p = unitOfWork.Repository<WallpaperProfile>().Query().FirstOrDefault(x => x.ProductId == request.ProductId);
        var overrides = unitOfWork.Repository<ProductMaterialOverride>().Query().Where(o => o.ProductId == request.ProductId).ToList().ToDictionary(o => o.MaterialId);
        var materials = unitOfWork.Repository<Material>().Query().OrderBy(m => m.SortOrder).ToList()
            .Select(m => new MaterialOverrideDto(m.Id, m.Code, m.Name, m.PricePerM2, overrides.GetValueOrDefault(m.Id)?.PricePerM2, overrides.GetValueOrDefault(m.Id)?.IsAllowed ?? true))
            .ToList();
        return Task.FromResult(p is null
            ? new WallpaperProfileAdminDto(null, false, false, WallProductType.Mural, null, null, RepeatType.Straight, 0, 0, 0, null, null, null, null, null, materials)
            : new WallpaperProfileAdminDto(p.Id, true, p.IsEnabled, p.ProductType, p.RepeatWidthCm, p.RepeatHeightCm, p.RepeatType, p.PopularityScore,
                p.ImageWidthPx, p.ImageHeightPx, p.Dpi, p.ThumbUrl, p.SceneThumbUrl, p.DominantColors, p.DerivativesGeneratedAtUtc, materials));
    }

    public async Task<Unit> Handle(SaveWallpaperProfileCommand r, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<WallpaperProfile>();
        var profile = repository.Query().FirstOrDefault(x => x.ProductId == r.ProductId);
        var isNew = profile is null;
        profile ??= new WallpaperProfile(r.ProductId, r.ProductType);
        var typeChanged = isNew || profile.ProductType != r.ProductType || profile.RepeatWidthCm != r.RepeatWidthCm || profile.RepeatHeightCm != r.RepeatHeightCm || profile.RepeatType != r.RepeatType;
        // Doğrulama (SetType) izlemeye EKLEMEDEN önce: Blazor devresinin uzun ömürlü DbContext'inde hatalı bir
        // denemeden "Added" durumda kalan profil sonraki her kaydetmeyi bozardı.
        profile.SetType(r.ProductType, r.RepeatWidthCm, r.RepeatHeightCm, r.RepeatType);
        if (isNew) await repository.AddAsync(profile, cancellationToken);
        profile.SetPopularity(r.PopularityScore);
        if (r.IsEnabled) profile.Enable(); else profile.Disable();
        if (typeChanged) profile.SetSceneThumb(null); // desen/tekrar değişince katalog küçük resmi yeniden üretilir
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (typeChanged) await renderCache.InvalidateAsync($"product:{r.ProductId}", cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(SetMaterialOverrideCommand r, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ProductMaterialOverride>();
        var existing = repository.Query().FirstOrDefault(o => o.ProductId == r.ProductId && o.MaterialId == r.MaterialId);
        if (r.PricePerM2 is null && r.IsAllowed)
        {
            if (existing is not null) repository.Remove(existing); // istisna kalmadı
        }
        else if (existing is null) await repository.AddAsync(new ProductMaterialOverride(r.ProductId, r.MaterialId, r.PricePerM2, r.IsAllowed), cancellationToken);
        else existing.Set(r.PricePerM2, r.IsAllowed);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(QueueDerivativesCommand request, CancellationToken cancellationToken)
    {
        var profile = unitOfWork.Repository<WallpaperProfile>().Query().FirstOrDefault(x => x.ProductId == request.ProductId)
            ?? throw new KeyNotFoundException("Bu ürün için duvar kağıdı profili yok.");
        profile.ResetDerivatives(request.ReloadOriginal);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await renderCache.InvalidateAsync($"product:{request.ProductId}", cancellationToken);
        return Unit.Value;
    }
}

// ============================ Oda sahneleri ============================

public sealed record CreateRoomSceneCommand(string Name, RoomType RoomType, Stream BaseImage, decimal RealWallWidthCm, decimal RealWallHeightCm) : IRequest<Guid>;

public sealed record UpdateRoomSceneCommand(Guid Id, string Name, RoomType RoomType, int SortOrder, double[] Corners, decimal RealWallWidthCm, decimal RealWallHeightCm) : IRequest<Unit>;

public enum SceneLayer { Base, Shadow, Mask }

public sealed record UploadSceneLayerCommand(Guid SceneId, SceneLayer Layer, Stream? Content) : IRequest<Unit>;

public sealed record SetDefaultSceneCommand(Guid SceneId) : IRequest<Unit>;
public sealed record SetSceneActiveCommand(Guid SceneId, bool IsActive) : IRequest<Unit>;

/// <summary>Katalog kartlarındaki sahne küçük resimlerini (SceneThumbUrl) sıfırlar; arka plan işçisi yeniden üretir.</summary>
public sealed record RegenerateSceneThumbsCommand : IRequest<int>;

public sealed record GetRoomScenesAdminQuery : IRequest<IReadOnlyList<SceneAdminDto>>;

public sealed record SceneAdminDto(SceneDto Scene, int SortOrder, bool IsActive);

public sealed class RoomSceneAdminHandler(IUnitOfWork unitOfWork, IImageDerivativeService images, IWallImageStore store, IWallRenderCache renderCache) :
    IRequestHandler<CreateRoomSceneCommand, Guid>,
    IRequestHandler<UpdateRoomSceneCommand, Unit>,
    IRequestHandler<UploadSceneLayerCommand, Unit>,
    IRequestHandler<SetDefaultSceneCommand, Unit>,
    IRequestHandler<SetSceneActiveCommand, Unit>,
    IRequestHandler<RegenerateSceneThumbsCommand, int>,
    IRequestHandler<GetRoomScenesAdminQuery, IReadOnlyList<SceneAdminDto>>
{
    public Task<IReadOnlyList<SceneAdminDto>> Handle(GetRoomScenesAdminQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<SceneAdminDto> list = unitOfWork.Repository<RoomScene>().Query().Where(s => s.OwnerKey == null)
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).ToList()
            .Select(s => new SceneAdminDto(GetRoomScenesQueryHandler.ToDto(s), s.SortOrder, s.IsActive)).ToList();
        return Task.FromResult(list);
    }

    public async Task<Guid> Handle(CreateRoomSceneCommand request, CancellationToken cancellationToken)
    {
        var (bytes, w, h) = await SanitizeAsync(request.BaseImage, keepPng: false, cancellationToken);
        var url = await store.SavePublicAsync($"wall/scenes/admin-{Guid.NewGuid():N}-base.jpg", bytes, cancellationToken);
        // Başlangıç köşeleri: görselin iç %70'i - admin sürükleyerek düzeltir.
        var quad = new WallQuad(new Point2(w * 0.15, h * 0.1), new Point2(w * 0.85, h * 0.1), new Point2(w * 0.85, h * 0.75), new Point2(w * 0.15, h * 0.75));
        var order = unitOfWork.Repository<RoomScene>().Query().Where(s => s.OwnerKey == null).Select(s => (int?)s.SortOrder).Max() ?? 0;
        var scene = new RoomScene(request.Name, request.RoomType, url, w, h, quad, request.RealWallWidthCm, request.RealWallHeightCm);
        scene.Update(request.Name, request.RoomType, order + 1);
        await unitOfWork.Repository<RoomScene>().AddAsync(scene, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return scene.Id;
    }

    public async Task<Unit> Handle(UpdateRoomSceneCommand r, CancellationToken cancellationToken)
    {
        var scene = Preset(r.Id);
        if (r.Corners.Length != 8) throw new InvalidOperationException("4 köşe gerekli.");
        var c = r.Corners;
        scene.Update(r.Name, r.RoomType, r.SortOrder);
        scene.SetWall(new WallQuad(new Point2(c[0], c[1]), new Point2(c[2], c[3]), new Point2(c[4], c[5]), new Point2(c[6], c[7])), r.RealWallWidthCm, r.RealWallHeightCm);
        await SaveAndInvalidateAsync(scene, cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(UploadSceneLayerCommand r, CancellationToken cancellationToken)
    {
        var scene = Preset(r.SceneId);
        string? url = null;
        if (r.Content is not null)
        {
            var isPng = r.Layer != SceneLayer.Base;
            var (bytes, w, h) = await SanitizeAsync(r.Content, keepPng: isPng, cancellationToken);
            if (r.Layer != SceneLayer.Base && (w != scene.ImageWidthPx || h != scene.ImageHeightPx))
            {
                // Gölge/maske taban görselle aynı boyutta olmalı; farklıysa taban boyutuna ölçeklenir.
                using var ms = new MemoryStream(bytes);
                bytes = await images.NormalizeMaskAsync(ms, scene.ImageWidthPx, scene.ImageHeightPx, cancellationToken);
            }
            url = await store.SavePublicAsync($"wall/scenes/admin-{scene.Id:N}-{r.Layer.ToString().ToLowerInvariant()}-{DateTime.UtcNow.Ticks:x}.{(isPng ? "png" : "jpg")}", bytes, cancellationToken);
            if (r.Layer == SceneLayer.Base && (w != scene.ImageWidthPx || h != scene.ImageHeightPx))
            {
                // Taban görsel boyutu değişti: köşeler orantılı ölçeklenir, katmanlar yeniden yüklenmeli.
                var sx = w / (double)scene.ImageWidthPx; var sy = h / (double)scene.ImageHeightPx;
                var q = scene.WallQuad;
                scene.SetImages(url, w, h, null, null);
                scene.SetWall(new WallQuad(new Point2(q.TopLeft.X * sx, q.TopLeft.Y * sy), new Point2(q.TopRight.X * sx, q.TopRight.Y * sy),
                    new Point2(q.BottomRight.X * sx, q.BottomRight.Y * sy), new Point2(q.BottomLeft.X * sx, q.BottomLeft.Y * sy)), scene.RealWallWidthCm, scene.RealWallHeightCm);
                await SaveAndInvalidateAsync(scene, cancellationToken);
                return Unit.Value;
            }
        }

        switch (r.Layer)
        {
            case SceneLayer.Base when url is not null: scene.SetImages(url, scene.ImageWidthPx, scene.ImageHeightPx, scene.ShadowMapUrl, scene.ForegroundMaskUrl); break;
            case SceneLayer.Shadow: scene.SetImages(scene.BaseImageUrl, scene.ImageWidthPx, scene.ImageHeightPx, url, scene.ForegroundMaskUrl); break;
            case SceneLayer.Mask: scene.SetForegroundMask(url); break;
        }
        await SaveAndInvalidateAsync(scene, cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(SetDefaultSceneCommand request, CancellationToken cancellationToken)
    {
        foreach (var s in unitOfWork.Repository<RoomScene>().Query().Where(s => s.OwnerKey == null).ToList())
            s.SetDefault(s.Id == request.SceneId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await Handle(new RegenerateSceneThumbsCommand(), cancellationToken); // varsayılan sahne değişti → katalog küçük resimleri
        return Unit.Value;
    }

    public async Task<Unit> Handle(SetSceneActiveCommand request, CancellationToken cancellationToken)
    {
        var scene = Preset(request.SceneId);
        if (request.IsActive) scene.Activate(); else scene.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<int> Handle(RegenerateSceneThumbsCommand request, CancellationToken cancellationToken)
    {
        var profiles = unitOfWork.Repository<WallpaperProfile>().Query().Where(p => p.SceneThumbUrl != null).ToList();
        foreach (var p in profiles) p.SetSceneThumb(null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return profiles.Count;
    }

    private RoomScene Preset(Guid id) =>
        unitOfWork.Repository<RoomScene>().Query().FirstOrDefault(s => s.Id == id && s.OwnerKey == null) ?? throw new KeyNotFoundException("Sahne bulunamadı.");

    private async Task SaveAndInvalidateAsync(RoomScene scene, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await renderCache.InvalidateAsync($"scene:{scene.Id}", cancellationToken);
        if (scene.IsDefault) await Handle(new RegenerateSceneThumbsCommand(), cancellationToken);
    }

    private async Task<(byte[] Bytes, int W, int H)> SanitizeAsync(Stream content, bool keepPng, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var inspection = images.Inspect(buffer);
        if (!inspection.IsValid) throw new InvalidOperationException(inspection.Error ?? "Geçersiz görsel.");
        buffer.Position = 0;
        if (keepPng)
        {
            // Maske/gölge saydamlık taşıdığı için PNG olarak korunur (NormalizeMask PNG'yi yeniden kodlar).
            if (inspection.Extension != ".png") throw new InvalidOperationException("Gölge haritası ve ön plan maskesi PNG olmalıdır.");
            var png = await images.NormalizeMaskAsync(buffer, inspection.WidthPx, inspection.HeightPx, cancellationToken);
            return (png, inspection.WidthPx, inspection.HeightPx);
        }
        var (jpeg, w, h) = await images.SanitizeAsync(buffer, 3000, cancellationToken);
        return (jpeg, w, h);
    }
}

// ============================ Embed istemcileri ============================

public sealed record EmbedClientAdminDto(Guid Id, string Name, string PublicKey, string AllowedOrigins, string AllowedImageHosts, int DailyQuota,
    bool IsActive, DateOnly? UsageDate, int UsageCount);

public sealed record GetEmbedClientsQuery : IRequest<IReadOnlyList<EmbedClientAdminDto>>;
public sealed record SaveEmbedClientCommand(Guid? Id, string Name, string AllowedOrigins, string AllowedImageHosts, int DailyQuota) : IRequest<Guid>;
public sealed record SetEmbedClientActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class EmbedClientAdminHandler(IUnitOfWork unitOfWork) :
    IRequestHandler<GetEmbedClientsQuery, IReadOnlyList<EmbedClientAdminDto>>,
    IRequestHandler<SaveEmbedClientCommand, Guid>,
    IRequestHandler<SetEmbedClientActiveCommand, Unit>
{
    private static IEnumerable<string> Split(string value) => value.Split([',', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public Task<IReadOnlyList<EmbedClientAdminDto>> Handle(GetEmbedClientsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmbedClientAdminDto> list = unitOfWork.Repository<EmbedClient>().Query().OrderBy(c => c.Name)
            .Select(c => new EmbedClientAdminDto(c.Id, c.Name, c.PublicKey, c.AllowedOrigins, c.AllowedImageHosts, c.DailyQuota, c.IsActive, c.UsageDate, c.UsageCount))
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<Guid> Handle(SaveEmbedClientCommand r, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new InvalidOperationException("Ad zorunludur.");
        var origins = Split(r.AllowedOrigins).ToList();
        if (origins.Count == 0) throw new InvalidOperationException("En az bir izinli origin girin (ör. https://magaza.com).");
        if (origins.Any(o => !Uri.TryCreate(o, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http") || u.AbsolutePath != "/"))
            throw new InvalidOperationException("Origin yalnızca şema + alan adı (+ port) olmalıdır, ör. https://magaza.com");

        var repository = unitOfWork.Repository<EmbedClient>();
        if (r.Id is Guid id)
        {
            var client = repository.Query().FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("İstemci bulunamadı.");
            client.Update(r.Name, origins, Split(r.AllowedImageHosts), r.DailyQuota);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return id;
        }
        var created = new EmbedClient(r.Name, origins, Split(r.AllowedImageHosts), r.DailyQuota);
        await repository.AddAsync(created, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Id;
    }

    public async Task<Unit> Handle(SetEmbedClientActiveCommand request, CancellationToken cancellationToken)
    {
        var client = unitOfWork.Repository<EmbedClient>().Query().FirstOrDefault(c => c.Id == request.Id) ?? throw new KeyNotFoundException("İstemci bulunamadı.");
        if (request.IsActive) client.Activate(); else client.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
