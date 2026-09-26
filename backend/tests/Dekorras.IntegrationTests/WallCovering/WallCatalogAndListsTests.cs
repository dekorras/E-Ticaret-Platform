using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Customers.Commands;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Common;
using Dekorras.Domain.WallCovering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.WallCovering;

/// <summary>Gerçek SQL Server'a karşı: poster kataloğu filtre/sıralama/sayfalama, renk yakınlığı,
/// "Duvarımda Dene" listesi (12 sınırı, sıralama, paylaşım, misafir → üye birleştirme) ve favori birleştirme.</summary>
public sealed class WallCatalogAndListsTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWallCatalogTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
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
    }

    public async Task DisposeAsync()
    {
        using (var scope = _serviceProvider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        await _serviceProvider.DisposeAsync();
    }

    private static async Task<List<Guid>> SeedPostersAsync(ISender sender, ApplicationDbContext db, int count)
    {
        await WallCoveringSeed.SeedAsync(db);
        var category = await sender.Send(new CreateCategoryCommand("posterler-141", null, 0, "tr", "Posterler", null));
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var id = await sender.Send(new CreateProductCommand($"poster-{i:00}", $"P-{i:00}", 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
                BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: i % 2 == 0 ? $"Orman Manzarası {i}" : $"Deniz Feneri {i}", Description: null));
            await sender.Send(new SetProductPublishedCommand(id, Published: true));
            ids.Add(id);
        }
        await sender.Send(new EnsureWallpaperProfilesCommand());
        return ids;
    }

    [Fact]
    public async Task Katalog_FiltreSiralamaSayfalamaVeRenkYakinligi()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ids = await SeedPostersAsync(sender, db, 30);

        // Görsel bilgisi: ilk 10 yatay yeşil, 10 dikey mavi, kalanı işlenmemiş (oran 0).
        var profiles = await db.WallpaperProfiles.ToListAsync();
        for (var i = 0; i < 20; i++)
        {
            var p = profiles.Single(x => x.ProductId == ids[i]);
            if (i < 10) { p.SetOriginalImage("k", 3000, 2000, 150); p.SetDerivatives("/t.jpg", "/l.jpg", "/p.jpg", "lqip", "#2f7d32,#a5d6a7"); }
            else { p.SetOriginalImage("k", 2000, 3000, 150); p.SetDerivatives("/t.jpg", "/l.jpg", "/p.jpg", "lqip", "#1e3a8a,#93c5fd"); }
        }
        profiles.Single(x => x.ProductId == ids[3]).SetType(WallProductType.Pattern, 53m, 64m, RepeatType.HalfDrop);

        var green = await db.Tags.SingleAsync(t => t.Group == TagGroup.Color && t.Value == "green");
        var salon = await db.Tags.SingleAsync(t => t.Group == TagGroup.Room && t.Value == "salon");
        var bedroom = await db.Tags.SingleAsync(t => t.Group == TagGroup.Room && t.Value == "yatak-odasi");
        for (var i = 0; i < 10; i++) db.ProductTags.Add(new ProductTag(ids[i], green.Id));
        db.ProductTags.Add(new ProductTag(ids[0], salon.Id));
        db.ProductTags.Add(new ProductTag(ids[1], bedroom.Id));
        db.ProductTags.Add(new ProductTag(ids[12], salon.Id));
        await db.SaveChangesAsync();

        var all = await sender.Send(new GetWallCatalogQuery(PageSize: 24));
        Assert.Equal(30, all.TotalCount);
        Assert.Equal(2, all.TotalPages);
        Assert.Equal(24, all.Items.Count);
        Assert.Equal(699m, all.Items[0].FromPricePerM2);
        var page2 = await sender.Send(new GetWallCatalogQuery(Page: 2, PageSize: 24));
        Assert.Equal(6, page2.Items.Count);
        Assert.Empty(all.Items.Select(i => i.ProductId).Intersect(page2.Items.Select(i => i.ProductId)));

        Assert.Equal(10, (await sender.Send(new GetWallCatalogQuery(Orientation: "yatay"))).TotalCount);
        Assert.Equal(10, (await sender.Send(new GetWallCatalogQuery(Orientation: "dikey"))).TotalCount);
        Assert.Equal(1, (await sender.Send(new GetWallCatalogQuery(Type: "pattern"))).TotalCount);
        Assert.Equal(15, (await sender.Send(new GetWallCatalogQuery(Search: "Orman"))).TotalCount);

        // Grup içinde VEYA (salon|yatak odası = 3), gruplar arasında VE (+ yeşil = 2).
        Assert.Equal(3, (await sender.Send(new GetWallCatalogQuery(Tags: ["room:salon", "room:yatak-odasi"]))).TotalCount);
        var greenRooms = await sender.Send(new GetWallCatalogQuery(Tags: ["color:green,room:salon,room:yatak-odasi"]));
        Assert.Equal(2, greenRooms.TotalCount);
        Assert.Contains(greenRooms.TagGroups, g => g.Group == "color" && g.Tags.Any(t => t.Key == "color:green" && t.Selected && t.Count == 10));

        // Renk yakınlığı: #2e7d32'ye yakın yeşiller gelir, maviler gelmez.
        var byColor = await sender.Send(new GetWallCatalogQuery(Color: "#2e7d32"));
        Assert.Equal(10, byColor.TotalCount);
        Assert.All(byColor.Items, i => Assert.Contains(i.ProductId, ids.Take(10)));

        // "Yeni" sıralaması en son oluşturulanı başa alır.
        Assert.Equal(ids[^1], (await sender.Send(new GetWallCatalogQuery(Sort: "yeni"))).Items[0].ProductId);

        var detail = await sender.Send(new GetWallProductDetailQuery("poster-03"));
        Assert.NotNull(detail);
        Assert.Equal("Pattern", detail!.ProductType);
        Assert.Equal(53m, detail.RepeatWidthCm);
        Assert.Equal("/p.jpg", detail.PreviewUrl);
        Assert.Null(await sender.Send(new GetWallProductDetailQuery("olmayan-urun")));
    }

    [Fact]
    public async Task DuvarimdaDene_SinirSiralamaPaylasimVeMisafirdenUyeyeBirlestirme()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ids = await SeedPostersAsync(sender, db, 14);

        var guest = WallOwner.ForGuest("misafir123");
        for (var i = 0; i < 12; i++) await sender.Send(new AddTryOnItemCommand(guest, ids[i]));
        await Assert.ThrowsAsync<DomainException>(() => sender.Send(new AddTryOnItemCommand(guest, ids[12])));
        await sender.Send(new AddTryOnItemCommand(guest, ids[0], new WallConfiguration(300m, 250m, "plain").ToJson())); // yineleme yok

        var list = await sender.Send(new GetTryOnListQuery(guest));
        Assert.Equal(12, list.Items.Count);
        Assert.NotNull(list.Items.Single(i => i.ProductId == ids[0]).ConfigurationJson);

        await sender.Send(new ReorderTryOnListCommand(guest, [ids[5], ids[0]]));
        list = await sender.Send(new GetTryOnListQuery(guest));
        Assert.Equal(ids[5], list.Items[0].ProductId);
        Assert.Equal(ids[0], list.Items[1].ProductId);

        await sender.Send(new RemoveTryOnItemCommand(guest, ids[5]));
        var token = await sender.Send(new ShareTryOnListCommand(guest));
        var shared = await sender.Send(new GetSharedTryOnListQuery(token));
        Assert.Equal(11, shared!.Items.Count);
        Assert.Null(await sender.Send(new GetSharedTryOnListQuery("yok")));

        // Üyenin kendi listesinde 3 öğe (biri misafirle ortak) → birleşince 12'yi aşmaz, ortak yinelenmez.
        var member = WallOwner.ForCustomer(Guid.NewGuid());
        await sender.Send(new AddTryOnItemCommand(member, ids[0]));
        await sender.Send(new AddTryOnItemCommand(member, ids[12]));
        await sender.Send(new AddTryOnItemCommand(member, ids[13]));
        var added = await sender.Send(new MergeTryOnListCommand(guest, member));

        Assert.Equal(9, added);
        var merged = await sender.Send(new GetTryOnListQuery(member));
        Assert.Equal(12, merged.Items.Count);
        Assert.Equal(ids[0], merged.Items[0].ProductId); // üyenin öğeleri önde
        Assert.Empty((await sender.Send(new GetTryOnListQuery(guest))).Items);

        // Üyenin listesi yoksa misafir listesi olduğu gibi devredilir.
        var guest2 = WallOwner.ForGuest("misafir456");
        await sender.Send(new AddTryOnItemCommand(guest2, ids[1]));
        var member2 = WallOwner.ForCustomer(Guid.NewGuid());
        Assert.Equal(1, await sender.Send(new MergeTryOnListCommand(guest2, member2)));
        Assert.Single((await sender.Send(new GetTryOnListQuery(member2))).Items);

        // Konfigüre edilemeyen ürün eklenemez.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sender.Send(new AddTryOnItemCommand(member2, Guid.NewGuid())));
    }

    [Fact]
    public async Task MisafirFavorileri_UyeWishlistineBirlesir()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ids = await SeedPostersAsync(sender, db, 3);

        db.CustomerGroups.Add(new Dekorras.Domain.Customers.CustomerGroup("Bireysel"));
        await db.SaveChangesAsync();
        await sender.Send(new CreateCustomerProfileCommand("user-1", "Ayşe", "ayse@x.com"));
        await sender.Send(new AddToWishlistCommand("user-1", ids[0]));

        var added = await sender.Send(new MergeGuestFavoritesCommand("user-1", [ids[0], ids[1], Guid.NewGuid()]));

        Assert.Equal(1, added); // ids[0] zaten vardı, rastgele kimlik yok sayıldı
        var favorites = await sender.Send(new GetFavoriteProductIdsQuery("user-1"));
        Assert.Equal(2, favorites.Count);
    }
}
