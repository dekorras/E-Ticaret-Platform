using Dekorras.Application.Common.Interfaces;
using Dekorras.Infrastructure.CargoProviders;
using Dekorras.Infrastructure.EInvoiceProviders;
using Dekorras.Infrastructure.ExchangeRates;
using Dekorras.Infrastructure.MarketplaceConnectors;
using Dekorras.Infrastructure.Notifications;
using Dekorras.Infrastructure.PaymentProviders;
using Dekorras.Infrastructure.Security;
using Dekorras.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProviderRegistryImpl = Dekorras.Infrastructure.ProviderRegistry.ProviderRegistry;

namespace Dekorras.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Provider Registry - TÜM connector'lar burada kayıt edilir, hiçbiri varsayılan aktif değildir (bkz. §4.1)
        services.AddSingleton<IPaymentGateway, IyzicoPaymentGateway>();
        services.AddSingleton<IPaymentGateway, PayTrPaymentGateway>();
        services.AddSingleton<IPaymentGateway, ParamPaymentGateway>();
        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddSingleton<IPaymentGateway, BankTransferPaymentGateway>();

        services.AddSingleton<ICargoProvider, YurticiKargoProvider>();
        services.AddSingleton<ICargoProvider, ArasKargoProvider>();
        services.AddSingleton<ICargoProvider, MngKargoProvider>();
        services.AddSingleton<ICargoProvider, DhlCargoProvider>();
        services.AddSingleton<ICargoProvider, UpsCargoProvider>();
        services.AddSingleton<ICargoProvider, FedExCargoProvider>();

        services.AddSingleton<IMarketplaceConnector, TrendyolConnector>();
        services.AddSingleton<IMarketplaceConnector, HepsiburadaConnector>();
        services.AddSingleton<IMarketplaceConnector, N11Connector>();
        services.AddSingleton<IMarketplaceConnector, IdefixConnector>();
        services.AddSingleton<IMarketplaceConnector, AmazonConnector>();

        services.AddSingleton<IEInvoiceProvider, BizimHesapEInvoiceProvider>();
        services.AddSingleton<IEInvoiceProvider, NilveraEInvoiceProvider>();
        services.AddSingleton<IEInvoiceProvider, UyumsoftEInvoiceProvider>();
        services.AddSingleton<IEInvoiceProvider, ForibaEInvoiceProvider>();
        services.AddSingleton<IEInvoiceProvider, IzibizEInvoiceProvider>();

        services.AddSingleton<IProviderRegistry, ProviderRegistryImpl>();

        // Altyapı servisleri
        // ÖNEMLİ: Dekorras.Admin, Dekorras.Api ve Dekorras.Storefront AYRI ASP.NET Core
        // uygulamalarıdır. SetApplicationName + PersistKeysToFileSystem OLMADAN her biri kendi
        // izole anahtar setini alır - Admin'de şifrelenen bir sağlayıcı anahtarı (§4.1) Api/
        // Storefront tarafından ÇÖZÜLEMEZ (checkout sırasında ISecretProtector.Unprotect atar).
        // Üçü de aynı anahtar setini paylaşsın diye burada ortak bir uygulama adı ve ortak bir
        // fiziksel anahtar deposu tanımlanır (production'da paylaşılan bir volume/Redis/Blob
        // olmalıdır - bkz. plan §11).
        var keyRingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dekorras", "DataProtection-Keys");
        services.AddDataProtection()
            .SetApplicationName("Dekorras")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IExchangeRateProvider, ExchangeRateProvider>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IPushNotificationSender, FirebasePushNotificationSender>();

        // Yüklenen dosya klasörleri appsettings "Storage" bölümünden (varsayılan wwwroot/uploads + App_Data/private).
        StoragePaths.Configure(configuration);
        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(LocalFileStorage.SharedUploadsRoot));

        // Ölçüye özel duvar kağıdı: görsel depolama/türev/render (ImageSharp) + render önbelleği (HybridCache)
        services.AddHybridCache();
        services.AddSingleton<Application.WallCovering.IWallImageStore, WallCovering.WallImageStore>();
        services.AddSingleton<Application.WallCovering.IImageDerivativeService, WallCovering.ImageDerivativeService>();
        services.AddSingleton<Application.WallCovering.IWallPreviewRenderer, WallCovering.WallPreviewRenderer>();
        services.AddSingleton<Application.WallCovering.IDefaultSceneGenerator, WallCovering.DefaultSceneGenerator>();
        services.AddSingleton<Application.WallCovering.IWallRenderCache, WallCovering.WallRenderCache>();
        services.AddSingleton<Application.WallCovering.IExternalImageFetcher, WallCovering.ExternalImageFetcher>();
        services.AddSingleton<Application.WallCovering.IProductionFileRenderer, WallCovering.ProductionFileRenderer>();

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
            services.AddSingleton<ICacheService, Caching.RedisCacheService>();
        }

        return services;
    }
}
