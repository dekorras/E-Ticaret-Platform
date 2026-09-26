using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.SystemAdmin;
using Dekorras.Domain.WallCovering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Dekorras.IntegrationTests.WallCovering;

/// <summary>Gerçek SQL Server + gerçek ImageSharp işleme: türev görseller ve otomatik renk etiketi,
/// varsayılan sahneler, sunucu render'ı (maske posterin önünde, render önbelleği, geçersiz kılma) ve
/// kendi oda fotoğrafı (içerik doğrulama, sahiplik, misafir kısıtı, üyeye taşıma, kota).</summary>
public sealed class WallRenderingAndRoomTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWallRenderTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
    private ServiceProvider _serviceProvider = default!;
    private readonly List<string> _createdUrls = [];

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
    }

    public async Task DisposeAsync()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Testin paylaşılan /uploads klasörüne yazdığı dosyalar temizlenir.
            var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();
            var urls = await db.RoomScenes.Select(s => new[] { s.BaseImageUrl, s.ShadowMapUrl, s.ForegroundMaskUrl }).ToListAsync();
            var profileUrls = await db.WallpaperProfiles.Select(p => new[] { p.ThumbUrl, p.ListUrl, p.PreviewUrl, p.SceneThumbUrl }).ToListAsync();
            var imageUrls = await db.Set<ProductImage>().Select(i => i.Url).ToListAsync();
            foreach (var url in urls.SelectMany(u => u).Concat(profileUrls.SelectMany(u => u)).Concat(imageUrls).Concat(_createdUrls).Where(u => u is not null))
                await store.DeletePublicAsync(url!, CancellationToken.None);
            await db.Database.EnsureDeletedAsync();
        }
        await _serviceProvider.DisposeAsync();
    }

    private static byte[] Jpeg(int width, int height, Rgba32 color, bool stripes = false)
    {
        using var image = new Image<Rgba32>(width, height, color);
        if (stripes)
            image.Mutate(x => x.ProcessPixelRowsAsVector4((row, point) =>
            {
                if (point.Y % 40 < 20) for (var i = 0; i < row.Length; i++) row[i] = new System.Numerics.Vector4(0.1f, 0.4f, 0.1f, 1f);
            }));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static async Task<Guid> ProductWithImageAsync(ISender sender, ApplicationDbContext db)
    {
        await WallCoveringSeed.SeedAsync(db);
        var category = await sender.Send(new CreateCategoryCommand("posterler-141", null, 0, "tr", "Posterler", null));
        var productId = await sender.Send(new CreateProductCommand("yesil-orman", "GR-01", 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
            BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: "Yeşil Orman", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));
        await sender.Send(new AddProductImageCommand(productId, new MemoryStream(Jpeg(1200, 800, new Rgba32(46, 125, 50), stripes: true)), "orman.jpg", "image/jpeg"));
        await sender.Send(new EnsureWallpaperProfilesCommand());
        return productId;
    }

    [Fact]
    public async Task TurevlerSahnelerVeSunucuRenderi_MaskePosterinOnunde_OnbellekVeGecersizKilma()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();
        var productId = await ProductWithImageAsync(sender, db);

        Assert.True(await sender.Send(new GenerateProductDerivativesCommand(productId)));
        Assert.False(await sender.Send(new GenerateProductDerivativesCommand(productId))); // zaten üretildi

        db.ChangeTracker.Clear();
        var profile = await db.WallpaperProfiles.SingleAsync(p => p.ProductId == productId);
        Assert.Equal(1200, profile.ImageWidthPx);
        Assert.Equal(1.5m, profile.AspectRatio);
        Assert.NotNull(profile.LqipBase64);
        Assert.NotNull(store.OpenPrivate(profile.OriginalImageKey!)); // orijinal özel depoda
        Assert.StartsWith("/uploads/wall/products/", profile.ThumbUrl);
        using (var thumb = Image.Load(store.OpenPublic(profile.ThumbUrl!)!)) Assert.Equal(400, thumb.Width);
        using (var preview = Image.Load(store.OpenPublic(profile.PreviewUrl!)!)) Assert.Equal(1200, preview.Width); // küçük görsel büyütülmez
        var greenTag = await db.Tags.SingleAsync(t => t.Group == TagGroup.Color && t.Value == "green");
        Assert.True(await db.ProductTags.AnyAsync(pt => pt.ProductId == productId && pt.TagId == greenTag.Id)); // baskın renkten otomatik etiket

        Assert.Equal(3, await sender.Send(new EnsureDefaultRoomScenesCommand()));
        Assert.Equal(0, await sender.Send(new EnsureDefaultRoomScenesCommand()));
        var scene = await db.RoomScenes.SingleAsync(s => s.IsDefault);

        var config = new WallConfiguration(300m, 200m, "plain");
        var first = await sender.Send(new RenderWallPreviewQuery(scene.Id, productId, config, WallAlign.Center, 800, Watermark: false));
        var again = await sender.Send(new RenderWallPreviewQuery(scene.Id, productId, config, WallAlign.Center, 800, Watermark: false));
        Assert.Equal(first.Url, again.Url); // önbellek
        Assert.False(first.Clipped);

        using (var render = Image.Load<Rgba32>(store.OpenPublic(first.Url)!))
        {
            Assert.Equal(800, render.Width);
            // Duvar ortası (poster alanı, 800/1600 ölçek): yeşil çizgili poster görünmeli.
            var wall = render[400, 160];
            Assert.True(wall.G > wall.R + 30, $"Duvarda poster rengi bekleniyordu, gelen {wall}");
            // Kanepe yastığı (ön plan maskesi, #5e6b7a) posterin ÖNÜNDE: sahnede (600,620) hem poster alanında hem kanepede.
            var sofa = render[300, 310];
            Assert.InRange(sofa.B - sofa.R, 5, 40);
        }

        // Duvardan büyük ölçü → kırpıldı işareti.
        Assert.True((await sender.Send(new RenderWallPreviewQuery(scene.Id, productId, new WallConfiguration(600m, 200m, "plain"), WallAlign.Center, 640, false))).Clipped);

        // Sahne küçük resmi + ürün türevleri yeniden üretilince önbellek anahtarı değişir.
        Assert.NotNull(await sender.Send(new GenerateSceneThumbCommand(productId)));
        Assert.True(await sender.Send(new GenerateProductDerivativesCommand(productId, Force: true)));
        var afterRegen = await sender.Send(new RenderWallPreviewQuery(scene.Id, productId, config, WallAlign.Center, 800, Watermark: false));
        Assert.NotEqual(first.Url, afterRegen.Url);
    }

    [Fact]
    public async Task KendiOdaFotografi_DogrulamaSahiplikMisafirKisitiUyeyeTasimaVeKota()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var productId = await ProductWithImageAsync(sender, db);
        await sender.Send(new GenerateProductDerivativesCommand(productId));

        var guest = WallOwner.ForGuest("odamisafir1");
        double[] corners = [100, 80, 900, 60, 920, 520, 90, 540];

        // Görsel olmayan dosya içerikten reddedilir (uzantı/MIME'ye güvenilmez).
        await Assert.ThrowsAsync<WallConfigurationException>(() => sender.Send(new CreateUserRoomSceneCommand(
            guest, new MemoryStream("MZ bu bir exe"u8.ToArray()), corners, 1000, 600, 400m, null, "x")));
        // Dışbükey olmayan köşeler reddedilir.
        await Assert.ThrowsAsync<WallConfigurationException>(() => sender.Send(new CreateUserRoomSceneCommand(
            guest, new MemoryStream(Jpeg(1000, 600, new Rgba32(220, 220, 215))), [100, 80, 920, 520, 900, 60, 90, 540], 1000, 600, 400m, null, "x")));

        var room = await sender.Send(new CreateUserRoomSceneCommand(guest, new MemoryStream(Jpeg(1000, 600, new Rgba32(220, 220, 215))), corners, 1000, 600, 400m, null, "Salonum"));
        Assert.True(room.IsUserScene);
        Assert.NotNull(room.ShadowMapUrl);
        Assert.InRange(room.RealWallHeightCm, 200m, 260m); // dörtgen oranından tahmin: ~(460/810)×400

        Assert.Contains(await sender.Send(new GetRoomScenesQuery(guest)), s => s.Id == room.Id);
        Assert.DoesNotContain(await sender.Send(new GetRoomScenesQuery(WallOwner.ForGuest("baskasi"))), s => s.Id == room.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(200m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: WallOwner.ForGuest("baskasi"))));

        // Misafir: yüksek kaliteli render yok, giriş istenir.
        var guestEx = await Assert.ThrowsAsync<RoomPreviewQuotaException>(() => sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(200m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: guest)));
        Assert.True(guestEx.RequiresLogin);

        // Girişte oda üyeye taşınır.
        var member = WallOwner.ForCustomer(Guid.NewGuid());
        Assert.Equal(1, await sender.Send(new MergeUserRoomScenesCommand(guest, member)));
        Assert.Contains(await sender.Send(new GetRoomScenesQuery(member)), s => s.Id == room.Id);

        db.Settings.Add(new Setting(WallCoveringSettingKeys.RoomPreviewFreeQuota, "2"));
        await db.SaveChangesAsync();

        await sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(200m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: member));
        await sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(200m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: member)); // aynı sonuç: ücretsiz
        await sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(250m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: member));
        var quota = await sender.Send(new GetRoomQuotaQuery(member));
        Assert.Equal(2, quota.Used);
        Assert.Equal(0, quota.Remaining);
        var exceeded = await Assert.ThrowsAsync<RoomPreviewQuotaException>(() => sender.Send(new RenderWallPreviewQuery(room.Id, productId, new WallConfiguration(300m, 150m, "plain"), WallAlign.Center, 800, OwnerKey: member)));
        Assert.False(exceeded.RequiresLogin);

        // Fırça maskesi: PNG olmayan reddedilir, PNG kabul edilir ve sahneye bağlanır.
        await Assert.ThrowsAsync<WallConfigurationException>(() => sender.Send(new SetUserRoomMaskCommand(member, room.Id, new MemoryStream(Jpeg(10, 10, new Rgba32(0, 0, 0))))));
        using var maskImage = new Image<Rgba32>(500, 300);
        using var maskStream = new MemoryStream();
        await maskImage.SaveAsPngAsync(maskStream);
        maskStream.Position = 0;
        var withMask = await sender.Send(new SetUserRoomMaskCommand(member, room.Id, maskStream));
        Assert.NotNull(withMask.ForegroundMaskUrl);
        Assert.True(withMask.Version > room.Version);

        await sender.Send(new DeleteUserRoomSceneCommand(member, room.Id));
        Assert.DoesNotContain(await sender.Send(new GetRoomScenesQuery(member)), s => s.Id == room.Id);
    }
}
