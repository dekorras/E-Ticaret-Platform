using Dekorras.Application.WallCovering;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace Dekorras.Storefront.WallCovering;

public static class WallFeatures
{
    public const string WallPreviewLink = "WallPreviewLink";

    public static IServiceCollection AddWallFeatureManagement(this IServiceCollection services)
    {
        services.AddMemoryCache();
        // AddFeatureManagement kendi yapılandırma sağlayıcısını TryAdd ile ekler - bizimki önce kaydedilmeli.
        services.AddSingleton<IFeatureDefinitionProvider, WallFeatureDefinitionProvider>();
        services.AddFeatureManagement().WithTargeting<WallTargetingContextAccessor>();
        return services;
    }
}

/// <summary>Özellik bayraklarını admin ayarından (Setting: WallCovering.WallPreviewLinkPercent) okur;
/// ayar yoksa appsettings `FeatureManagement:WallPreviewLink` (true/false veya 0–100), o da yoksa %100.
/// Kısmi yüzdede Targeting filtresi kullanılır: ziyaretçi anahtarına göre YAPIŞKAN karar verilir
/// (Percentage filtresi her istekte zar attığı için bağlantı sayfa sayfa görünüp kaybolurdu).
/// Değer 60 sn önbelleklenir - admin değişikliği en geç 1 dakikada yansır.</summary>
public sealed class WallFeatureDefinitionProvider(IServiceScopeFactory scopeFactory, IConfiguration configuration, IMemoryCache cache) : IFeatureDefinitionProvider
{
    private static readonly string[] Features = [WallFeatures.WallPreviewLink];

    public async Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName)
    {
        var percent = await cache.GetOrCreateAsync($"feature:{featureName}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            return await ResolvePercentAsync(featureName);
        });
        return Build(featureName, percent);
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        foreach (var name in Features)
            yield return await GetFeatureDefinitionAsync(name);
    }

    private async Task<int> ResolvePercentAsync(string featureName)
    {
        if (featureName == WallFeatures.WallPreviewLink)
        {
            using var scope = scopeFactory.CreateScope();
            var fromSettings = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetWallPreviewLinkPercentQuery());
            if (fromSettings is int p) return p;
        }

        var raw = configuration[$"FeatureManagement:{featureName}"];
        if (bool.TryParse(raw, out var enabled)) return enabled ? 100 : 0;
        if (int.TryParse(raw, out var percent)) return Math.Clamp(percent, 0, 100);
        return 100;
    }

    public static FeatureDefinition Build(string featureName, int percent)
    {
        if (percent <= 0) return new FeatureDefinition { Name = featureName, EnabledFor = [] };
        if (percent >= 100) return new FeatureDefinition { Name = featureName, EnabledFor = [new FeatureFilterConfiguration { Name = "AlwaysOn" }] };

        var parameters = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audience:DefaultRolloutPercentage"] = percent.ToString() })
            .Build();
        return new FeatureDefinition
        {
            Name = featureName,
            EnabledFor = [new FeatureFilterConfiguration { Name = "Microsoft.Targeting", Parameters = parameters }]
        };
    }
}

/// <summary>Kademeli açılışta hedefleme kimliği: üyede Identity kullanıcı kimliği, misafirde duvar çerezi.</summary>
public sealed class WallTargetingContextAccessor(IHttpContextAccessor httpContextAccessor) : ITargetingContextAccessor
{
    public ValueTask<TargetingContext> GetContextAsync()
    {
        var http = httpContextAccessor.HttpContext;
        // Yanıt başladıysa çerez yazılamaz - o durumda (nadir) istek bazlı kimliğe düşülür.
        var userId = http is null
            ? "anonymous"
            : WallVisitor.GetIdentityUserId(http.User)
              ?? (http.Response.HasStarted ? WallVisitor.GetGuestKey(http) ?? http.TraceIdentifier : WallVisitor.GetOrCreateGuestKey(http));
        return ValueTask.FromResult(new TargetingContext { UserId = userId, Groups = [] });
    }
}
