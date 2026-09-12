using System.Globalization;
using System.Threading.RateLimiting;
using Dekorras.Application;
using Dekorras.Infrastructure;
using Dekorras.Infrastructure.Storage;
using Dekorras.Infrastructure.Web;
using Dekorras.Persistence;
using Dekorras.Persistence.Security;
using Dekorras.Storefront;
using Dekorras.Storefront.Components.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options => options.Filters.Add<CurrencyResultFilter>());
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Bkz. plan §11 - "reCAPTCHA v3, rate limiting, güvenlik başlıkları". reCAPTCHA gerçek API
// kimlik bilgisi gerektirdiği için BİLİNÇLİ OLARAK yazılmadı (bkz. README); rate limiting ise
// üçüncü taraf kimlik bilgisi gerektirmez - giriş/kayıt uç noktalarına IP başına brute-force
// koruması eklendi. "admin-auth" - eskiden Dekorras.Admin'in KENDİ "auth" politikasıydı, Faz 4
// birleştirmesinde (bkz. plan "Admin Panel Yeniden Yapılandırma") bu projenin ZATEN var olan
// "auth" politikasıyla İSİM ÇAKIŞMASINI önlemek için yeniden adlandırıldı.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("admin-auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Admin paneli artık AYRI bir uygulama DEĞİL - "/admin" altında AYNI süreçte çalışıyor (bkz. plan
// "Admin Panel Yeniden Yapılandırma" Faz 4, ASP.NET Core Areas mantığı). Blazor Server bileşenleri
// (Components/Admin/**) ile Storefront'un kendi MVC controller'ları AYNI endpoint routing
// tablosunda bir arada yaşıyor - `/admin/*` route'ları özgün (attribute-routed) olduğu için
// Storefront'un `{controller}/{action}` genel MVC route'uyla ÇAKIŞMAZ.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthorization(options =>
{
    // Permission claim'leri AppUserClaimsPrincipalFactory tarafından oturum çerezine gömülür
    // (bkz. Dekorras.Persistence/Security) - RBAC bu politikalar üzerinden çalışır (§10.1, §11).
    // SysAdmin claim'i verilmiş izinlerden BAĞIMSIZ olarak HER politikayı geçer (bkz. plan
    // "Admin Panel Yeniden Yapılandırma" Faz 1).
    options.AddPolicy("ECommerceAccess", policy => policy.RequireAssertion(ctx =>
        ctx.User.HasClaim(AppClaimTypes.SysAdmin, "true") || ctx.User.HasClaim(AppClaimTypes.Permission, "ECommerce.Access")));
    options.AddPolicy("AccountingAccess", policy => policy.RequireAssertion(ctx =>
        ctx.User.HasClaim(AppClaimTypes.SysAdmin, "true") || ctx.User.HasClaim(AppClaimTypes.Permission, "Accounting.Access")));
    options.AddPolicy("SysAdminOnly", policy => policy.RequireClaim(AppClaimTypes.SysAdmin, "true"));
});

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// AYNI paylaşılan "Identity.Application" çerez şeması hem Storefront'un müşteri girişini hem de
// Admin panelinin yönetici girişini taşıyor (ikisi de aynı AspNetUsers tablosunu paylaşıyor -
// bkz. AdminProfile deseni). Tek bir global LoginPath OLAMAZ (biri /admin/login'e, diğeri kendi
// /Account/Login'ine ihtiyaç duyuyor) - bu yüzden yönlendirme, isteğin yoluna göre DALLANIR.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        var isAdmin = context.Request.Path.StartsWithSegments("/admin");
        var loginPath = isAdmin ? "/admin/login" : "/Account/Login";
        var returnUrl = context.Request.Path + context.Request.QueryString;
        context.Response.Redirect($"{loginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        var isAdmin = context.Request.Path.StartsWithSegments("/admin");
        context.Response.Redirect(isAdmin ? "/admin/Account/AccessDenied" : "/Account/AccessDenied");
        return Task.CompletedTask;
    };
});

var app = builder.Build();

