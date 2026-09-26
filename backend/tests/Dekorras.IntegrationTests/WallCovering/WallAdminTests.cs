using System.IO.Compression;
using System.Text;
using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.SystemAdmin.Commands;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Dekorras.IntegrationTests.WallCovering;

/// <summary>Dilim 10 admin komutları gerçek SQL Server + gerçek ImageSharp ile: malzeme/etiket CRUD,
/// numune/tutkal ürünlerinin oluşturulması, ürün profili + fiyat istisnası (fiyatlamaya yansır),
/// oda sahnesi oluşturma/köşe/katman/varsayılan, embed istemcisi doğrulaması, ayarlar ve
/// toplu poster yükleme (ZIP + CSV, satır bazlı hata raporu).</summary>
public sealed class WallAdminTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWallAdminTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
    private ServiceProvider _serviceProvider = default!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString, ["ConnectionStrings:Redis"] = "" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddPersistence(configuration);
        services.AddInfrastructure(configuration);
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await WallCoveringSeed.SeedAsync(db);
    }

    public async Task DisposeAsync()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();
            var urls = await db.RoomScenes.Select(s => new[] { s.BaseImageUrl, s.ShadowMapUrl, s.ForegroundMaskUrl }).ToListAsync();
            var imageUrls = await db.Set<ProductImage>().Select(i => i.Url).ToListAsync();
            foreach (var url in urls.SelectMany(u => u).Concat(imageUrls).Where(u => u is not null))
                await store.DeletePublicAsync(url!, CancellationToken.None);
            await db.Database.EnsureDeletedAsync();
        }
        await _serviceProvider.DisposeAsync();
    }

    private static byte[] Image(int width, int height, bool png = false)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 120, 60));
        using var ms = new MemoryStream();
        if (png) image.SaveAsPng(ms); else image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static readonly SemaphoreSlim CategoryLock = new(1, 1);

    private async Task<Guid> ProductAsync(ISender sender, string slug)
    {
        Guid category;
        await CategoryLock.WaitAsync();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            category = await db.Categories.Where(c => c.Slug == "posterler-141").Select(c => (Guid?)c.Id).FirstOrDefaultAsync()
                ?? await sender.Send(new CreateCategoryCommand("posterler-141", null, 0, "tr", "Posterler", null));
        }
        finally { CategoryLock.Release(); }
        return await sender.Send(new CreateProductCommand(slug, slug.ToUpperInvariant(), 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
            BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: slug, Description: null));
    }

    [Fact]
    public async Task Malzeme_KaydetVeKodCakismasi_NumuneTutkalUrunleriBirKezOlusturulur()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var id = await sender.Send(new SaveMaterialCommand(null, "Vinil", "Vinil", "Silinebilir", "Silinebilir\nDayanıklı", 899m, 100m, 330m, 280, "B-s1,d0",
            false, true, 5m, 1m, 20, null, null));
        var list = await sender.Send(new GetMaterialsAdminQuery());
        var vinyl = Assert.Single(list, m => m.Id == id);
        Assert.Equal("vinil", vinyl.Code); // kod küçük harfe normalize
        Assert.Equal(899m, vinyl.PricePerM2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new SaveMaterialCommand(null, "vinil", "İkinci", null, null, 1m, 100m, 300m, 100, null,
            false, true, 5m, 1m, 21, null, null)));

        await sender.Send(new SetMaterialActiveCommand(id, false));
        Assert.DoesNotContain(await sender.Send(new GetMaterialsQuery()), m => m.Code == "vinil");

        var created = await sender.Send(new CreateSampleAndGlueProductsCommand());
        Assert.True(created >= 2);
        Assert.Equal(0, await sender.Send(new CreateSampleAndGlueProductsCommand())); // ikinci çağrı hiçbir şey oluşturmaz

        using var verify = _serviceProvider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var materials = await db.Materials.ToListAsync();
        Assert.All(materials, m => Assert.NotNull(m.SampleProductId));
        Assert.All(materials.Where(m => m.RequiresGlue), m => Assert.NotNull(m.GlueProductId));
        Assert.All(materials.Where(m => !m.RequiresGlue), m => Assert.Null(m.GlueProductId));
        Assert.Single(materials.Where(m => m.GlueProductId != null).Select(m => m.GlueProductId).Distinct()); // tek tutkal ürünü
    }

    [Fact]
    public async Task Etiket_EkleCakismaUrunAtaSil_ProfilIstisnasiFiyataYansir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var productId = await ProductAsync(sender, "etiket-urunu");

        var tagId = await sender.Send(new SaveTagCommand(null, TagGroup.Theme, " Uzay ", "Uzay", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new SaveTagCommand(null, TagGroup.Theme, "uzay", "Tekrar", null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new SaveTagCommand(null, TagGroup.Color, "mor2", "Mor", "mor")));

        await sender.Send(new SetProductTagsCommand(productId, [tagId]));
        Assert.Equal([tagId], await sender.Send(new GetProductTagIdsQuery(productId)));
        Assert.Equal(1, (await sender.Send(new GetTagsAdminQuery())).Single(t => t.Id == tagId).ProductCount);

        await sender.Send(new DeleteTagCommand(tagId));
        Assert.Empty(await sender.Send(new GetProductTagIdsQuery(productId)));

        // Profil: desen türü tekrar ölçüsü olmadan kaydedilemez; istisna fiyatı ve kapatma fiyatlamaya yansır.
        await Assert.ThrowsAnyAsync<Exception>(() => sender.Send(new SaveWallpaperProfileCommand(productId, true, WallProductType.Pattern, null, null, RepeatType.Straight, 0)));
        await sender.Send(new SaveWallpaperProfileCommand(productId, true, WallProductType.Pattern, 53m, 53m, RepeatType.HalfDrop, 7));
        var profile = await sender.Send(new GetWallpaperProfileAdminQuery(productId));
        Assert.True(profile.Exists);
        Assert.Equal(RepeatType.HalfDrop, profile.RepeatType);

        var plain = profile.Overrides.First(o => o.MaterialCode == "plain");
        var textured = profile.Overrides.First(o => o.MaterialCode == "textured");
        await sender.Send(new SetMaterialOverrideCommand(productId, plain.MaterialId, 555m, true));
        await sender.Send(new SetMaterialOverrideCommand(productId, textured.MaterialId, null, false));
        var materials = await sender.Send(new GetMaterialsQuery(productId));
        Assert.Equal(555m, materials.Single(m => m.Code == "plain").PricePerM2);
        Assert.DoesNotContain(materials, m => m.Code == "textured");

        // İstisna kaldırılınca genel fiyat geri gelir.
        await sender.Send(new SetMaterialOverrideCommand(productId, plain.MaterialId, null, true));
        Assert.Equal(plain.BasePricePerM2, (await sender.Send(new GetMaterialsQuery(productId))).Single(m => m.Code == "plain").PricePerM2);

        await sender.Send(new QueueDerivativesCommand(productId, ReloadOriginal: true));
        Assert.Null((await sender.Send(new GetWallpaperProfileAdminQuery(productId))).DerivativesGeneratedAtUtc);
    }

    [Fact]
    public async Task GenelUrunKarti_DuvarKagidiniTanir_VeOlcusuzSepeteEklemeyiReddeder()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var wallId = await ProductAsync(sender, "kart-duvar");
        var plainId = await ProductAsync(sender, "kart-normal");
        await sender.Send(new SetProductPublishedCommand(wallId, Published: true));
        await sender.Send(new SaveWallpaperProfileCommand(wallId, true, WallProductType.Mural, null, null, RepeatType.Straight, 0));

        var info = await sender.Send(new GetWallCardInfoQuery([wallId, plainId]));
        Assert.Equal([wallId], info.Keys.ToList());
        var cheapest = (await sender.Send(new GetMaterialsQuery())).Min(m => m.PricePerM2);
        Assert.Equal(cheapest, info[wallId]);

        var plain = (await sender.Send(new GetWallpaperProfileAdminQuery(wallId))).Overrides.First(o => o.MaterialCode == "plain");
        await sender.Send(new SetMaterialOverrideCommand(wallId, plain.MaterialId, 111m, true));
        Assert.Equal(111m, (await sender.Send(new GetWallCardInfoQuery([wallId])))[wallId]);

        // Düz "Sepete Ekle" ölçüye özel üründe reddedilir ve ürün sayfasına yönlendirmek için slug taşır.
        var ex = await Assert.ThrowsAsync<WallConfigurationRequiredException>(() =>
            sender.Send(new Dekorras.Application.Ordering.Storefront.AddCartItemCommand("kart-sepet", wallId, 1)));
        Assert.Equal("kart-duvar", ex.ProductSlug);
    }

    [Fact]
    public async Task KategoriAgaci_AltKategoriSayilari_UrunKokKategorisi_VeKatalogSuzgeci()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var posters = await db.Categories.Where(c => c.Slug == "posterler-141").Select(c => (Guid?)c.Id).FirstOrDefaultAsync()
            ?? await sender.Send(new CreateCategoryCommand("posterler-141", null, 0, "tr", "Posterler", null));
        var wallpapers = await sender.Send(new CreateCategoryCommand("duvar-kagitlari-129", null, 1, "tr", "Duvar Kağıtları", null));
        var sub = await sender.Send(new CreateCategoryCommand("duvar-posterleri-142", posters, 0, "tr", "Duvar Posterleri", null));

        async Task<Guid> Make(string slug, Guid category)
        {
            var id = await sender.Send(new CreateProductCommand(slug, slug.ToUpperInvariant(), 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
                BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: slug, Description: null));
            await sender.Send(new SetProductPublishedCommand(id, Published: true));
            await sender.Send(new SaveWallpaperProfileCommand(id, true, WallProductType.Mural, null, null, RepeatType.Straight, 0));
            return id;
        }
        var posterId = await Make("kat-poster", sub);
        await Make("kat-duvar", wallpapers);

        var result = await sender.Send(new GetWallCategoriesQuery(posterId));
        var byslug = result.Categories.ToDictionary(c => c.Slug);
        Assert.True(byslug["posterler-141"].ProductCount >= 1); // alt kategorideki ürün köke de sayılır
        Assert.Equal(1, byslug["duvar-posterleri-142"].Depth);
        Assert.Equal("posterler-141", result.ProductCategorySlug); // alt kategoriden köke çıkılır

        var filtered = await sender.Send(new GetWallCatalogQuery(Category: "posterler-141", PageSize: 60));
        Assert.Contains(filtered.Items, i => i.Slug == "kat-poster");
        Assert.DoesNotContain(filtered.Items, i => i.Slug == "kat-duvar");
        Assert.Empty((await sender.Send(new GetWallCatalogQuery(Category: "olmayan-kategori"))).Items);
    }

    [Fact]
    public async Task GolgeHaritasi_KoyuEsyaPostereLekeOlarakYayilmaz_GercekGolgeKorunur()
    {
        using var scope = _serviceProvider.CreateScope();
        var images = scope.ServiceProvider.GetRequiredService<IImageDerivativeService>();

        // 800×500 krem duvar; sağda koyu "koltuk" (dörtgen içinde), solda duvarın kendi yumuşak gölgesi.
        using var photo = new Image<Rgba32>(800, 500);
        photo.ProcessPixelRows(a =>
        {
            for (var y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var wall = (byte)(x < 200 ? 200 : 235); // sol şerit %15 daha karanlık (gerçek gölge)
                    row[x] = x is >= 500 and < 650 && y >= 250 ? new Rgba32(70, 45, 30) : new Rgba32(wall, wall, wall);
                }
            }
        });
        using var ms = new MemoryStream();
        await photo.SaveAsJpegAsync(ms);
        var quad = new WallQuad(new Point2(0, 0), new Point2(800, 0), new Point2(800, 400), new Point2(0, 400));

        var shadow = SixLabors.ImageSharp.Image.Load<L8>(await images.CreateShadowMapAsync(ms.ToArray(), quad, null, CancellationToken.None));
        Assert.True(shadow[575, 230].PackedValue > 225, $"Koltuğun üstü karardı: {shadow[575, 230].PackedValue}"); // eski yöntemde ~150
        Assert.True(shadow[575, 350].PackedValue > 225); // koltuk bölgesinin kendisi de gölge sayılmaz
        Assert.True(shadow[60, 200].PackedValue < shadow[400, 200].PackedValue - 15); // duvarın gerçek gölgesi korunur
        var min = 255;
        shadow.ProcessPixelRows(a => { for (var y = 0; y < a.Height; y++) foreach (var p in a.GetRowSpan(y)) min = Math.Min(min, p.PackedValue); });
        Assert.True(min >= 175, $"En koyu değer {min}"); // en fazla ~%30 karartma
        shadow.Dispose();
    }

    [Fact]
    public async Task OdaSahnesi_OlusturKoseKatmanVarsayilan()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var sceneId = await sender.Send(new CreateRoomSceneCommand("Test salonu", RoomType.LivingRoom, new MemoryStream(Image(800, 500)), 400m, 260m));
        var scene = (await sender.Send(new GetRoomScenesAdminQuery())).Single(s => s.Scene.Id == sceneId);
        Assert.Equal(800, scene.Scene.ImageWidthPx);
        Assert.Equal(8, scene.Scene.WallQuad.Length);

        await sender.Send(new UpdateRoomSceneCommand(sceneId, "Test salonu 2", RoomType.Office, 5, [100, 50, 700, 60, 690, 420, 110, 400], 380m, 250m));
        scene = (await sender.Send(new GetRoomScenesAdminQuery())).Single(s => s.Scene.Id == sceneId);
        Assert.Equal("Test salonu 2", scene.Scene.Name);
        Assert.Equal(700, scene.Scene.WallQuad[2]);
        Assert.Equal(380m, scene.Scene.RealWallWidthCm);
        var version = scene.Scene.Version;

        // Gölge/maske PNG olmalı; farklı boyuttaki PNG taban boyutuna ölçeklenir.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new UploadSceneLayerCommand(sceneId, SceneLayer.Mask, new MemoryStream(Image(800, 500)))));
        await sender.Send(new UploadSceneLayerCommand(sceneId, SceneLayer.Mask, new MemoryStream(Image(400, 250, png: true))));
        scene = (await sender.Send(new GetRoomScenesAdminQuery())).Single(s => s.Scene.Id == sceneId);
        Assert.NotNull(scene.Scene.ForegroundMaskUrl);
        Assert.True(scene.Scene.Version > version);
        var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();
        await using (var mask = store.OpenPublic(scene.Scene.ForegroundMaskUrl!)!)
        {
            var info = await SixLabors.ImageSharp.Image.IdentifyAsync(mask);
            Assert.Equal((800, 500), (info.Width, info.Height));
        }

        await sender.Send(new UploadSceneLayerCommand(sceneId, SceneLayer.Mask, null));
        Assert.Null((await sender.Send(new GetRoomScenesAdminQuery())).Single(s => s.Scene.Id == sceneId).Scene.ForegroundMaskUrl);

        await sender.Send(new SetDefaultSceneCommand(sceneId));
        var all = await sender.Send(new GetRoomScenesAdminQuery());
        Assert.Equal(sceneId, Assert.Single(all, s => s.Scene.IsDefault).Scene.Id);

        await sender.Send(new SetSceneActiveCommand(sceneId, false));
        Assert.DoesNotContain(await sender.Send(new GetRoomScenesQuery()), s => s.Id == sceneId);
    }

    [Fact]
    public async Task EmbedIstemcisi_OriginDogrulanir_AnahtarUretilir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new SaveEmbedClientCommand(null, "Mağaza", "https://magaza.com/yol", "", 100)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new SaveEmbedClientCommand(null, "Mağaza", "", "", 100)));

        var id = await sender.Send(new SaveEmbedClientCommand(null, "Mağaza", "https://magaza.com\nhttp://localhost:5000", "cdn.magaza.com", 100));
        var client = Assert.Single(await sender.Send(new GetEmbedClientsQuery()), c => c.Id == id);
        Assert.False(string.IsNullOrWhiteSpace(client.PublicKey));
        Assert.NotNull(await sender.Send(new GetEmbedClientQuery(client.PublicKey, "https://magaza.com")));
        Assert.True(await sender.Send(new IsEmbedOriginAllowedQuery("https://magaza.com")));
        Assert.Null(await sender.Send(new GetEmbedClientQuery(client.PublicKey, "https://baska.com")));

        await sender.Send(new SetEmbedClientActiveCommand(id, false));
        Assert.False(await sender.Send(new IsEmbedOriginAllowedQuery("https://magaza.com")));
    }

    [Fact]
    public async Task Ayarlar_KaydedilenDegerlerYuklenir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Dekorras.Application.Common.Interfaces.IUnitOfWork>();

        await sender.Send(new UpsertSettingCommand(WallCoveringSettingKeys.ChargeBleed, "false"));
        await sender.Send(new UpsertSettingCommand(WallCoveringSettingKeys.GlueFreeThresholdTry, "750"));
        await sender.Send(new UpsertSettingCommand(WallCoveringSettingKeys.ExtraHolidays, "2026-10-30, 2026-12-31"));
        await sender.Send(new UpsertSettingCommand(WallCoveringSettingKeys.FreeShippingThresholdTry, ""));

        var settings = WallCoveringSettings.Load(unitOfWork);
        Assert.False(settings.ChargeBleed);
        Assert.Equal(750m, settings.GlueFreeThresholdTry);
        Assert.Null(settings.FreeShippingThresholdTry); // boş = kapalı
        Assert.Contains(new DateOnly(2026, 12, 31), settings.ExtraHolidays);

        await sender.Send(new UpsertSettingCommand(WallCoveringSettingKeys.ChargeBleed, "true"));
    }

    [Fact]
    public async Task TopluYukleme_ZipVeCsv_SatirBazliRapor()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await ProductAsync(sender, "var-olan-slug");

        using var zipBuffer = new MemoryStream();
        using (var zip = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(content);
            }
            Add("klasor/orman.jpg", Image(4000, 2500));
            Add("desen.png", Image(1000, 1000, png: true));
            Add("bozuk.jpg", Encoding.UTF8.GetBytes("bu bir resim değil"));
            Add("cakisan.jpg", Image(500, 400));
            Add("desen-eksik.jpg", Image(500, 400));
            Add("__MACOSX/._orman.jpg", [1, 2, 3]);
        }
        var csv = """
            dosya;baslik;slug;etiketler;tur;tekrar_en;tekrar_boy;tekrar_tipi
            orman.jpg;Sisli Orman;;color:green|room:salon|olmayan:etiket;mural;;;
            desen.png;Çiçek Deseni;cicek-desen;;pattern;53,5;53;halfdrop
            bozuk.jpg;Bozuk;;;;;;
            cakisan.jpg;Çakışan;var-olan-slug;;;;;
            desen-eksik.jpg;Eksik Desen;;;pattern;;;
            yok.jpg;Yok;;;;;;
            """;

        var results = await sender.Send(new BulkPosterImportCommand(
            [new BulkUploadFile("paket.zip", zipBuffer.ToArray()), new BulkUploadFile("liste.csv", Encoding.UTF8.GetBytes("﻿" + csv))]));

        Assert.Equal(6, results.Count);
        var orman = results.Single(r => r.File == "orman.jpg");
        Assert.True(orman.Success, orman.Message);
        Assert.Equal("sisli-orman", orman.Slug);
        Assert.False(orman.LowResolution);
        Assert.Contains("olmayan:etiket", orman.Message);

        var desen = results.Single(r => r.File == "desen.png");
        Assert.True(desen.Success, desen.Message);
        Assert.True(desen.LowResolution);

        Assert.False(results.Single(r => r.File == "bozuk.jpg").Success);
        Assert.Contains("zaten", results.Single(r => r.File == "cakisan.jpg").Message);
        Assert.Contains("tekrar", results.Single(r => r.File == "desen-eksik.jpg").Message);
        Assert.Contains("yok", results.Single(r => r.File == "yok.jpg").Message);

        using var verify = _serviceProvider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ormanProfile = await db.WallpaperProfiles.SingleAsync(p => p.ProductId == orman.ProductId);
        Assert.Equal(WallProductType.Mural, ormanProfile.ProductType);
        Assert.Null(ormanProfile.DerivativesGeneratedAtUtc); // arka plan işçisi üretir
        var tagKeys = await db.ProductTags.Where(pt => pt.ProductId == orman.ProductId)
            .Join(db.Tags, pt => pt.TagId, t => t.Id, (pt, t) => t.Value).ToListAsync();
        Assert.Equal(["green", "salon"], tagKeys.Order().ToList());

        var desenProfile = await db.WallpaperProfiles.SingleAsync(p => p.ProductId == desen.ProductId);
        Assert.Equal(WallProductType.Pattern, desenProfile.ProductType);
        Assert.Equal(53.5m, desenProfile.RepeatWidthCm);
        Assert.Equal(RepeatType.HalfDrop, desenProfile.RepeatType);

        var product = await db.Products.Include(p => p.Images).SingleAsync(p => p.Id == orman.ProductId);
        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.Single(product.Images);

        // CSV'siz yükleme: başlık dosya adından üretilir.
        var plain = await sender.Send(new BulkPosterImportCommand([new BulkUploadFile("mavi_deniz-manzarasi.jpg", Image(600, 400))], Publish: false));
        var row = Assert.Single(plain);
        Assert.True(row.Success, row.Message);
        Assert.Equal("mavi-deniz-manzarasi", row.Slug);
    }
}
