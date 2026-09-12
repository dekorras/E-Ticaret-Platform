// Dekorras Katalog Aktarım Aracı - dekorras.com'un (kullanıcının kendi şirketinin GERÇEK, canlı
// OpenCart mağazası) gerçek kategori/ürün verisini bu sistemin gerçek dev DB'sine aktarır.
// Bkz. plan "Admin Panel Velzon Reskin + dekorras.com Katalog Aktarımı" (İş 2).
//
// Yaklaşım: OpenCart'ın SEO slug'larına GEREK DUYMADAN "ham" route'ları kullanır
// (index.php?route=product/category&path={id}, index.php?route=product/product&product_id={id})
// - kategori/ürün ID'leri sayfa gövdesindeki class'lardan ve "İlgili Kategoriler" filtre
// panelinden çıkarılır. Ürün alanları (ad/açıklama/fiyat/SKU/görsel) sayfadaki standart
// application/ld+json "Product" bloğundan okunur - HTML seçicilerinden çok daha güvenilir.
//
// Kendi slug/ProductCode'umuzu ÜRETİRİZ (OpenCart'ın kendi SEO slug'ına bağımlı olmadan):
// slug = "{isim-slug}-{opencart-id}" - hem benzersizliği garanti eder hem kaynakla eşleşmeyi
// kolaylaştırır. İdempotent: her kategori/ürün oluşturmadan önce bu slug DB'de aranır, varsa
// atlanır - araç YARIDA kesilip TEKRAR çalıştırılabilir.

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Domain.Catalog;
using Dekorras.Infrastructure.Storage;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

var sqlPassword = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD")
    ?? throw new InvalidOperationException("DEKORRAS_SQL_PASSWORD ortam değişkeni ayarlanmamış - yerel SQLEXPRESS 'sa' şifrenizi bu değişkene atayın.");
string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={sqlPassword};TrustServerCertificate=True;";
const string BaseUrl = "https://dekorras.com";
const string LanguageCode = "tr";
const decimal DefaultTaxRatePercentage = 20m;
const int PageSize = 100;

var rootCategories = new (int Id, string Name)[]
{
    (138, "Duvar Kaplamaları"),
    (129, "Duvar Kağıtları"),
    (128, "Aksesuarlar"),
    (141, "Posterler"),
};

var logPath = Path.Combine(AppContext.BaseDirectory, "import-log.txt");
var logWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };

void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(line);
    logWriter.WriteLine(line);
}

var random = new Random();
using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
httpClient.Timeout = TimeSpan.FromSeconds(30);

async Task<string?> GetWithRetryAsync(string url)
{
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            await Task.Delay(random.Next(300, 800));
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync();
            Log($"  UYARI: {url} -> HTTP {(int)response.StatusCode} (deneme {attempt}/3)");
        }
        catch (Exception ex)
        {
            Log($"  UYARI: {url} -> {ex.Message} (deneme {attempt}/3)");
        }
        await Task.Delay(1000 * attempt);
    }
    return null;
}

ApplicationDbContext CreateDbContext() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

string Slugify(string input)
{
    var replacements = new (string From, string To)[]
    {
        ("ı", "i"), ("İ", "I"), ("ğ", "g"), ("Ğ", "G"), ("ü", "u"), ("Ü", "U"),
        ("ş", "s"), ("Ş", "S"), ("ö", "o"), ("Ö", "O"), ("ç", "c"), ("Ç", "C"),
    };
    var result = input;
    foreach (var (from, to) in replacements) result = result.Replace(from, to);
    result = result.ToLowerInvariant();
    result = Regex.Replace(result, "[^a-z0-9]+", "-").Trim('-');
    return result.Length > 180 ? result[..180].Trim('-') : result;
}

