using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>Konfigürasyon kuralları ihlal edildiğinde (ölçü/malzeme/ürün) fırlatılır; API bunu alan
/// bazlı hatalarla 400 ProblemDetails'e çevirir.</summary>
public sealed class WallConfigurationException(IReadOnlyDictionary<string, string> errors)
    : Exception(string.Join(" ", errors.Values))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

/// <summary>Ölçüye özel ürün, ölçü seçilmeden (düz "Sepete Ekle" ile) sepete eklenmeye çalışıldı.
/// Arayüz müşteriyi ürün sayfasındaki konfigüratöre yönlendirir.</summary>
public sealed class WallConfigurationRequiredException(string productSlug)
    : InvalidOperationException("Bu ürün ölçüye özel üretilir; lütfen önce duvar ölçünüzü seçin.")
{
    public string ProductSlug { get; } = productSlug;
}

public sealed record MaterialDto(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Features,
    decimal PricePerM2,
    decimal PanelWidthCm,
    decimal MaxHeightCm,
    int WeightGsm,
    string? FireRating,
    bool IsSelfAdhesive,
    bool RequiresGlue,
    decimal BleedCm,
    decimal MinBillableAreaM2,
    Guid? SampleProductId,
    Guid? GlueProductId);

/// <summary>Aktif malzemeler. ProductId verilirse ürün bazlı fiyat istisnaları uygulanır ve o üründe
/// kapatılmış malzemeler listelenmez.</summary>
public sealed record GetMaterialsQuery(Guid? ProductId = null) : IRequest<IReadOnlyList<MaterialDto>>;

public sealed class GetMaterialsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMaterialsQuery, IReadOnlyList<MaterialDto>>
{
    public Task<IReadOnlyList<MaterialDto>> Handle(GetMaterialsQuery request, CancellationToken cancellationToken)
    {
        var materials = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToList();

        var overrides = request.ProductId is Guid productId
            ? unitOfWork.Repository<ProductMaterialOverride>().Query().Where(o => o.ProductId == productId).ToDictionary(o => o.MaterialId)
            : [];

        IReadOnlyList<MaterialDto> result = materials
            .Where(m => !overrides.TryGetValue(m.Id, out var o) || o.IsAllowed)
            .Select(m => new MaterialDto(
                m.Code, m.Name, m.Description,
                (m.Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                overrides.TryGetValue(m.Id, out var o) && o.PricePerM2 is decimal p ? p : m.PricePerM2,
                m.PanelWidthCm, m.MaxHeightCm, m.WeightGsm, m.FireRating, m.IsSelfAdhesive, m.RequiresGlue, m.BleedCm, m.MinBillableAreaM2,
                m.SampleProductId, m.GlueProductId))
            .ToList();

        return Task.FromResult(result);
    }
}

public sealed record GetWallCoveringSettingsQuery : IRequest<WallCoveringSettings>;

public sealed class GetWallCoveringSettingsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallCoveringSettingsQuery, WallCoveringSettings>
{
    public Task<WallCoveringSettings> Handle(GetWallCoveringSettingsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(WallCoveringSettings.Load(unitOfWork));
}

/// <summary>"Duvarında Gör" özellik bayrağının admin ayarındaki yüzdesi (ayar yoksa null).</summary>
public sealed record GetWallPreviewLinkPercentQuery : IRequest<int?>;

public sealed class GetWallPreviewLinkPercentQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallPreviewLinkPercentQuery, int?>
{
    public Task<int?> Handle(GetWallPreviewLinkPercentQuery request, CancellationToken cancellationToken)
    {
        var raw = unitOfWork.Repository<Domain.SystemAdmin.Setting>().Query()
            .Where(s => s.Key == WallCoveringSettingKeys.WallPreviewLinkPercent).Select(s => s.Value).FirstOrDefault();
        return Task.FromResult(int.TryParse(raw, out var percent) ? Math.Clamp(percent, 0, 100) : (int?)null);
    }
}

/// <summary>Slug → konfigüre edilebilir, aktif ürünün kimliği (yoksa null).</summary>
public sealed record GetWallProductIdQuery(string Slug) : IRequest<Guid?>;

public sealed class GetWallProductIdQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallProductIdQuery, Guid?>
{
    public Task<Guid?> Handle(GetWallProductIdQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(unitOfWork.Repository<Product>().Query()
            .Where(p => p.Slug == request.Slug && p.Status == ProductStatus.Active)
            .Join(unitOfWork.Repository<WallpaperProfile>().Query().Where(w => w.IsEnabled), p => p.Id, w => w.ProductId, (p, w) => (Guid?)p.Id)
            .FirstOrDefault());
}

public sealed record QuoteWallpaperQuery(Guid ProductId, WallConfiguration Configuration, int Quantity, string? SessionKey) : IRequest<WallQuoteDto>;

public sealed class QuoteWallpaperQueryHandler(IPricingService pricingService) : IRequestHandler<QuoteWallpaperQuery, WallQuoteDto>
{
    public Task<WallQuoteDto> Handle(QuoteWallpaperQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(pricingService.Quote(request.ProductId, request.Configuration, request.Quantity, request.SessionKey, DateTime.UtcNow));
}

public sealed record DeliveryEstimateDto(DateOnly ProductionStart, DateOnly EarliestDelivery, DateOnly LatestDelivery, string Label);

public sealed record GetDeliveryEstimateQuery(string? MaterialCode = null) : IRequest<DeliveryEstimateDto>;

public sealed class GetDeliveryEstimateQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetDeliveryEstimateQuery, DeliveryEstimateDto>
{
    // VARSAYIM: malzemeye göre üretim süresi farkı tanımlanmadı - tüm malzemeler 1–2 iş günü (spec 1.7).
    public Task<DeliveryEstimateDto> Handle(GetDeliveryEstimateQuery request, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        var e = DeliveryEstimator.Estimate(DateTime.UtcNow, extraHolidays: settings.ExtraHolidays);
        return Task.FromResult(new DeliveryEstimateDto(e.ProductionStart, e.EarliestDelivery, e.LatestDelivery, FormatRange(e.EarliestDelivery, e.LatestDelivery)));
    }

