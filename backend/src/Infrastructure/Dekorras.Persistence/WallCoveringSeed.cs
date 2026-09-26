using Dekorras.Domain.WallCovering;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Persistence;

/// <summary>Ölçüye özel duvar kağıdı modülünün başlangıç verisi: 8 malzeme (spec 1.2 ₺/m² fiyatları,
/// KDV HARİÇ) ve temel renk/oda etiketleri. Her parça ayrı ayrı idempotenttir - admin'in sonradan
/// değiştirdiği değerlerin üzerine YAZMAZ, yalnızca eksik kodları ekler.</summary>
public static class WallCoveringSeed
{
    // VARSAYIM: panel eni, maks. yükseklik, gramaj ve yangın sınıfı referans sitedeki tipik değerlerden
    // tahmin edildi; admin panelinden düzeltilebilir. Yapışkanlı olmayanlar tutkal gerektirir.
    private static readonly Material[] Materials =
    [
        new("plain", "Dokusuz", 699m, 100m, 330m, 180, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 1),
        new("textured", "Dokulu", 849m, 100m, 330m, 220, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 2),
        new("textile", "Tekstil", 999m, 100m, 330m, 280, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 3),
        new("self-adhesive", "Kendiliğinden Yapışkanlı Folyo", 1099m, 140m, 300m, 150, "C-s2,d0", isSelfAdhesive: true, requiresGlue: false, sortOrder: 4),
        new("premium-textile", "Premium Tekstil", 1399m, 100m, 330m, 320, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 5),
        new("canvas-adhesive", "Canvas Yapışkanlı", 1499m, 140m, 300m, 350, "C-s2,d0", isSelfAdhesive: true, requiresGlue: false, sortOrder: 6),
        new("straw", "Hasır Dokulu", 1699m, 90m, 300m, 300, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 7),
        new("metallic", "Gümüş / Gold", 3499m, 90m, 300m, 280, "B-s1,d0", isSelfAdhesive: false, requiresGlue: true, sortOrder: 8),
    ];

    private static readonly Tag[] Tags =
    [
        new(TagGroup.Color, "red", "Kırmızı", "#c0392b"),
        new(TagGroup.Color, "orange", "Turuncu", "#e67e22"),
        new(TagGroup.Color, "yellow", "Sarı", "#f1c40f"),
        new(TagGroup.Color, "green", "Yeşil", "#27ae60"),
        new(TagGroup.Color, "blue", "Mavi", "#2e86c1"),
        new(TagGroup.Color, "purple", "Mor", "#8e44ad"),
        new(TagGroup.Color, "pink", "Pembe", "#f1948a"),
        new(TagGroup.Color, "brown", "Kahverengi", "#8d6e63"),
        new(TagGroup.Color, "beige", "Bej", "#e8dcc4"),
        new(TagGroup.Color, "gray", "Gri", "#95a5a6"),
        new(TagGroup.Color, "black", "Siyah", "#222222"),
        new(TagGroup.Color, "white", "Beyaz", "#f7f7f7"),
        new(TagGroup.Room, "salon", "Salon"),
        new(TagGroup.Room, "yatak-odasi", "Yatak Odası"),
        new(TagGroup.Room, "cocuk-odasi", "Çocuk Odası"),
        new(TagGroup.Room, "ofis", "Ofis"),
        new(TagGroup.Room, "mutfak", "Mutfak"),
        new(TagGroup.Room, "koridor", "Koridor"),
        new(TagGroup.Style, "modern", "Modern"),
        new(TagGroup.Style, "klasik", "Klasik"),
        new(TagGroup.Style, "minimal", "Minimal"),
        new(TagGroup.Style, "vintage", "Vintage"),
        new(TagGroup.Tone, "acik", "Açık"),
        new(TagGroup.Tone, "koyu", "Koyu"),
        new(TagGroup.Tone, "pastel", "Pastel"),
    ];

    public static async Task SeedAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existingCodes = await dbContext.Materials.Select(m => m.Code).ToListAsync(cancellationToken);
        dbContext.Materials.AddRange(Materials.Where(m => !existingCodes.Contains(m.Code)).Select(Clone));

        var existingTags = (await dbContext.Tags.Select(t => new { t.Group, t.Value }).ToListAsync(cancellationToken))
            .Select(t => (t.Group, t.Value)).ToHashSet();
        dbContext.Tags.AddRange(Tags.Where(t => !existingTags.Contains((t.Group, t.Value))).Select(t => new Tag(t.Group, t.Value, t.Label, t.Hex)));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Statik dizideki örnekler EF tarafından izlenmesin (aynı DbContext'e iki kez eklenmesin) diye kopya.
    private static Material Clone(Material m) =>
        new(m.Code, m.Name, m.PricePerM2, m.PanelWidthCm, m.MaxHeightCm, m.WeightGsm, m.FireRating, m.IsSelfAdhesive, m.RequiresGlue, m.SortOrder, m.BleedCm, m.MinBillableAreaM2);
}
