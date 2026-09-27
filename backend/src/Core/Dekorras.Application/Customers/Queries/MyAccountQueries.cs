using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

// "Hesabım" alanı (Siparişlerim, Sana Özel Fırsatlar, Soru ve Taleplerim, Değerlendirmelerim, Kuponlarım,
// Kullanıcı bilgilerim) sorguları. Hepsi Identity kullanıcısının KENDİ müşteri kaydıyla sınırlıdır.

internal static class MyAccount
{
    public static Customer? Customer(IUnitOfWork unitOfWork, string identityUserId) =>
        unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == identityUserId);

    /// <summary>Ürün kimliği → (ad, slug, küçük görsel). Duvar kağıdında profil küçük resmi tercih edilir.</summary>
    public static Dictionary<Guid, (string Name, string Slug, string? Image)> Products(IUnitOfWork unitOfWork, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return [];
        var thumbs = unitOfWork.Repository<WallpaperProfile>().Query()
            .Where(w => ids.Contains(w.ProductId) && w.ThumbUrl != null)
            .Select(w => new { w.ProductId, w.ThumbUrl })
            .ToList()
            .ToDictionary(x => x.ProductId, x => x.ThumbUrl);
        return unitOfWork.Repository<Product>().Query()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Slug,
                Name = p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault()
            })
            .ToList()
            .ToDictionary(p => p.Id, p => (p.Name, p.Slug, thumbs.GetValueOrDefault(p.Id) ?? p.Image));
    }
}

// ============================ Siparişlerim ============================

/// <summary>Siparişlerim üst filtreleri (Hepsiburada düzeni): Tümü / Devam edenler / İptaller / İadeler / Teslim edilemeyenler.</summary>
public enum MyOrderFilter { All, Ongoing, Cancelled, Returned, Undelivered }

public static class MyOrderStatusGroups
{
    public static readonly OrderStatus[] Ongoing = [OrderStatus.PendingApproval, OrderStatus.Preparing, OrderStatus.Prepared, OrderStatus.Shipped, OrderStatus.CancellationReverted, OrderStatus.OnHold];
    public static readonly OrderStatus[] Cancelled = [OrderStatus.Cancelled, OrderStatus.Voided];
    public static readonly OrderStatus[] Returned = [OrderStatus.Refunded, OrderStatus.Chargeback];
    // VARSAYIM: sistemde ayrı bir "teslim edilemedi" durumu yok; ödemesi reddedilen/süresi dolan (müşteriye hiç
    // ulaşmayan) siparişler "Teslim edilemeyenler" altında gösterilir.
    public static readonly OrderStatus[] Undelivered = [OrderStatus.Rejected, OrderStatus.Expired, OrderStatus.Failed];

    public static OrderStatus[]? For(MyOrderFilter filter) => filter switch
    {
        MyOrderFilter.Ongoing => Ongoing,
        MyOrderFilter.Cancelled => Cancelled,
        MyOrderFilter.Returned => Returned,
        MyOrderFilter.Undelivered => Undelivered,
        _ => null
    };
}

public sealed record MyOrderCardItemDto(string ProductName, string? Slug, string? ImageUrl, int Quantity, decimal LineTotalTry);

public sealed record MyOrderCardDto(Guid Id, string OrderNumber, DateTime CreatedAtUtc, decimal GrandTotalTry, OrderStatus Status,
    string? ShipmentTrackingNumber, IReadOnlyList<MyOrderCardItemDto> Items);

public sealed record MyOrdersPageDto(IReadOnlyList<MyOrderCardDto> Orders, IReadOnlyList<int> Years, int TotalOrderCount);

/// <param name="Period">null = tüm siparişler; "30" / "90" / "180" = son N gün; "2025" gibi yıl.</param>
public sealed record GetMyOrdersPageQuery(string IdentityUserId, string? Search = null, MyOrderFilter Filter = MyOrderFilter.All, string? Period = null)
    : IRequest<MyOrdersPageDto>;

public sealed class GetMyOrdersPageQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyOrdersPageQuery, MyOrdersPageDto>
{
    public Task<MyOrdersPageDto> Handle(GetMyOrdersPageQuery request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId);
        if (customer is null) return Task.FromResult(new MyOrdersPageDto([], [], 0));

        var mine = unitOfWork.Repository<Order>().Query().Where(o => o.CustomerId == customer.Id);
        var totalCount = mine.Count();
        var years = mine.Select(o => o.CreatedAtUtc.Year).Distinct().ToList().OrderByDescending(y => y).ToList();

