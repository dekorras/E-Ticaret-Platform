using System.Globalization;
using System.IO.Compression;
using System.Text;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record BulkUploadFile(string FileName, byte[] Content);

public sealed record BulkImportResultRow(int Line, string File, bool Success, string Message, string? Slug, Guid? ProductId, int WidthPx, int HeightPx, bool LowResolution);

/// <summary>Toplu poster yükleme (spec 1.10): çoklu görsel ve/veya ZIP + isteğe bağlı CSV.
/// CSV başlıkları (ayırıcı ; veya ,): dosya, baslik, slug, etiketler (grup:değer|grup:değer), tur (mural|pattern),
/// tekrar_en, tekrar_boy, tekrar_tipi (straight|halfdrop). CSV yoksa her görsel dosya adından başlıkla eklenir.
/// Her satır bağımsızdır: hatalı satır raporlanır, diğerleri işlenir.
/// VARSAYIM: en uzun kenarı 3000 px altındaki görseller "düşük çözünürlük" uyarısıyla yine eklenir.</summary>
public sealed record BulkPosterImportCommand(IReadOnlyList<BulkUploadFile> Files, string CategorySlug = "posterler-141", bool Publish = true)
    : IRequest<IReadOnlyList<BulkImportResultRow>>;

public sealed class BulkPosterImportCommandHandler(IUnitOfWork unitOfWork, ISender sender, IImageDerivativeService images)
    : IRequestHandler<BulkPosterImportCommand, IReadOnlyList<BulkImportResultRow>>
{
    public const int LowResolutionLongEdgePx = 3000;
    private const int MaxImages = 500;
    private const long MaxTotalBytes = 1024L * 1024 * 1024;
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private sealed record CsvRow(int Line, string File, string? Title, string? Slug, string? Tags, string? Type, decimal? RepeatW, decimal? RepeatH, string? RepeatType);

    public async Task<IReadOnlyList<BulkImportResultRow>> Handle(BulkPosterImportCommand request, CancellationToken cancellationToken)
    {
        var (imageFiles, csvText) = Expand(request.Files);
        var results = new List<BulkImportResultRow>();

        var categoryId = unitOfWork.Repository<Category>().Query().Where(c => c.Slug == request.CategorySlug).Select(c => (Guid?)c.Id).FirstOrDefault()
            ?? throw new InvalidOperationException($"'{request.CategorySlug}' kategorisi bulunamadı.");
        var basePrice = unitOfWork.Repository<Material>().Query().Where(m => m.IsActive).Select(m => (decimal?)m.PricePerM2).Min() ?? 1m;
        var tags = unitOfWork.Repository<Tag>().Query().ToList();

        var rows = csvText is null
            ? imageFiles.Keys.Select((name, i) => new CsvRow(i + 1, name, null, null, null, null, null, null, null)).ToList()
            : ParseCsv(csvText);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!imageFiles.TryGetValue(row.File, out var bytes))
            {
                results.Add(Fail(row, $"'{row.File}' dosyası yüklenenler arasında yok."));
                continue;
            }

            using var probe = new MemoryStream(bytes);
            var inspection = images.Inspect(probe);
            if (!inspection.IsValid) { results.Add(Fail(row, inspection.Error ?? "Geçersiz görsel.")); continue; }

            var title = string.IsNullOrWhiteSpace(row.Title) ? TitleFromFile(row.File) : row.Title.Trim();
            var slug = Slugify(string.IsNullOrWhiteSpace(row.Slug) ? title : row.Slug);
            if (slug.Length == 0) { results.Add(Fail(row, "Geçerli bir slug üretilemedi.")); continue; }
            if (unitOfWork.Repository<Product>().Query().Any(p => p.Slug == slug)) { results.Add(Fail(row, $"'{slug}' slug'ı zaten kullanılıyor.")); continue; }

            var type = row.Type?.Trim().ToLowerInvariant() == "pattern" ? WallProductType.Pattern : WallProductType.Mural;
            if (type == WallProductType.Pattern && (row.RepeatW is not > 0 || row.RepeatH is not > 0))
            {
                results.Add(Fail(row, "Desenli üründe tekrar_en ve tekrar_boy zorunludur."));
                continue;
            }

            var tagIds = new List<Guid>();
            var unknownTags = new List<string>();
            foreach (var key in (row.Tags ?? "").Split(['|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = key.Split(':', 2);
                var tag = parts.Length == 2 && Enum.TryParse<TagGroup>(parts[0], true, out var g)
                    ? tags.FirstOrDefault(t => t.Group == g && t.Value == Tag.Normalize(parts[1]))
                    : null;
                if (tag is null) unknownTags.Add(key); else tagIds.Add(tag.Id);
            }

            try
            {
                var code = $"PST-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                var productId = await sender.Send(new CreateProductCommand(slug, code, basePrice, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
                    BrandId: null, CategoryIds: [categoryId], LanguageCode: "tr", Name: title, Description: null), cancellationToken);
                await sender.Send(new SetProductTrackStockCommand(productId, false), cancellationToken);
                await sender.Send(new AddProductImageCommand(productId, new MemoryStream(bytes), Path.GetFileName(row.File), MimeOf(inspection.Extension)), cancellationToken);
                if (request.Publish) await sender.Send(new SetProductPublishedCommand(productId, Published: true), cancellationToken);

                var repeatType = row.RepeatType?.Trim().ToLowerInvariant() is "halfdrop" or "half-drop" or "yarim" ? RepeatType.HalfDrop : RepeatType.Straight;
                await sender.Send(new SaveWallpaperProfileCommand(productId, true, type, row.RepeatW, row.RepeatH, repeatType, 0), cancellationToken);
                if (tagIds.Count > 0) await sender.Send(new SetProductTagsCommand(productId, tagIds), cancellationToken);
                // Türevler + otomatik renk etiketleri + sahne küçük resmi arka plan işçisinde üretilir (istek hızlı döner).

                var longEdge = Math.Max(inspection.WidthPx, inspection.HeightPx);
                var low = longEdge < LowResolutionLongEdgePx;
                var message = "Eklendi." + (low ? $" Düşük çözünürlük ({inspection.WidthPx}×{inspection.HeightPx} px): büyük ölçülerde baskı kalitesi düşebilir." : "")
                              + (unknownTags.Count > 0 ? $" Tanınmayan etiketler atlandı: {string.Join(", ", unknownTags)}." : "");
                results.Add(new BulkImportResultRow(row.Line, row.File, true, message, slug, productId, inspection.WidthPx, inspection.HeightPx, low));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(Fail(row, ex.Message));
            }
        }
        return results;
    }

    private static BulkImportResultRow Fail(CsvRow row, string message) => new(row.Line, row.File, false, message, null, null, 0, 0, false);

    /// <summary>ZIP'leri açar; görselleri dosya adına göre (klasör yolu atılır) toplar, ilk CSV'yi döner.</summary>
    private static (Dictionary<string, byte[]> Images, string? Csv) Expand(IReadOnlyList<BulkUploadFile> files)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? csv = null;
        long total = 0;

        void Add(string name, byte[] content)
        {
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".csv") { csv ??= Encoding.UTF8.GetString(content).TrimStart('﻿'); return; }
            if (!ImageExtensions.Contains(ext)) return;
            if (result.Count >= MaxImages) throw new InvalidOperationException($"Tek seferde en fazla {MaxImages} görsel yüklenebilir.");
            total += content.LongLength;
            if (total > MaxTotalBytes) throw new InvalidOperationException("Toplam yükleme 1 GB'ı aşıyor.");
            result[Path.GetFileName(name)] = content;
        }

        foreach (var file in files)
        {
            if (Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
                foreach (var entry in zip.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/') && !e.FullName.StartsWith("__MACOSX")))
                {
                    // ZIP bombası koruması: tek girdi 100 MB'ı aşamaz.
                    if (entry.Length > 100 * 1024 * 1024) throw new InvalidOperationException($"'{entry.FullName}' çok büyük.");
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    Add(entry.FullName, ms.ToArray());
                }
            }
            else Add(file.FileName, file.Content);
        }
        return (result, csv);
    }

    private static List<CsvRow> ParseCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) return [];
        var separator = lines[0].Count(c => c == ';') >= lines[0].Count(c => c == ',') ? ';' : ',';
        var header = lines[0].Split(separator).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.IndexOf(name);
        var (fDosya, fBaslik, fSlug, fEtiket, fTur, fTekrarEn, fTekrarBoy, fTekrarTipi) =
            (Col("dosya"), Col("baslik"), Col("slug"), Col("etiketler"), Col("tur"), Col("tekrar_en"), Col("tekrar_boy"), Col("tekrar_tipi"));
        if (fDosya < 0) throw new InvalidOperationException("CSV'de 'dosya' sütunu zorunludur.");

        var rows = new List<CsvRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = lines[i].Split(separator).Select(c => c.Trim().Trim('"')).ToArray();
            string? Get(int idx) => idx >= 0 && idx < cells.Length && cells[idx].Length > 0 ? cells[idx] : null;
            decimal? Dec(int idx) => decimal.TryParse(Get(idx)?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
            rows.Add(new CsvRow(i + 1, Get(fDosya) ?? "", Get(fBaslik), Get(fSlug), Get(fEtiket), Get(fTur), Dec(fTekrarEn), Dec(fTekrarBoy), Get(fTekrarTipi)));
        }
        return rows;
    }

    private static string TitleFromFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file).Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(name.Trim());
    }

    public static string Slugify(string text)
    {
        var map = new Dictionary<char, string> { ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u", ['İ'] = "i", ['Ç'] = "c", ['Ğ'] = "g", ['Ö'] = "o", ['Ş'] = "s", ['Ü'] = "u" };
        var sb = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            if (map.TryGetValue(ch, out var r)) sb.Append(r);
            else if (char.IsLetterOrDigit(ch) && ch < 128) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private static string MimeOf(string extension) => extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
}