// --- Kategori keşfi: "İlgili Kategoriler" filtre paneli (data-filter-trigger name="c") ---
List<(int Id, string Name, int Count)> ParseChildCategories(string html)
{
    var results = new List<(int, string, int)>();
    var matches = Regex.Matches(html,
        "data-filter-trigger name=\"c\" value=\"(\\d+)\"\\s*>\\s*<span class=\"links-text\">([^<]+)</span>\\s*<span class=\"count-badge\">(\\d+)</span>",
        RegexOptions.Singleline);
    foreach (Match m in matches)
    {
        results.Add((int.Parse(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value).Trim(), int.Parse(m.Groups[3].Value)));
    }
    return results;
}

HashSet<int> ParseProductIdsFromListing(string html)
{
    var ids = new HashSet<int>();
    foreach (Match m in Regex.Matches(html, @"cart\.add\('(\d+)'"))
        ids.Add(int.Parse(m.Groups[1].Value));
    return ids;
}

int ParseTotalCount(string html)
{
    var m = Regex.Match(html, @"toplam:\s*(\d+)");
    return m.Success ? int.Parse(m.Groups[1].Value) : 0;
}

// --- Ürün detayı: application/ld+json "Product" bloğu ---
ScrapedProduct? ParseProductJsonLd(string html, int productId)
{
    foreach (Match m in Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline))
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(m.Groups[1].Value); }
        catch { continue; }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("@type", out var typeProp) || typeProp.GetString() != "Product") continue;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? $"Ürün {productId}" : $"Ürün {productId}";
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            var imageUrl = root.TryGetProperty("image", out var img) ? img.GetString() : null;
            var sku = root.TryGetProperty("sku", out var s) ? s.GetString() : null;
            var model = root.TryGetProperty("model", out var mo) ? mo.GetString() : null;
            var brand = root.TryGetProperty("brand", out var b) && b.TryGetProperty("name", out var bn) ? bn.GetString()?.Trim() : null;

            decimal price = 0m;
            var inStock = true;
            if (root.TryGetProperty("offers", out var offers))
            {
                if (offers.TryGetProperty("price", out var p) && decimal.TryParse(p.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice))
                    price = parsedPrice;
                if (offers.TryGetProperty("availability", out var av))
                    inStock = (av.GetString() ?? "").Contains("InStock", StringComparison.OrdinalIgnoreCase);
            }

            return new ScrapedProduct(productId, name.Trim(), description?.Trim(), price, sku ?? model, imageUrl, inStock, brand);
        }
    }
    return null;
}

Guid? TryGetExistingCategoryId(ApplicationDbContext db, string slug) =>
    db.Categories.Where(c => c.Slug == slug).Select(c => (Guid?)c.Id).FirstOrDefault();

Guid? TryGetExistingProductId(ApplicationDbContext db, string slug) =>
    db.Products.Where(p => p.Slug == slug).Select(p => (Guid?)p.Id).FirstOrDefault();

Guid? TryGetExistingBrandId(ApplicationDbContext db, string slug) =>
    db.Brands.Where(b => b.Slug == slug).Select(b => (Guid?)b.Id).FirstOrDefault();

async Task<Stream?> DownloadImageAsync(string url)
{
    try
    {
        await Task.Delay(random.Next(200, 500));
        var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new MemoryStream(bytes);
    }
    catch
    {
        return null;
    }
}

// --- Faz 1: kategori ağacını keşfet (DB'ye DOKUNMADAN, saf HTTP/HTML) ---
Log("=== Kategori ağacı keşfi başlıyor ===");
var discovered = new Dictionary<int, DiscoveredCategory>();
var toVisit = new Queue<(int Id, string Name, int? ParentId)>();
foreach (var root in rootCategories) toVisit.Enqueue((root.Id, root.Name, null));

