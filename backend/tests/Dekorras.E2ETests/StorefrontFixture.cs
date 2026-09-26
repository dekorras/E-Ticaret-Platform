using System.Text.Json;
using Microsoft.Playwright;

namespace Dekorras.E2ETests;

/// <summary>Çalışan bir Storefront'a (varsayılan http://localhost:5297, DEKORRAS_E2E_BASEURL ile
/// değiştirilebilir) karşı gerçek tarayıcı testleri. Tarayıcı İNDİRİLMEZ: sistemde kurulu Microsoft Edge
/// (Playwright "msedge" kanalı) kullanılır. Sunucu erişilemiyorsa testler başarısız değil ATLANDI
/// olarak raporlanır - böylece `dotnet test` sunucu kapalıyken de yeşil kalır ama yanlış "geçti" demez.</summary>
public sealed class StorefrontFixture : IAsyncLifetime
{
    public string BaseUrl { get; } = (Environment.GetEnvironmentVariable("DEKORRAS_E2E_BASEURL") ?? "http://localhost:5297").TrimEnd('/');
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public IPlaywright Playwright { get; private set; } = default!;
    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetAsync($"{BaseUrl}/api/v1/materials");
            IsAvailable = response.IsSuccessStatusCode;
            UnavailableReason = IsAvailable ? null : $"Storefront {(int)response.StatusCode} döndü.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Storefront'a ulaşılamadı ({BaseUrl}): {ex.Message}";
        }
        if (!IsAvailable) return;

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
    }

    /// <summary>Sipariş kaydı gerektiren testler için geçici müşteri (geliştirme veritabanında müşteri olmayabilir).
    /// Çağıran test sonda <c>Customers</c>'tan siler.</summary>
    public static async Task<Dekorras.Domain.Customers.Customer> CreateTemporaryCustomerAsync(Dekorras.Persistence.ApplicationDbContext db)
    {
        var groupId = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            System.Linq.Queryable.Select(db.Set<Dekorras.Domain.Customers.CustomerGroup>(), g => g.Id));
        var customer = new Dekorras.Domain.Customers.Customer($"e2e-{Guid.NewGuid():N}", "E2E Müşteri", $"e2e-{Guid.NewGuid():N}@dekorras.test", groupId);
        db.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    /// <summary>İşlenmiş görseli olan (türevleri üretilmiş) konfigüre edilebilir bir ürünün slug'ı.</summary>
    public async Task<(string Slug, string ProductId)> AnyProductAsync(IAPIRequestContext api, int index = 0)
    {
        var response = await api.GetAsync($"{BaseUrl}/api/v1/products?pageSize=5");
        var doc = JsonDocument.Parse(await response.TextAsync());
        var item = doc.RootElement.GetProperty("items")[index];
        return (item.GetProperty("slug").GetString()!, item.GetProperty("productId").GetString()!);
    }
}

[CollectionDefinition("storefront")]
public sealed class StorefrontCollection : ICollectionFixture<StorefrontFixture>;
