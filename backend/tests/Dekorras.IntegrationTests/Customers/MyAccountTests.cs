using Dekorras.Application;
using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using Dekorras.Infrastructure;
using Dekorras.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.IntegrationTests.Customers;

/// <summary>"Hesabım" (Siparişlerim filtre/arama/dönem, Soru ve Taleplerim, Değerlendirmelerim, Kuponlarım,
/// Sana Özel Fırsatlar, Kullanıcı bilgilerim) sorguları gerçek SQL Server ile; başka müşterinin verisi görünmez.</summary>
public sealed class MyAccountTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasMyAccountTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";
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

    [Fact]
    public async Task HesabimSorgulari_FiltreAramaDonem_SoruTalepDegerlendirmeKuponFirsat()
    {
        using var scope = _serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var group = new CustomerGroup("Kurumsal");
        db.CustomerGroups.Add(group);
        await db.SaveChangesAsync();
        var me = new Customer("user-me", "Ayşe Yılmaz", "ayse@dekorras.test", group.Id);
        var other = new Customer("user-other", "Başka Biri", "baska@dekorras.test", group.Id);
        db.Customers.AddRange(me, other);
        await db.SaveChangesAsync();

        var category = await sender.Send(new CreateCategoryCommand("aksesuar", null, 0, "tr", "Aksesuar", null));
        async Task<Guid> Product(string slug, string name, decimal price)
        {
            var id = await sender.Send(new CreateProductCommand(slug, slug.ToUpperInvariant(), price, 20m, UnitOfMeasure.Piece, 1, StockQuantity: 100,
                BrandId: null, CategoryIds: [category], LanguageCode: "tr", Name: name, Description: null));
            await sender.Send(new SetProductPublishedCommand(id, Published: true));
            return id;
        }
        var lamba = await Product("masa-lambasi", "Masa Lambası", 500m);
        var vazo = await Product("seramik-vazo", "Seramik Vazo", 300m);

        Order NewOrder(Customer c, string no, Guid productId, string name, params OrderStatus[] path)
        {
            var o = new Order(no, c.Id, OrderSource.Web, Guid.NewGuid(), Guid.NewGuid());
            o.AddItem(productId, name, 100m, 1, 20m);
            foreach (var s in path) o.TransitionTo(s);
            return o;
        }
        var completed = NewOrder(me, "WEB-1001", lamba, "Masa Lambası", OrderStatus.Preparing, OrderStatus.Prepared, OrderStatus.Shipped, OrderStatus.Completed);
        completed.ApplyCoupon("HOSGELDIN", 10m);
        db.Orders.AddRange(
            completed,
            NewOrder(me, "WEB-1002", vazo, "Seramik Vazo", OrderStatus.Preparing),
            NewOrder(me, "WEB-1003", vazo, "Seramik Vazo", OrderStatus.Cancelled),
            NewOrder(me, "WEB-1004", lamba, "Masa Lambası", OrderStatus.Preparing, OrderStatus.Prepared, OrderStatus.Shipped, OrderStatus.Refunded),
            NewOrder(me, "WEB-1005", vazo, "Seramik Vazo", OrderStatus.Expired),
            NewOrder(other, "WEB-9999", lamba, "Masa Lambası", OrderStatus.Cancelled));
        db.Set<Coupon>().Add(new Coupon("HOSGELDIN", DiscountType.Percentage, 10m, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(5)));
        await db.SaveChangesAsync();

        // ---- Siparişlerim: filtreler, arama, dönem, sahiplik ----
        async Task<List<string>> Orders(MyOrderFilter f, string? q = null, string? period = null) =>
            (await sender.Send(new GetMyOrdersPageQuery("user-me", q, f, period))).Orders.Select(o => o.OrderNumber).OrderBy(x => x).ToList();

        Assert.Equal(["WEB-1001", "WEB-1002", "WEB-1003", "WEB-1004", "WEB-1005"], await Orders(MyOrderFilter.All)); // WEB-9999 başkasının
        Assert.Equal(["WEB-1002"], await Orders(MyOrderFilter.Ongoing));
        Assert.Equal(["WEB-1003"], await Orders(MyOrderFilter.Cancelled));
        Assert.Equal(["WEB-1004"], await Orders(MyOrderFilter.Returned));
        Assert.Equal(["WEB-1005"], await Orders(MyOrderFilter.Undelivered));
        Assert.Equal(["WEB-1002", "WEB-1003", "WEB-1005"], await Orders(MyOrderFilter.All, "vazo"));       // ürün adıyla
        Assert.Equal(["WEB-1004"], await Orders(MyOrderFilter.All, "1004"));                               // sipariş no ile
        Assert.Equal(5, (await Orders(MyOrderFilter.All, period: "30")).Count);
        Assert.Equal(5, (await Orders(MyOrderFilter.All, period: DateTime.UtcNow.Year.ToString())).Count);
        Assert.Empty(await Orders(MyOrderFilter.All, period: "2000"));
        var page = await sender.Send(new GetMyOrdersPageQuery("user-me"));
        Assert.Equal(5, page.TotalOrderCount);
        Assert.Contains(DateTime.UtcNow.Year, page.Years);
        var card = page.Orders.Single(o => o.OrderNumber == "WEB-1001");
        Assert.Equal("masa-lambasi", Assert.Single(card.Items).Slug);

        // ---- Soru ve Taleplerim ----
        var question = new ProductQuestion(lamba, me.Id, "Ampulü dahil mi?");
        question.SetAnswer("Evet, LED ampul dahildir.");
        db.Set<ProductQuestion>().AddRange(question, new ProductQuestion(vazo, other.Id, "Başkasının sorusu"));
        db.Set<DesignRequest>().Add(new DesignRequest("Ayşe Yılmaz", "AYSE@dekorras.test", null, DesignRequestType.OzelOlcu, "Özel ölçü istiyorum", DateTime.UtcNow.AddDays(2), null));
        await db.SaveChangesAsync();
        var requests = await sender.Send(new GetMyRequestsQuery("user-me"));
        var q1 = Assert.Single(requests.Questions);
        Assert.Equal("Masa Lambası", q1.ProductName);
        Assert.Equal("Evet, LED ampul dahildir.", q1.Answer);
        Assert.Single(requests.DesignRequests); // e-posta büyük/küçük harf duyarsız eşleşir

        // ---- Değerlendirmelerim: tamamlanan siparişteki ürün "bekleyen", değerlendirince listeye geçer ----
        var reviews = await sender.Send(new GetMyReviewsQuery("user-me"));
        Assert.Empty(reviews.Reviews);
        Assert.Equal(lamba, Assert.Single(reviews.Pending).ProductId); // iade/iptal edilenler değil, yalnızca tamamlanan
        db.Set<ProductReview>().Add(new ProductReview(lamba, me.Id, 5, "Çok güzel"));
        await db.SaveChangesAsync();
        reviews = await sender.Send(new GetMyReviewsQuery("user-me"));
        Assert.Equal(5, Assert.Single(reviews.Reviews).Rating);
        Assert.Empty(reviews.Pending);

        // ---- Kuponlarım ----
        var coupon = Assert.Single(await sender.Send(new GetMyCouponsQuery("user-me")));
        Assert.Equal("HOSGELDIN", coupon.Code);
        Assert.Equal("WEB-1001", coupon.OrderNumber);
        Assert.True(coupon.StillValid);
        Assert.Equal("%10 indirim", coupon.DiscountText);

        // ---- Sana Özel Fırsatlar: geçerli kampanya + grup fiyatı (yalnız taban fiyattan düşükse) ----
        db.Set<Campaign>().AddRange(
            new Campaign("Sonbahar İndirimi", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(7), DiscountType.Percentage, 15m),
            new Campaign("Bitmiş Kampanya", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-1), DiscountType.Percentage, 50m));
        db.Set<ProductGroupPrice>().AddRange(new ProductGroupPrice(lamba, group.Id, 420m), new ProductGroupPrice(vazo, group.Id, 350m));
        await db.SaveChangesAsync();
        var offers = await sender.Send(new GetMyOffersQuery("user-me"));
        Assert.Equal("Kurumsal", offers.CustomerGroupName);
        Assert.Equal("Sonbahar İndirimi", Assert.Single(offers.Campaigns).Name);
        var gp = Assert.Single(offers.GroupPrices);
        Assert.Equal((500m, 420m), (gp.BasePriceTry, gp.GroupPriceTry));

        // ---- Kullanıcı bilgilerim ----
        await sender.Send(new UpdateMyContactCommand("user-me", "  Ayşe Kaya ", "0555 111 22 33"));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => sender.Send(new UpdateMyContactCommand("user-me", "", null)));
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => sender.Send(new UpdateMyContactCommand("user-me", "Ad", "abc<script>")));
        db.ChangeTracker.Clear();
        var saved = await db.Customers.SingleAsync(c => c.IdentityUserId == "user-me");
        Assert.Equal(("Ayşe Kaya", "0555 111 22 33"), (saved.FullName, saved.PhoneNumber));

        // Bilinmeyen kullanıcı: boş sonuç, hata yok.
        Assert.Empty((await sender.Send(new GetMyOrdersPageQuery("yok"))).Orders);
    }
}
