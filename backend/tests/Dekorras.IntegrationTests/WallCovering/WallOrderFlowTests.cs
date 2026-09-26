using System.Text;
using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Ordering.Commands;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Ordering;
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

/// <summary>Gerçek SQL Server + gerçek görsel/PDF üretimi: sipariş sonrası onay önizlemesi, üretim kilidi,
/// müşteri onayı/revizyon/otomatik onay, panellere bölünmüş PDF ve tasarım değişiklik talebi.</summary>
public sealed class WallOrderFlowTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasWallOrderTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
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
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();
            foreach (var url in await db.ProductionProofs.Where(p => p.PreviewUrl != null).Select(p => p.PreviewUrl!).ToListAsync())
                await store.DeletePublicAsync(url, CancellationToken.None);
            foreach (var url in await db.Set<ProductImage>().Select(i => i.Url).ToListAsync())
                await store.DeletePublicAsync(url, CancellationToken.None);
            foreach (var key in await db.ProductionFiles.Where(f => f.FileKey != null).Select(f => f.FileKey!).ToListAsync())
                await store.DeletePrivateAsync(key, CancellationToken.None);
            foreach (var key in await db.Set<DesignRequestAttachment>().Select(a => a.FileKey).ToListAsync())
                await store.DeletePrivateAsync(key, CancellationToken.None);
            foreach (var key in await db.WallpaperProfiles.Where(p => p.OriginalImageKey != null).Select(p => p.OriginalImageKey!).ToListAsync())
                await store.DeletePrivateAsync(key, CancellationToken.None);
            await db.Database.EnsureDeletedAsync();
        }
        await _serviceProvider.DisposeAsync();
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var image = new Image<Rgba32>(w, h, new Rgba32(180, 90, 60));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static async Task<(Guid OrderId, Guid ProductId)> PlaceWallpaperOrderAsync(ISender sender, ApplicationDbContext db, WallConfiguration config)
    {
        db.CustomerGroups.Add(new CustomerGroup("Bireysel"));
        await db.SaveChangesAsync();
        await WallCoveringSeed.SeedAsync(db);
        var category = await sender.Send(new CreateCategoryCommand("posterler-141", null, 0, "tr", "Posterler", null));
        var productId = await sender.Send(new CreateProductCommand("gunbatimi", "GB-01", 1m, 20m, UnitOfMeasure.SquareMeter, 1, StockQuantity: 0,
            BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: "Gün Batımı", Description: null));
        await sender.Send(new SetProductPublishedCommand(productId, Published: true));
        await sender.Send(new AddProductImageCommand(productId, new MemoryStream(Jpeg(1600, 900)), "gb.jpg", "image/jpeg"));
        await sender.Send(new EnsureWallpaperProfilesCommand());
        await sender.Send(new ConfigureIntegrationProviderCommand("bank-transfer", ProviderCategory.Payment,
            new Dictionary<string, string> { ["BankName"] = "B", ["Iban"] = "TR000000000000000000000000", ["AccountHolder"] = "D" }, null));
        await sender.Send(new ConfigureIntegrationProviderCommand("yurtici-kargo", ProviderCategory.Cargo,
            new Dictionary<string, string> { ["AccountNumber"] = "1", ["ApiKey"] = "k" }, null));

        var session = Guid.NewGuid().ToString("N");
        await sender.Send(new AddConfiguredCartItemCommand(session, productId, config, 1));
        var result = await sender.Send(new PlaceOrderCommand(session, null, "Zeynep", "zeynep@dekorras.com", "5550000000", "TR", "Kayseri", "Adres", "bank-transfer", "yurtici-kargo"));
        return (result.OrderId, productId);
    }

    [Fact]
    public async Task Siparis_OnaySonrasiUretimeGecer_PdfPanellereBolunur_RevizyonVeOtomatikOnay()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();

        // 250 cm + 5 cm pay = 255 cm → 100 cm panellerle 3 panel.
        var (orderId, _) = await PlaceWallpaperOrderAsync(sender, db, new WallConfiguration(250m, 200m, "plain", mirror: true, filter: ImageFilter.Sepia));

        var proof = await db.ProductionProofs.SingleAsync(p => p.OrderId == orderId);
        Assert.Equal(ProofStatus.Beklemede, proof.Status);
        Assert.NotNull(proof.AutoApproveAtUtc); // varsayılan 24 saat
        Assert.Single(await db.ProductionFiles.Where(f => f.OrderId == orderId).ToListAsync());

        // Onay yokken üretime (Hazırlanıyor) geçilemez.
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new TransitionOrderStatusCommand(orderId, OrderStatus.Preparing, null)));
        Assert.Contains("onay", blocked.Message);

        // Önizleme arka planda üretilir.
        Assert.Equal(1, await sender.Send(new GenerateProofPreviewsCommand()));
        Assert.Equal(0, await sender.Send(new GenerateProofPreviewsCommand()));
        db.ChangeTracker.Clear();
        proof = await db.ProductionProofs.SingleAsync(p => p.OrderId == orderId);
        Assert.NotNull(proof.PreviewUrl);
        using (var preview = Image.Load(store.OpenPublic(proof.PreviewUrl!)!)) Assert.Equal(1600, preview.Width);

        // Üretim dosyası: 3 sayfalık geçerli PDF, özel depoda.
        Assert.Equal(1, await sender.Send(new GenerateProductionFilesCommand()));
        db.ChangeTracker.Clear();
        var file = await db.ProductionFiles.SingleAsync(f => f.OrderId == orderId);
        Assert.Equal(ProductionFileStatus.Ready, file.Status);
        Assert.Equal(3, file.PanelCount);
        await using (var pdf = store.OpenPrivate(file.FileKey!)!)
        {
            using var ms = new MemoryStream();
            await pdf.CopyToAsync(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());
            Assert.StartsWith("%PDF-1.4", text);
            Assert.EndsWith("%%EOF\n", text);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page /").Count);
            Assert.Contains("/Count 3", text);
        }
        var download = await sender.Send(new OpenProductionFileQuery(file.Id));
        Assert.NotNull(download);
        await download!.Content.DisposeAsync();

        // Müşteri revizyon ister (açıklama zorunlu) → admin yeniden gönderir → önizleme yeniden üretilir → onay.
        await Assert.ThrowsAsync<Dekorras.Domain.Common.DomainException>(() => sender.Send(new RespondToProofCommand(proof.Token, false, " ")));
        var revised = await sender.Send(new RespondToProofCommand(proof.Token, false, "Görseli biraz sola kaydırın"));
        Assert.Equal(ProofStatus.RevizyonIstendi, revised.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new TransitionOrderStatusCommand(orderId, OrderStatus.Preparing, null)));

        await sender.Send(new ReissueProofCommand(proof.Id));
        Assert.Equal(1, await sender.Send(new GenerateProofPreviewsCommand()));
        var approved = await sender.Send(new RespondToProofCommand(proof.Token, true, null));
        Assert.Equal(ProofStatus.Onaylandi, approved.Status);

        await sender.Send(new TransitionOrderStatusCommand(orderId, OrderStatus.Preparing, null));
        db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Preparing, (await db.Orders.SingleAsync(o => o.Id == orderId)).Status);

        var wallItems = await sender.Send(new GetOrderWallItemsQuery(orderId));
        var item = Assert.Single(wallItems);
        Assert.Equal(ProofStatus.Onaylandi, item.ProofStatus);
        Assert.Contains("250×200 cm", item.ConfigurationSummary);
        Assert.Contains("Ayna", item.ConfigurationSummary);
    }

    [Fact]
    public async Task OtomatikOnay_SuresiDolanOnizlemeyiOnaylar()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (orderId, _) = await PlaceWallpaperOrderAsync(sender, db, new WallConfiguration(120m, 100m, "self-adhesive"));
        await sender.Send(new GenerateProofPreviewsCommand());

        Assert.Equal(0, await sender.Send(new AutoApproveProofsCommand())); // süre dolmadı
        await db.Database.ExecuteSqlRawAsync("UPDATE ProductionProofs SET AutoApproveAtUtc = DATEADD(hour, -1, SYSUTCDATETIME())");
        db.ChangeTracker.Clear(); // ham SQL izleyiciyi atlar; gerçek işçi her turda yeni kapsam açar
        Assert.Equal(1, await sender.Send(new AutoApproveProofsCommand()));

        db.ChangeTracker.Clear();
        var proof = await db.ProductionProofs.SingleAsync(p => p.OrderId == orderId);
        Assert.Equal(ProofStatus.Onaylandi, proof.Status);
        Assert.True(proof.AutoApproved);
    }

    [Fact]
    public async Task TasarimTalebi_DosyaDogrulama_SlaVeAdminDurumu()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var store = scope.ServiceProvider.GetRequiredService<IWallImageStore>();

        await Assert.ThrowsAsync<WallConfigurationException>(() => sender.Send(new CreateDesignRequestCommand("Ali", "ali@x.com", null, DesignRequestType.RenkDegisikligi,
            "Mavi tonlar olsun", null, [new DesignRequestFile(new MemoryStream("not-an-image"u8.ToArray()), "virus.png", 12)])));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => sender.Send(new CreateDesignRequestCommand("", "gecersiz", null, DesignRequestType.Diger, "", null, [])));

        var id = await sender.Send(new CreateDesignRequestCommand("Ali Veli", "ali@x.com", null, DesignRequestType.OzelOlcu, "Kapı boşluğu bırakın",
            new WallConfiguration(300m, 250m, "plain").ToJson(), [new DesignRequestFile(new MemoryStream(Jpeg(800, 600)), "duvarim.jpg", 1000)]));

        var detail = await sender.Send(new GetDesignRequestQuery(id));
        Assert.NotNull(detail);
        Assert.Equal(DesignRequestStatus.New, detail!.Status);
        var attachment = Assert.Single(detail.Attachments);
        Assert.Equal("duvarim.jpg", attachment.FileName);
        var opened = await sender.Send(new OpenDesignRequestAttachmentQuery(attachment.Id));
        Assert.NotNull(opened);
        await opened!.Content.DisposeAsync();
        // SLA: 2 iş günü (hafta sonu/tatil hariç) - en az 2 takvim günü sonra.
        Assert.True(detail.DueAtUtc >= detail.CreatedAtUtc.AddDays(2).AddMinutes(-1));

        await sender.Send(new UpdateDesignRequestStatusCommand(id, DesignRequestStatus.Answered, "Müşteriye örnek gönderildi"));
        var list = await sender.Send(new GetDesignRequestsQuery(DesignRequestStatus.Answered));
        Assert.Single(list);
        Assert.False(list[0].IsOverdue);
        Assert.Equal(1, list[0].AttachmentCount);
    }
}