        var query = mine;
        if (MyOrderStatusGroups.For(request.Filter) is { } statuses)
            query = query.Where(o => statuses.Contains(o.Status));

        if (int.TryParse(request.Period, out var period))
        {
            if (period is > 1900 and < 3000)
            {
                var from = new DateTime(period, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var to = from.AddYears(1);
                query = query.Where(o => o.CreatedAtUtc >= from && o.CreatedAtUtc < to);
            }
            else if (period > 0)
            {
                var since = DateTime.UtcNow.AddDays(-period);
                query = query.Where(o => o.CreatedAtUtc >= since);
            }
        }

        var items = unitOfWork.Repository<OrderItem>().Query();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Sipariş numarası (boşluklar yok sayılır) veya siparişteki ürün adıyla arama.
            var term = request.Search.Trim();
            var compact = term.Replace(" ", "");
            query = query.Where(o => o.OrderNumber.Contains(compact) || items.Any(i => i.OrderId == o.Id && i.ProductName.Contains(term)));
        }

        var orders = query.OrderByDescending(o => o.CreatedAtUtc).Take(200)
            .Select(o => new { o.Id, o.OrderNumber, o.CreatedAtUtc, o.GrandTotalTry, o.Status, o.ShipmentTrackingNumber })
            .ToList();
        var orderIds = orders.Select(o => o.Id).ToList();
        var lines = items.Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.ProductId, i.ProductName, i.Quantity, i.UnitPriceTry })
            .ToList();
        var products = MyAccount.Products(unitOfWork, lines.Select(l => l.ProductId).Distinct().ToList());

        var cards = orders.Select(o => new MyOrderCardDto(o.Id, o.OrderNumber, o.CreatedAtUtc, o.GrandTotalTry, o.Status, o.ShipmentTrackingNumber,
            lines.Where(l => l.OrderId == o.Id).Select(l =>
            {
                var p = products.GetValueOrDefault(l.ProductId);
                return new MyOrderCardItemDto(l.ProductName, p.Slug, p.Image, l.Quantity, l.UnitPriceTry * l.Quantity);
            }).ToList())).ToList();

        return Task.FromResult(new MyOrdersPageDto(cards, years, totalCount));
    }
}

// ============================ Soru ve Taleplerim ============================

public sealed record MyQuestionDto(Guid Id, string ProductName, string? ProductSlug, string Question, string? Answer, DateTime CreatedAtUtc, DateTime? AnsweredAtUtc);

public sealed record MyDesignRequestDto(Guid Id, string? ProductName, string? ProductSlug, DesignRequestType RequestType, DesignRequestStatus Status,
    string Message, DateTime CreatedAtUtc, DateTime DueAtUtc, DateTime? RespondedAtUtc);

public sealed record MyRequestsDto(IReadOnlyList<MyQuestionDto> Questions, IReadOnlyList<MyDesignRequestDto> DesignRequests);

/// <summary>Ürünlere sorduğu sorular (ve cevapları) + tasarım değişikliği talepleri (e-postasıyla eşleşen).</summary>
public sealed record GetMyRequestsQuery(string IdentityUserId) : IRequest<MyRequestsDto>;

public sealed class GetMyRequestsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyRequestsQuery, MyRequestsDto>
{
    public Task<MyRequestsDto> Handle(GetMyRequestsQuery request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId);
        if (customer is null) return Task.FromResult(new MyRequestsDto([], []));

        var questions = unitOfWork.Repository<ProductQuestion>().Query().Where(q => q.CustomerId == customer.Id)
            .OrderByDescending(q => q.CreatedAtUtc)
            .Select(q => new { q.Id, q.ProductId, q.Question, q.Answer, q.CreatedAtUtc, q.AnsweredAtUtc })
            .ToList();
        var email = customer.Email.Trim().ToLower();
        var requests = unitOfWork.Repository<DesignRequest>().Query().Where(r => r.Email.ToLower() == email)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new { r.Id, r.ProductId, r.RequestType, r.Status, r.Message, r.CreatedAtUtc, r.DueAtUtc, r.RespondedAtUtc })
            .ToList();

        var productIds = questions.Select(q => q.ProductId).Concat(requests.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value)).Distinct().ToList();
        var products = MyAccount.Products(unitOfWork, productIds);

        return Task.FromResult(new MyRequestsDto(
            questions.Select(q =>
            {
                var p = products.GetValueOrDefault(q.ProductId);
                return new MyQuestionDto(q.Id, p.Name ?? "Ürün", p.Slug, q.Question, q.Answer, q.CreatedAtUtc, q.AnsweredAtUtc);
            }).ToList(),
            requests.Select(r =>
            {
                var p = r.ProductId is Guid id ? products.GetValueOrDefault(id) : default;
                return new MyDesignRequestDto(r.Id, p.Name, p.Slug, r.RequestType, r.Status, r.Message, r.CreatedAtUtc, r.DueAtUtc, r.RespondedAtUtc);
            }).ToList()));
    }
}

