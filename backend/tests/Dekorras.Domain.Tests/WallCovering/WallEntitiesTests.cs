using Dekorras.Domain.Common;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Domain.Tests.WallCovering;

public class WallEntitiesTests
{
    [Fact]
    public void DuvarimdaDeneListesi_EnFazla12Oge()
    {
        var list = new TryOnList("g:abc");
        for (var i = 0; i < TryOnList.MaxItems; i++) list.Add(Guid.NewGuid());

        Assert.Throws<DomainException>(() => list.Add(Guid.NewGuid()));
        Assert.Equal(TryOnList.MaxItems, list.Items.Count);
    }

    [Fact]
    public void DuvarimdaDeneListesi_AyniUrunIkinciKezEklenmez()
    {
        var list = new TryOnList("g:abc");
        var productId = Guid.NewGuid();
        list.Add(productId);
        list.Add(productId, "{\"materialCode\":\"plain\"}");

        Assert.Single(list.Items);
        Assert.NotNull(list.Items.Single().ConfigurationJson);
    }

    [Fact]
    public void MisafirListesi_UyeListesineBirlesir_KapasiteAsilmaz()
    {
        var member = new TryOnList("c:1");
        var shared = Guid.NewGuid();
        member.Add(shared);
        for (var i = 0; i < 9; i++) member.Add(Guid.NewGuid());

        var guest = new TryOnList("g:x");
        guest.Add(shared);
        for (var i = 0; i < 5; i++) guest.Add(Guid.NewGuid());

        var added = member.MergeFrom(guest);

        Assert.Equal(2, added);
        Assert.Equal(TryOnList.MaxItems, member.Items.Count);
    }

    [Fact]
    public void Siralama_VerilenSirayaGoreYenidenNumaralanir()
    {
        var list = new TryOnList("g:abc");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        list.Add(a);
        list.Add(b);
        list.Add(c);

        list.Reorder([c, a]);

        var ordered = list.Items.OrderBy(i => i.SortOrder).Select(i => i.ProductId).ToList();
        Assert.Equal([c, a, b], ordered);
    }

    [Fact]
    public void PaylasimAnahtari_BirKezUretilirSabitKalir()
    {
        var list = new TryOnList("g:abc");
        var token = list.EnsureShareToken();
        Assert.Equal(24, token.Length);
        Assert.Equal(token, list.EnsureShareToken());
    }

    [Fact]
    public void OnayOnizlemesi_YanitVerildiktenSonraTekrarYanitlanamaz()
    {
        var proof = new ProductionProof(Guid.NewGuid(), Guid.NewGuid(), null);
        proof.Approve();

        Assert.Equal(ProofStatus.Onaylandi, proof.Status);
        Assert.Throws<DomainException>(() => proof.RequestRevision("renk koyu"));
    }

    [Fact]
    public void OnayOnizlemesi_SureDoluncaOtomatikOnaylanir()
    {
        var now = DateTime.UtcNow;
        var proof = new ProductionProof(Guid.NewGuid(), Guid.NewGuid(), now.AddHours(24));

        Assert.False(proof.TryAutoApprove(now.AddHours(23)));
        Assert.True(proof.TryAutoApprove(now.AddHours(25)));
        Assert.True(proof.AutoApproved);
    }

    [Fact]
    public void RevizyonTalebi_AciklamaZorunlu()
    {
        var proof = new ProductionProof(Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.Throws<DomainException>(() => proof.RequestRevision(" "));
    }

    [Fact]
    public void EmbedIstemcisi_OriginKontroluVeGunlukKota()
    {
        var client = new EmbedClient("Mağaza", ["https://magaza.com/", "javascript:alert(1)"], ["CDN.Shopify.com"], 2);
        var today = new DateOnly(2026, 10, 1);

        Assert.True(client.IsOriginAllowed("https://magaza.com"));
        Assert.False(client.IsOriginAllowed("https://kotu.com"));
        Assert.Single(client.Origins);
        Assert.True(client.IsImageHostAllowed("cdn.shopify.com"));

        Assert.True(client.TryConsumeQuota(today));
        Assert.True(client.TryConsumeQuota(today));
        Assert.False(client.TryConsumeQuota(today));
        Assert.True(client.TryConsumeQuota(today.AddDays(1)));
    }

    [Fact]
    public void DesenProfili_TekrarOlcusuZorunlu()
    {
        var profile = new WallpaperProfile(Guid.NewGuid());
        Assert.Throws<DomainException>(() => profile.SetType(WallProductType.Pattern, null, 50m, RepeatType.HalfDrop));

        profile.SetType(WallProductType.Pattern, 53m, 64m, RepeatType.HalfDrop);
        Assert.Equal(53m, profile.RepeatWidthCm);
    }

    [Fact]
    public void Profil_EnBoyOraniGorseldenHesaplanir()
    {
        var profile = new WallpaperProfile(Guid.NewGuid());
        profile.SetOriginalImage("k", 4000, 2000, 150);
        Assert.Equal(2m, profile.AspectRatio);
    }
}