app.UseDekorrasSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    // Geliştirme ortamında migration'ları ve başlangıç yönetici hesabını otomatik uygula - eskiden
    // yalnızca Dekorras.Admin'in Program.cs'inde vardı, Faz 4 birleştirmesiyle buraya taşındı
    // (aksi halde Admin ayrı bir uygulama olarak hiç başlatılmayacağı için migration/seed hiç
    // TETİKLENMEZDİ).
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    await DbInitializer.SeedAsync(dbContext, userManager);
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

// Hedef diller TR/EN/DE/FR/NL/ES/AR (Arapça RTL) - bkz. plan §7. `StorefrontLanguage` çerezinden
// okunan dil, hem içerik sorgularının (Product/Category çevirisi) hem de .resx tabanlı UI metni
// yerelleştirmesinin (IStringLocalizer) TEK kaynağıdır - ASP.NET Core'un kendi çerez/kültür
// sağlayıcıları YERİNE bilinçli olarak burada, tek bir yerde çözülür.
app.Use(async (context, next) =>
{
    var languageCode = StorefrontLanguage.GetLanguage(context);
    var culture = new CultureInfo(languageCode);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Admin paneli giriş/çıkış işlemleri bilinçli olarak düz HTML <form> POST'ları ile buradaki uç
// noktalara yönlendirilir (Blazor EditForm/etkileşimli devre ile DEĞİL) - çünkü SignInManager'ın
// oturum çerezini yazabilmesi için normal bir HTTP istek/yanıt döngüsüne ihtiyacı var; bir SignalR
// devresi (interactive server render) üzerinden bu mümkün değildir. "/admin" öneki Storefront'un
// KENDİ "/Account/Login" (müşteri girişi) controller action'ıyla ÇAKIŞMASIN diye eklendi.
var adminAccountGroup = app.MapGroup("/admin/Account");

adminAccountGroup.MapPost("/Login", async (
    SignInManager<IdentityUser> signInManager,
    [FromForm] string email,
    [FromForm] string password,
    [FromForm] string? returnUrl) =>
{
    var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);

    // Bkz. plan §11 - "admin panelde ... 2FA (önerilen bir iyileştirme)". `PasswordSignInAsync`
    // 2FA etkin bir hesap için oturumu TAMAMLAMAZ, geçici bir "iki faktör bekleniyor" çerezi
    // yazıp `RequiresTwoFactor=true` döner - asıl oturum ancak `TwoFactorAuthenticatorSignInAsync`
    // ile doğru kod girildikten sonra açılır (bkz. `/admin/Account/LoginWith2fa`).
    if (result.RequiresTwoFactor)
    {
        var query = string.IsNullOrEmpty(returnUrl) ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.LocalRedirect($"~/admin/login-2fa{query}");
    }

    return result.Succeeded
        ? Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "~/admin" : $"~/{returnUrl.TrimStart('/')}")
        : Results.LocalRedirect("~/admin/login?error=1");
}).RequireRateLimiting("admin-auth");

adminAccountGroup.MapPost("/LoginWith2fa", async (
    SignInManager<IdentityUser> signInManager,
    [FromForm] string code,
    [FromForm] string? returnUrl) =>
{
    var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
    if (user is null)
        return Results.LocalRedirect("~/admin/login");

    // Kullanıcı kodu boşluk/tire ile girebilir (bazı uygulamalar "123 456" gösterir) - normalize edilir.
    var normalizedCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);
    var result = await signInManager.TwoFactorAuthenticatorSignInAsync(normalizedCode, isPersistent: true, rememberClient: false);

    if (!result.Succeeded)
    {
        var query = string.IsNullOrEmpty(returnUrl) ? "?error=1" : $"?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.LocalRedirect($"~/admin/login-2fa{query}");
    }

    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "~/admin" : $"~/{returnUrl.TrimStart('/')}");
}).RequireRateLimiting("admin-auth");

adminAccountGroup.MapPost("/Logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("~/admin/login");
});

app.MapStaticAssets();

// Ürün görselleri gibi yüklenen dosyalar Admin/Api/Storefront arasında PAYLAŞILAN fiziksel bir
// klasörden sunulur - bkz. LocalFileStorage.SharedUploadsRoot dokümantasyonu.
Directory.CreateDirectory(LocalFileStorage.SharedUploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(LocalFileStorage.SharedUploadsRoot),
    RequestPath = "/uploads"
});

app.MapRazorComponents<AdminApp>()
    .AddInteractiveServerRenderMode();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