// ============================ Değerlendirmelerim ============================

public sealed record MyReviewDto(Guid Id, string ProductName, string? ProductSlug, string? ImageUrl, int Rating, string Comment, bool IsApproved, DateTime CreatedAtUtc);

public sealed record MyPendingReviewDto(Guid ProductId, string ProductName, string ProductSlug, string? ImageUrl, DateTime OrderedAtUtc);

public sealed record MyReviewsDto(IReadOnlyList<MyReviewDto> Reviews, IReadOnlyList<MyPendingReviewDto> Pending);

/// <summary>Yaptığı değerlendirmeler + tamamlanmış siparişlerinde olup henüz değerlendirmediği ürünler.</summary>
public sealed record GetMyReviewsQuery(string IdentityUserId) : IRequest<MyReviewsDto>;

public sealed class GetMyReviewsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyReviewsQuery, MyReviewsDto>
{
    public Task<MyReviewsDto> Handle(GetMyReviewsQuery request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId);
        if (customer is null) return Task.FromResult(new MyReviewsDto([], []));

        var reviews = unitOfWork.Repository<ProductReview>().Query().Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new { r.Id, r.ProductId, r.Rating, r.Comment, r.IsApproved, r.CreatedAtUtc })
            .ToList();
        var reviewed = reviews.Select(r => r.ProductId).ToHashSet();

        var completedOrders = unitOfWork.Repository<Order>().Query().Where(o => o.CustomerId == customer.Id && o.Status == OrderStatus.Completed);
        var bought = unitOfWork.Repository<OrderItem>().Query()
            .Where(i => completedOrders.Any(o => o.Id == i.OrderId))
            .Select(i => new { i.ProductId, OrderedAt = completedOrders.Where(o => o.Id == i.OrderId).Select(o => o.CreatedAtUtc).First() })
            .ToList()
            .Where(x => !reviewed.Contains(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, OrderedAt = g.Max(x => x.OrderedAt) })
            .ToList();

        var products = MyAccount.Products(unitOfWork, reviews.Select(r => r.ProductId).Concat(bought.Select(b => b.ProductId)).Distinct().ToList());

        return Task.FromResult(new MyReviewsDto(
            reviews.Select(r =>
            {
                var p = products.GetValueOrDefault(r.ProductId);
                return new MyReviewDto(r.Id, p.Name ?? "Ürün", p.Slug, p.Image, r.Rating, r.Comment, r.IsApproved, r.CreatedAtUtc);
            }).ToList(),
            bought.Where(b => products.ContainsKey(b.ProductId))
                .OrderByDescending(b => b.OrderedAt)
                .Select(b => { var p = products[b.ProductId]; return new MyPendingReviewDto(b.ProductId, p.Name, p.Slug, p.Image, b.OrderedAt); })
                .ToList()));
    }
}

// ============================ Kuponlarım ============================

public sealed record MyCouponUsageDto(string Code, string OrderNumber, DateTime UsedAtUtc, decimal DiscountTry, string? DiscountText, bool StillValid, DateTime? ValidToUtc);

/// <summary>Siparişlerinde kullandığı kuponlar. VARSAYIM: kuponlar müşteriye atanmadığı (genel kod) için "kullanılabilir
/// kuponlar" listelenmez; tüm aktif kodları göstermek müşteriye özel kodları sızdırırdı.</summary>
public sealed record GetMyCouponsQuery(string IdentityUserId) : IRequest<IReadOnlyList<MyCouponUsageDto>>;

public sealed class GetMyCouponsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyCouponsQuery, IReadOnlyList<MyCouponUsageDto>>
{
    public Task<IReadOnlyList<MyCouponUsageDto>> Handle(GetMyCouponsQuery request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId);
        if (customer is null) return Task.FromResult<IReadOnlyList<MyCouponUsageDto>>([]);