    /// <summary>"2–4 Ekim" / "30 Eylül – 3 Ekim" biçimi.</summary>
    public static string FormatRange(DateOnly from, DateOnly to)
    {
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        return from.Month == to.Month
            ? $"{from.Day}–{to.ToString("d MMMM", tr)}"
            : $"{from.ToString("d MMMM", tr)} – {to.ToString("d MMMM", tr)}";
    }
}

/// <summary>Ayarlardaki kategori slug'larındaki (alt kategoriler DAHİL) tüm ürünler için eksik
/// WallpaperProfile kayıtlarını oluşturur - kullanıcı kararı: "Duvar Kağıtları + Posterler
/// kategorilerinin tamamı konfigüre edilebilir". İdempotenttir; mevcut profillere dokunmaz.</summary>
public sealed record EnsureWallpaperProfilesCommand : IRequest<int>;

public sealed class EnsureWallpaperProfilesCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<EnsureWallpaperProfilesCommand, int>
{
    public async Task<int> Handle(EnsureWallpaperProfilesCommand request, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        var categoryIds = ResolveCategoryTree(unitOfWork, settings.CategorySlugs);

        var productIds = unitOfWork.Repository<ProductCategory>().Query()
            .Where(pc => categoryIds.Contains(pc.CategoryId))
            .Select(pc => pc.ProductId)
            .Distinct()
            .ToList();

        var existing = unitOfWork.Repository<WallpaperProfile>().Query().Select(p => p.ProductId).ToHashSet();
        var repository = unitOfWork.Repository<WallpaperProfile>();
        var created = 0;
        foreach (var productId in productIds.Where(id => !existing.Contains(id)))
        {
            await repository.AddAsync(new WallpaperProfile(productId), cancellationToken);
            created++;
        }

        if (created > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }

    public static HashSet<Guid> ResolveCategoryTree(IUnitOfWork unitOfWork, IEnumerable<string> slugs)
    {
        var all = unitOfWork.Repository<Category>().Query().Select(c => new { c.Id, c.Slug, c.ParentCategoryId }).ToList();
        var result = all.Where(c => slugs.Contains(c.Slug)).Select(c => c.Id).ToHashSet();

        bool added;
        do
        {
            added = false;
            foreach (var c in all.Where(c => c.ParentCategoryId is Guid parent && result.Contains(parent) && !result.Contains(c.Id)))
            {
                result.Add(c.Id);
                added = true;
            }
        } while (added);

        return result;
    }
}