while (toVisit.Count > 0)
{
    var (id, name, parentId) = toVisit.Dequeue();
    if (discovered.ContainsKey(id)) continue;

    var url = $"{BaseUrl}/index.php?route=product/category&path={id}&limit={PageSize}&page=1";
    var html = await GetWithRetryAsync(url);
    if (html is null) { Log($"  ATLANDI (erişilemedi): kategori {id} '{name}'"); continue; }

    var totalProducts = ParseTotalCount(html);
    discovered[id] = new DiscoveredCategory(id, name, parentId, totalProducts);
    Log($"  Kategori keşfedildi: [{id}] {name} (üst: {parentId?.ToString() ?? "-"}, ~{totalProducts} ürün)");

    foreach (var child in ParseChildCategories(html))
    {
        if (!discovered.ContainsKey(child.Id))
            toVisit.Enqueue((child.Id, child.Name, id));
    }
}
Log($"=== Kategori keşfi tamamlandı: {discovered.Count} kategori ===");

// --- Faz 2: kategorileri DB'de oluştur (üst→alt sırayla, idempotent) ---
Log("=== Kategoriler oluşturuluyor ===");
var categoryGuids = new Dictionary<int, Guid>();
var remaining = new Queue<DiscoveredCategory>(discovered.Values);
while (remaining.Count > 0)
{
    var cat = remaining.Dequeue();
    if (cat.ParentId is not null && !categoryGuids.ContainsKey(cat.ParentId.Value))
    {
        remaining.Enqueue(cat); // üst henüz oluşmadı, sona at
        continue;
    }

    var slug = $"{Slugify(cat.Name)}-{cat.Id}";
    await using var db = CreateDbContext();
    var existing = TryGetExistingCategoryId(db, slug);
    if (existing is not null)
    {
        categoryGuids[cat.Id] = existing.Value;
        Log($"  Zaten var, atlandı: [{cat.Id}] {cat.Name}");
        continue;
    }

    var unitOfWork = new UnitOfWork(db);
    var handler = new CreateCategoryCommandHandler(unitOfWork);
    Guid? parentGuid = cat.ParentId is not null ? categoryGuids[cat.ParentId.Value] : null;
    var newId = await handler.Handle(new CreateCategoryCommand(slug, parentGuid, DisplayOrder: 1, LanguageCode, cat.Name, Description: null), CancellationToken.None);
    categoryGuids[cat.Id] = newId;
    Log($"  Oluşturuldu: [{cat.Id}] {cat.Name} -> {newId}");
}
Log($"=== {categoryGuids.Count} kategori hazır ===");

// --- Faz 3: her kategorinin ürün listesini tara, product_id -> [category_id] eşlemesi çıkar ---
Log("=== Ürün listeleri taranıyor ===");
var productCategoryIds = new Dictionary<int, List<int>>();
foreach (var cat in discovered.Values)
{
    var page = 1;
    while (true)
    {
        var url = $"{BaseUrl}/index.php?route=product/category&path={cat.Id}&limit={PageSize}&page={page}";
        var html = await GetWithRetryAsync(url);
        if (html is null) break;

        var ids = ParseProductIdsFromListing(html);
        if (ids.Count == 0) break;

        foreach (var pid in ids)
        {
            if (!productCategoryIds.TryGetValue(pid, out var list))
                productCategoryIds[pid] = list = [];
            if (!list.Contains(cat.Id)) list.Add(cat.Id);
        }

        Log($"  [{cat.Id}] {cat.Name} sayfa {page}: {ids.Count} ürün (toplam benzersiz: {productCategoryIds.Count})");
        if (ids.Count < PageSize) break; // son sayfa
        page++;
    }
}
Log($"=== Toplam {productCategoryIds.Count} benzersiz ürün keşfedildi ===");

// --- Faz 4: her ürünü oluştur ---
Log("=== Ürünler oluşturuluyor ===");
var brandGuids = new Dictionary<string, Guid>();
var processed = 0;
var created = 0;
var skipped = 0;
var failed = 0;

