using System.Reflection;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Common;
using Dekorras.Domain.Content;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Identity;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Localization;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Marketplace;
using Dekorras.Domain.Notifications;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using Dekorras.Domain.Shipping;
using Dekorras.Domain.SystemAdmin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Persistence;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
    }

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
    public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();

    // Ordering
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<GiftVoucher> GiftVouchers => Set<GiftVoucher>();

    // Customers
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();
    public DbSet<Affiliate> Affiliates => Set<Affiliate>();

    // Identity (Dekorras.Api JWT akışı)
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Integrations / Provider Registry
    public DbSet<IntegrationProvider> IntegrationProviders => Set<IntegrationProvider>();
    public DbSet<IntegrationHealthCheckLog> IntegrationHealthCheckLogs => Set<IntegrationHealthCheckLog>();

    // Payments
    public DbSet<Payment> Payments => Set<Payment>();

    // Shipping
    public DbSet<CustomsDeclaration> CustomsDeclarations => Set<CustomsDeclaration>();

    // Marketplace
    public DbSet<MarketplaceAccount> MarketplaceAccounts => Set<MarketplaceAccount>();
    public DbSet<MarketplaceListing> MarketplaceListings => Set<MarketplaceListing>();
    public DbSet<MarketplaceCategoryMapping> MarketplaceCategoryMappings => Set<MarketplaceCategoryMapping>();
    public DbSet<MarketplaceOrder> MarketplaceOrders => Set<MarketplaceOrder>();
    public DbSet<MarketplaceSyncLog> MarketplaceSyncLogs => Set<MarketplaceSyncLog>();

    // Accounting
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Waybill> Waybills => Set<Waybill>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<PromissoryNote> PromissoryNotes => Set<PromissoryNote>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<EInvoiceLog> EInvoiceLogs => Set<EInvoiceLog>();

    // Localization
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Content
    public DbSet<CmsPage> CmsPages => Set<CmsPage>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<BannerZone> BannerZones => Set<BannerZone>();
    public DbSet<BannerNode> BannerNodes => Set<BannerNode>();
    public DbSet<BannerContent> BannerContents => Set<BannerContent>();

    // Marketing
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();

    // System / RBAC
    public DbSet<AdminProfile> AdminProfiles => Set<AdminProfile>();
    public DbSet<Role> AppRoles => Set<Role>(); // "Roles" adı IdentityDbContext ile çakıştığı için farklı adlandırıldı
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Notifications
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // BaseEntity.DomainEvents ve IsTransient, EF Core'un otomatik ilişki/kolon keşfi
        // tarafından yanlışlıkla bir gezinme özelliği/sütun olarak algılanmasın diye tüm
        // entity'lerde yok sayılır (IsTransient salt bellek-içi bir bayraktır - bkz. BaseEntity).
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.GetProperty(nameof(BaseEntity.DomainEvents)) is not null)
                builder.Entity(entityType.ClrType).Ignore(nameof(BaseEntity.DomainEvents));
            if (entityType.ClrType.GetProperty(nameof(BaseEntity.IsTransient)) is not null)
                builder.Entity(entityType.ClrType).Ignore(nameof(BaseEntity.IsTransient));
        }

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    // Paylaşılan, DURUMSUZ TEK bir örnek olarak tutulur: OnConfiguring HER YENİ DbContext
    // örneğinde (yani normalde istek başına bir kez) çalışır - burada "new TransientTrackingInterceptor()"
    // ile HER SEFERİNDE FARKLI bir örnek eklenseydi, EF Core bunu "farklı bir yapılandırma" sanıp
    // her istek için yeni bir dahili service provider inşa eder, birkaç düzine istek sonra
    // "ManyServiceProvidersCreatedWarning" bir InvalidOperationException olarak fırlatılırdı
    // (canlı Storefront testinde art arda birkaç sayfa isteğinden sonra tam olarak bu oldu).
    private static readonly TransientTrackingInterceptor SharedTransientTrackingInterceptor = new();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // DbContext nasıl oluşturulursa oluşturulsun (DI ile AddPersistence, testlerde doğrudan
        // DbContextOptionsBuilder, migration araçları) her zaman devrede olsun diye burada
        // eklenir - bkz. TransientTrackingInterceptor ve BaseEntity.IsTransient dokümantasyonu.
        optionsBuilder.AddInterceptors(SharedTransientTrackingInterceptor);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // EF Core'un DOĞAL değişiklik algılamasını ZORLA çalıştırıp, ardından ALREADY TRACKED bir
        // aggregate'in koleksiyonuna YENİ eklenmiş (henüz hiç kaydedilmemiş) çocukları düzeltiriz.
        // Neden gerekli: BaseEntity.Id her zaman istemci tarafında (Guid.NewGuid()) üretilir; EF
        // Core, anahtarı "zaten atanmış" gördüğü YENİ bir entity'yi (ör. kategoriye ilk kez
        // eklenen bir çeviri, siparişe eklenen yeni bir durum geçmişi kaydı) sırf pasif gezinme
        // keşfiyle bulduğunda "Modified" sanıp var olmayan bir satırı UPDATE etmeye çalışır ve
        // DbUpdateConcurrencyException fırlatır. IsTransient bayrağı (TransientTrackingInterceptor
        // tarafından yalnızca veritabanından GERÇEKTEN materyalize edilen entity'lerde false'a
        // çekilir) burada güvenilir "gerçekten yeni mi?" cevabını verir.
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.Entity.IsTransient && entry.State is not EntityState.Added and not EntityState.Detached)
                entry.State = EntityState.Added;
        }

        var entitiesWithEvents = ChangeTracker.Entries<BaseEntity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count != 0)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            entry.Entity.MarkPersisted();

        if (_domainEventDispatcher is not null && entitiesWithEvents.Count != 0)
            await _domainEventDispatcher.DispatchAndClearEventsAsync(entitiesWithEvents, cancellationToken);

        return result;
    }
}