        var used = unitOfWork.Repository<Order>().Query()
            .Where(o => o.CustomerId == customer.Id && o.CouponCode != null)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new { o.CouponCode, o.OrderNumber, o.CreatedAtUtc, o.DiscountTotalTry })
            .ToList();
        var codes = used.Select(u => u.CouponCode!).Distinct().ToList();
        var now = DateTime.UtcNow;
        var coupons = unitOfWork.Repository<Coupon>().Query().Where(c => codes.Contains(c.Code)).ToList().ToDictionary(c => c.Code);

        IReadOnlyList<MyCouponUsageDto> list = used.Select(u =>
        {
            var c = coupons.GetValueOrDefault(u.CouponCode!);
            var text = c is null ? null : c.DiscountType == DiscountType.Percentage ? $"%{c.DiscountValue:0.##} indirim" : $"{c.DiscountValue:N2} ₺ indirim";
            return new MyCouponUsageDto(u.CouponCode!, u.OrderNumber, u.CreatedAtUtc, u.DiscountTotalTry, text,
                c is not null && c.IsValidNow(now), c?.ValidToUtc);
        }).ToList();
        return Task.FromResult(list);
    }
}

// ============================ Sana Özel Fırsatlar ============================

public sealed record MyCampaignDto(string Name, DiscountType DiscountType, decimal DiscountValue, DateTime EndsAtUtc);

public sealed record MyGroupPriceDto(Guid ProductId, string ProductName, string ProductSlug, string? ImageUrl, decimal BasePriceTry, decimal GroupPriceTry);

public sealed record MyOffersDto(string? CustomerGroupName, IReadOnlyList<MyCampaignDto> Campaigns, IReadOnlyList<MyGroupPriceDto> GroupPrices);

/// <summary>Şu an geçerli kampanyalar + müşterinin grubuna (Bireysel/Kurumsal) tanımlı özel fiyatlı ürünler.</summary>
public sealed record GetMyOffersQuery(string IdentityUserId) : IRequest<MyOffersDto>;

public sealed class GetMyOffersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyOffersQuery, MyOffersDto>
{
    public Task<MyOffersDto> Handle(GetMyOffersQuery request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId);
        var now = DateTime.UtcNow;
        var campaigns = unitOfWork.Repository<Campaign>().Query()
            .Where(c => c.IsActive && c.StartsAtUtc <= now && c.EndsAtUtc >= now)
            .OrderBy(c => c.EndsAtUtc)
            .ToList()
            .Where(c => c.UsageLimit is null || c.UsageCount < c.UsageLimit)
            .Select(c => new MyCampaignDto(c.Name, c.DiscountType, c.DiscountValue, c.EndsAtUtc))
            .ToList();
        if (customer is null) return Task.FromResult(new MyOffersDto(null, campaigns, []));

        var groupName = unitOfWork.Repository<CustomerGroup>().Query().Where(g => g.Id == customer.CustomerGroupId).Select(g => g.Name).FirstOrDefault();
        var prices = unitOfWork.Repository<ProductGroupPrice>().Query()
            .Where(gp => gp.CustomerGroupId == customer.CustomerGroupId)
            .Join(unitOfWork.Repository<Product>().Query().Where(p => p.Status == ProductStatus.Active), gp => gp.ProductId, p => p.Id,
                (gp, p) => new { gp.ProductId, gp.PriceTry, p.BasePriceTry })
            .Where(x => x.PriceTry < x.BasePriceTry)
            .Take(48)
            .ToList();
        var products = MyAccount.Products(unitOfWork, prices.Select(p => p.ProductId).ToList());

        return Task.FromResult(new MyOffersDto(groupName, campaigns,
            prices.Where(p => products.ContainsKey(p.ProductId)).Select(p =>
            {
                var info = products[p.ProductId];
                return new MyGroupPriceDto(p.ProductId, info.Name, info.Slug, info.Image, p.BasePriceTry, p.PriceTry);
            }).ToList()));
    }
}

// ============================ Kullanıcı bilgilerim ============================

public sealed record UpdateMyContactCommand(string IdentityUserId, string FullName, string? PhoneNumber) : IRequest<Unit>;

public sealed class UpdateMyContactCommandValidator : AbstractValidator<UpdateMyContactCommand>
{
    public UpdateMyContactCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Ad soyad zorunludur.").MaximumLength(200);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^[0-9 +()\-]*$").WithMessage("Telefon yalnızca rakam, boşluk, +, - ve parantez içerebilir.");
    }
}

public sealed class UpdateMyContactCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateMyContactCommand, Unit>
{
    public async Task<Unit> Handle(UpdateMyContactCommand request, CancellationToken cancellationToken)
    {
        var customer = MyAccount.Customer(unitOfWork, request.IdentityUserId) ?? throw new KeyNotFoundException("Müşteri kaydı bulunamadı.");
        customer.UpdateContact(request.FullName.Trim(), string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
