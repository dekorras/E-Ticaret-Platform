using Dekorras.Application.Content.Commands;
using Dekorras.Application.Content.Queries;
using Dekorras.Domain.Content;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Content;

/// <summary>Gerçek SQL Server'a karşı çalışır - admin panelindeki BannerZoneBuilder'a eklenen
/// "Kopyala" butonunun (bkz. backend/README.md, kullanıcı ekran görüntüsüyle istedi) arkasındaki
/// `DuplicateBannerNodeCommand`'ın DOĞRU derin kopyaladığını (iç içe satır/kolon + içerikler),
/// orijinali BOZMADIĞINI ve kopyayı doğru sırada (orijinalin HEMEN yanına) yerleştirdiğini kanıtlar.</summary>
public sealed class BannerZoneDuplicationRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasBannerDuplicationTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SatiriKopyalaIcIceKolonlariIcerikleriVeKardesSirasiniDogruKlonlar()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var zone = new BannerZone("test-zone", "Test Bölgesi", null);
        dbContext.Set<BannerZone>().Add(zone);
        await dbContext.SaveChangesAsync();

        // rowA (kopyalanacak) ve rowB (rowA'dan SONRA gelen bir kardeş - kayma testinin ölçütü).
        var rowAId = await new AddBannerRowCommandHandler(unitOfWork).Handle(new AddBannerRowCommand(zone.Id, null, null), CancellationToken.None);
        var rowBId = await new AddBannerRowCommandHandler(unitOfWork).Handle(new AddBannerRowCommand(zone.Id, null, null), CancellationToken.None);

        await new SplitRowIntoColumnsCommandHandler(unitOfWork).Handle(new SplitRowIntoColumnsCommand(rowAId, [6, 6]), CancellationToken.None);
        var nodeRepository = unitOfWork.Repository<BannerNode>();
        var rowAColumns = nodeRepository.Query().Where(n => n.ParentId == rowAId).OrderBy(n => n.SortOrder).ToList();
        var col1Id = rowAColumns[0].Id;
        var col2Id = rowAColumns[1].Id;

        // col1'e bir içerik, col2'nin İÇİNE iç içe bir satır (nested row) ekleniyor - derin kopyalamanın
        // hem içerikleri HEM alt satırları kapsadığını kanıtlamak için.
        var content = new BannerContent(col1Id, BannerContentType.Heading, sortOrder: 0);
        content.UpdateText("Test Başlık", null, null, null);
        dbContext.Set<BannerContent>().Add(content);
        await dbContext.SaveChangesAsync();

        var nestedRowId = await new AddBannerRowCommandHandler(unitOfWork).Handle(new AddBannerRowCommand(zone.Id, col2Id, null), CancellationToken.None);

        // --- Kopyala ---
        var duplicatedRowId = await new DuplicateBannerNodeCommandHandler(unitOfWork).Handle(new DuplicateBannerNodeCommand(rowAId), CancellationToken.None);

        Assert.NotEqual(rowAId, duplicatedRowId);

        var tree = await new GetBannerZoneTreeQueryHandler(unitOfWork).Handle(new GetBannerZoneTreeQuery(zone.Key, IncludeInactive: true), CancellationToken.None);
        Assert.NotNull(tree);
        Assert.Equal(3, tree!.Roots.Count);

        // Sıra: rowA (0) → kopya (1) → rowB (2, ESKİDEN 1'di, kayması GEREKİYORDU).
        var orderedRoots = tree.Roots.OrderBy(r => r.SortOrder).ToList();
        Assert.Equal(rowAId, orderedRoots[0].Id);
        Assert.Equal(duplicatedRowId, orderedRoots[1].Id);
        Assert.Equal(rowBId, orderedRoots[2].Id);
        Assert.Equal(0, orderedRoots[0].SortOrder);
        Assert.Equal(1, orderedRoots[1].SortOrder);
        Assert.Equal(2, orderedRoots[2].SortOrder);

        var duplicatedRow = orderedRoots[1];
        Assert.Equal(2, duplicatedRow.Children.Count);
        var duplicatedCol1 = duplicatedRow.Children.OrderBy(c => c.SortOrder).First();
        var duplicatedCol2 = duplicatedRow.Children.OrderBy(c => c.SortOrder).Last();

        // Kolonların KENDİSİ farklı Id'lere sahip (gerçek yeni kayıtlar) ama SettingsJson'ları
        // (ör. col:6 genişliği) orijinalle AYNI olmalı.
        Assert.NotEqual(col1Id, duplicatedCol1.Id);
        Assert.NotEqual(col2Id, duplicatedCol2.Id);
        var originalCol1Settings = nodeRepository.Query().First(n => n.Id == col1Id).SettingsJson;
        Assert.Equal(originalCol1Settings, duplicatedCol1.SettingsJson);

        // İçerik klonlandı - farklı Id, AYNI Başlık, hâlâ col1 orijinal içeriği DE duruyor (bozulmadı).
        Assert.Single(duplicatedCol1.Contents);
        Assert.Equal("Test Başlık", duplicatedCol1.Contents.First().Title);
        Assert.NotEqual(content.Id, duplicatedCol1.Contents.First().Id);
        var originalContentStillExists = unitOfWork.Repository<BannerContent>().Query().Any(c => c.Id == content.Id);
        Assert.True(originalContentStillExists);

        // İç içe satır klonlandı - farklı Id, aynı yapı (1 çocuk satır).
        Assert.Single(duplicatedCol2.Children);
        Assert.NotEqual(nestedRowId, duplicatedCol2.Children.First().Id);

        // Orijinal rowA'nın KENDİ alt ağacı TAMAMEN bozulmadan kalmalı.
        var originalRowAfterDuplicate = tree.Roots.First(r => r.Id == rowAId);
        Assert.Equal(2, originalRowAfterDuplicate.Children.Count);
        var originalCol1AfterDuplicate = originalRowAfterDuplicate.Children.First(c => c.Id == col1Id);
        Assert.Single(originalCol1AfterDuplicate.Contents);
        Assert.Equal(content.Id, originalCol1AfterDuplicate.Contents.First().Id);
    }
}