foreach (var (productId, categoryIds) in productCategoryIds)
{
    processed++;
    var detailUrl = $"{BaseUrl}/index.php?route=product/product&product_id={productId}";
    var html = await GetWithRetryAsync(detailUrl);
    if (html is null) { failed++; Log($"  [{processed}/{productCategoryIds.Count}] BAŞARISIZ (erişilemedi): ürün {productId}"); continue; }

    var scraped = ParseProductJsonLd(html, productId);
    if (scraped is null) { failed++; Log($"  [{processed}/{productCategoryIds.Count}] BAŞARISIZ (JSON-LD yok): ürün {productId}"); continue; }

    var slug = $"{Slugify(scraped.Name)}-{productId}";

    await using var db = CreateDbContext();
    if (TryGetExistingProductId(db, slug) is not null)
    {
        skipped++;
        Log($"  [{processed}/{productCategoryIds.Count}] Zaten var, atlandı: {scraped.Name}");
        continue;
    }

    var unitOfWork = new UnitOfWork(db);
    var fileStorage = new LocalFileStorage(LocalFileStorage.SharedUploadsRoot);

    Guid? brandId = null;
    if (!string.IsNullOrWhiteSpace(scraped.Brand))
    {
        var brandSlug = Slugify(scraped.Brand);
        if (!brandGuids.TryGetValue(brandSlug, out var bId))
        {
            bId = TryGetExistingBrandId(db, brandSlug) ?? await new CreateBrandCommandHandler(unitOfWork).Handle(new CreateBrandCommand(scraped.Brand, brandSlug), CancellationToken.None);
            brandGuids[brandSlug] = bId;
        }
        brandId = bId;
    }

    var productCode = !string.IsNullOrWhiteSpace(scraped.Sku) ? scraped.Sku! : $"OC-{productId}";
    var categoryGuidList = categoryIds.Where(categoryGuids.ContainsKey).Select(id => categoryGuids[id]).Distinct().ToList();
    if (categoryGuidList.Count == 0) { failed++; Log($"  [{processed}/{productCategoryIds.Count}] BAŞARISIZ (kategori yok): {scraped.Name}"); continue; }

    try
    {
        var productHandler = new CreateProductCommandHandler(unitOfWork);
        var newProductId = await productHandler.Handle(new CreateProductCommand(
            slug, productCode, scraped.PriceTry, DefaultTaxRatePercentage, UnitOfMeasure.Piece, MinimumOrderQuantity: 1,
            StockQuantity: scraped.InStock ? 100 : 0, BrandId: brandId, CategoryIds: categoryGuidList,
            LanguageCode: LanguageCode, Name: scraped.Name, Description: scraped.Description), CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(scraped.ImageUrl))
        {
            var imageStream = await DownloadImageAsync(scraped.ImageUrl);
            if (imageStream is not null)
            {
                await using (imageStream)
                {
                    var imageHandler = new AddProductImageCommandHandler(unitOfWork, fileStorage);
                    var fileName = $"{productId}.jpg";
                    var contentType = scraped.ImageUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                    await imageHandler.Handle(new AddProductImageCommand(newProductId, imageStream, fileName, contentType), CancellationToken.None);
                }
            }
        }

        var publishHandler = new SetProductPublishedCommandHandler(unitOfWork);
        await publishHandler.Handle(new SetProductPublishedCommand(newProductId, Published: true), CancellationToken.None);

        created++;
        Log($"  [{processed}/{productCategoryIds.Count}] Oluşturuldu: {scraped.Name} ({scraped.PriceTry:N2} TRY)");
    }
    catch (Exception ex)
    {
        failed++;
        Log($"  [{processed}/{productCategoryIds.Count}] BAŞARISIZ (hata): {scraped.Name} -> {ex.Message}");
    }
}

Log("=== Aktarım tamamlandı ===");
Log($"Kategoriler: {categoryGuids.Count}, Ürünler - oluşturulan: {created}, atlanan: {skipped}, başarısız: {failed}, toplam işlenen: {processed}");
logWriter.Dispose();

internal sealed record DiscoveredCategory(int Id, string Name, int? ParentId, int TotalProducts);
internal sealed record ScrapedProduct(int ProductId, string Name, string? Description, decimal PriceTry, string? Sku, string? ImageUrl, bool InStock, string? Brand);
