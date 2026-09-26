using System.Threading.RateLimiting;
using Dekorras.Application.Ordering.Storefront;
using Dekorras.Application.WallCovering;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.RateLimiting;

namespace Dekorras.Storefront.WallCovering;

public sealed record QuoteRequest(string Product, WallConfigurationInput Configuration, int Quantity = 1);

public sealed record AddCartItemRequest(string Product, WallConfigurationInput Configuration, int Quantity = 1);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record ApplyCouponRequest(string Code);

/// <summary>Ölçüye özel duvar kağıdı konfigüratörünün Minimal API uçları (spec 1.9, `/api/v1`).
/// Storefront içinde çalışır: misafir sepet çerezi ve üye oturumu aynı origin'de kalır.
/// Tüm hatalar RFC 7807 ProblemDetails olarak döner; fiyat daima sunucuda hesaplanır.
/// CSRF: gövde yalnızca application/json ile bağlanır (çapraz site form gönderimi preflight'sız
/// JSON gönderemez) ve oturum çerezi SameSite=Lax'tır.</summary>
public static class WallApi
{
    public const string QuoteRateLimitPolicy = "wall-quote";

    public static void AddWallApiRateLimits(this RateLimiterOptions options)
    {
        // Fiyat teklifi her tuş vuruşunda (debounce'lu) çağrılabilir - IP başına dakikada 120 cömert ama sınırlı.
        options.AddPolicy(QuoteRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        // Oda fotoğrafı yükleme: IP başına saatte 20 (kota veritabanında ayrıca tutulur).
        options.AddPolicy(RoomUploadRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
        // Eşya işaretleme (maske) kaydı: IP başına saatte 60 (fotoğraf yükleme hakkından ayrı).
        options.AddPolicy(RoomMaskRateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    }

    public static RouteGroupBuilder MapWallApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter(ProblemDetailsFilter).WithTags("Duvar Kağıdı");

        api.MapGet("/materials", async (string? product, ISender sender) =>
        {
            Guid? productId = string.IsNullOrWhiteSpace(product) ? null : await sender.Send(new GetWallProductIdQuery(product));
            return Results.Ok(await sender.Send(new GetMaterialsQuery(productId)));
        });

        api.MapPost("/pricing/quote", async (QuoteRequest request, HttpContext http, ISender sender) =>
        {
            var productId = await ResolveProductAsync(sender, request.Product);
            var configuration = await ParseAsync(sender, request.Configuration, productId);
            var sessionKey = http.Request.Cookies.TryGetValue("dekorras_cart", out var key) ? key : null;
            return Results.Ok(await sender.Send(new QuoteWallpaperQuery(productId, configuration, request.Quantity, sessionKey)));
        }).RequireRateLimiting(QuoteRateLimitPolicy);

        api.MapPost("/cart/items", async (AddCartItemRequest request, HttpContext http, ISender sender) =>
        {
            var productId = await ResolveProductAsync(sender, request.Product);
            var configuration = await ParseAsync(sender, request.Configuration, productId);
            var customerId = await StorefrontCustomerId.ResolveAsync(sender, http.User);
            var result = await sender.Send(new AddConfiguredCartItemCommand(
                CartSession.GetOrCreateSessionKey(http), productId, configuration, request.Quantity, customerId));
            return Results.Created($"/api/v1/cart/items/{result.CartItemId}", result);
        });

        api.MapPatch("/cart/items/{id:guid}", async (Guid id, UpdateCartItemRequest request, HttpContext http, ISender sender) =>
        {
            await sender.Send(new UpdateCartItemQuantityByIdCommand(CartSession.GetOrCreateSessionKey(http), id, request.Quantity));
            return Results.NoContent();
        });

        api.MapDelete("/cart/items/{id:guid}", async (Guid id, HttpContext http, ISender sender) =>
        {
            await sender.Send(new RemoveCartItemByIdCommand(CartSession.GetOrCreateSessionKey(http), id));
            return Results.NoContent();
        });

        api.MapGet("/cart", async (HttpContext http, ISender sender) =>
            Results.Ok(await sender.Send(new GetCartQuery(CartSession.GetOrCreateSessionKey(http), StorefrontLanguage.GetLanguage(http)))));

        api.MapPost("/cart/coupon", async (ApplyCouponRequest request, HttpContext http, ISender sender) =>
        {
            var result = await sender.Send(new ApplyCouponCommand(CartSession.GetOrCreateSessionKey(http), request.Code));
            return Results.Ok(result);
        });

        api.MapGet("/delivery/estimate", async (string? materialCode, ISender sender) =>
            Results.Ok(await sender.Send(new GetDeliveryEstimateQuery(materialCode))));

        MapCatalog(api);
        MapLists(api);
        MapScenes(api);
        MapRoomPreviews(api);
        MapEmbed(api);
        MapOrdersAndRequests(api);
        return api;
    }

    /// <summary>Admin indirmeleri: üretim PDF'i ve tasarım talebi ekleri ÖZEL depodadır (herkese açık URL yok),
    /// yalnızca ECommerceAccess yetkili yöneticiye akıtılır. Müşteri yüklemesi olan ekler her zaman
    /// "attachment" olarak iner (tarayıcıda çalıştırılmaz).</summary>
    public static void MapWallAdminDownloads(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/wall").RequireAuthorization("ECommerceAccess").ExcludeFromDescription();

        admin.MapGet("/production-files/{id:guid}", async (Guid id, ISender sender) =>
        {
            var file = await sender.Send(new OpenProductionFileQuery(id));
            return file is null ? Results.NotFound() : Results.File(file.Content, "application/pdf", file.FileName);
        });

        admin.MapGet("/design-request-attachments/{id:guid}", async (Guid id, ISender sender) =>
        {
            var file = await sender.Send(new OpenDesignRequestAttachmentQuery(id));
            if (file is null) return Results.NotFound();
            var contentType = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().TryGetContentType(file.FileName, out var ct) ? ct : "application/octet-stream";
            return Results.File(file.Content, contentType, file.FileName);
        });
    }

    public const string RoomUploadRateLimitPolicy = "wall-room-upload";

    /// <summary>Eşya işaretleme (maske) kaydı fotoğraf yükleme hakkından düşmez; müşteri birkaç kez düzeltebilir.</summary>
    public const string RoomMaskRateLimitPolicy = "wall-room-mask";

    public sealed record ProofResponseRequest(string Token, string? Note);

    private static void MapOrdersAndRequests(RouteGroupBuilder api)
    {
        // POST /orders/{id}/proofs/{proofId}/approve|reject - e-postadaki token ile (giriş gerekmez).
        api.MapPost("/orders/{orderId:guid}/proofs/{proofId:guid}/{decision:regex(^(approve|reject)$)}",
            async (Guid orderId, Guid proofId, string decision, ProofResponseRequest request, ISender sender) =>
            {
                var proof = await sender.Send(new GetProofByTokenQuery(request.Token));
                if (proof is null || proof.ProofId != proofId || proof.OrderId != orderId)
                    return Results.Problem("Onay önizlemesi bulunamadı.", statusCode: StatusCodes.Status404NotFound);
                return Results.Ok(await sender.Send(new RespondToProofCommand(request.Token, decision == "approve", request.Note)));
            });

        // POST /design-requests (multipart) - antiforgery doğrulamalı (formu Storefront sayfaları gönderir).
        api.MapPost("/design-requests", async (HttpContext http, ISender sender, [Microsoft.AspNetCore.Mvc.FromForm] string fullName,
            [Microsoft.AspNetCore.Mvc.FromForm] string email, [Microsoft.AspNetCore.Mvc.FromForm] string requestType, [Microsoft.AspNetCore.Mvc.FromForm] string message,
            [Microsoft.AspNetCore.Mvc.FromForm] string? product, [Microsoft.AspNetCore.Mvc.FromForm] string? configuration) =>
        {
            if (!Enum.TryParse<Domain.WallCovering.DesignRequestType>(requestType, out var type))
                throw new WallConfigurationException(new Dictionary<string, string> { ["requestType"] = "Talep türünü seçin." });
            var files = http.Request.Form.Files.GetFiles("files");
            var streams = files.Select(f => new DesignRequestFile(f.OpenReadStream(), f.FileName, f.Length)).ToList();
            try
            {
                var id = await sender.Send(new CreateDesignRequestCommand(fullName, email, product, type, message, configuration, streams));
                return Results.Created($"/api/v1/design-requests/{id}", new { id });
            }
            finally
            {
                foreach (var s in streams) await s.Content.DisposeAsync();
            }
        }).RequireRateLimiting(RoomUploadRateLimitPolicy)
          .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(52 * 1024 * 1024));
    }

    public sealed record ExternalImageRequest(string Key, string ImageUrl);

    /// <summary>Harici görsel kaydı (spec 1.9 - POST /embed/external-images). Başka origin'deki script
    /// tarafından çağrılır: CORS yalnızca istemcinin izinli originleri için açılır; PublicKey + Origin birlikte doğrulanır.</summary>
    private static void MapEmbed(RouteGroupBuilder api)
    {
        api.MapMethods("/embed/external-images", ["OPTIONS"], async (HttpContext http, ISender sender) =>
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || !await sender.Send(new IsEmbedOriginAllowedQuery(origin))) return Results.StatusCode(StatusCodes.Status403Forbidden);
            AllowCors(http, origin);
            http.Response.Headers.AccessControlAllowMethods = "POST";
            http.Response.Headers.AccessControlAllowHeaders = "Content-Type";
            http.Response.Headers.AccessControlMaxAge = "600";
            return Results.NoContent();
        });

        api.MapPost("/embed/external-images", async (ExternalImageRequest request, HttpContext http, ISender sender) =>
        {
            var origin = http.Request.Headers.Origin.ToString();
            var client = string.IsNullOrEmpty(origin) ? null : await sender.Send(new GetEmbedClientQuery(request.Key, origin));
            if (client is null) return Results.Problem("Bu origin veya anahtar için izin yok.", statusCode: StatusCodes.Status403Forbidden);
            AllowCors(http, origin);
            try
            {
                var image = await sender.Send(new RegisterExternalImageCommand(request.Key, request.ImageUrl));
                return Results.Ok(image);
            }
            catch (ExternalImageRejectedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Görsel kabul edilmedi.");
            }
        }).RequireRateLimiting(QuoteRateLimitPolicy);
    }

    private static void AllowCors(HttpContext http, string origin)
    {
        http.Response.Headers.AccessControlAllowOrigin = origin;
        http.Response.Headers.Vary = "Origin";
    }

    private static void MapRoomPreviews(RouteGroupBuilder api)
    {
        // Kendi oda fotoğrafı (spec 1.6.4). Multipart istekler antiforgery doğrulamasından geçer
        // (çapraz site formu preflight'sız multipart gönderebildiği için) - JS "RequestVerificationToken" başlığını yollar.
        api.MapPost("/room-previews", async (HttpContext http, ISender sender, IFormFile photo, [Microsoft.AspNetCore.Mvc.FromForm] string corners,
            [Microsoft.AspNetCore.Mvc.FromForm] int imageWidth, [Microsoft.AspNetCore.Mvc.FromForm] int imageHeight,
            [Microsoft.AspNetCore.Mvc.FromForm] string wallWidthCm, [Microsoft.AspNetCore.Mvc.FromForm] string? wallHeightCm, [Microsoft.AspNetCore.Mvc.FromForm] string? name) =>
        {
            var points = corners.Split(',', StringSplitOptions.TrimEntries)
                .Select(s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
                .ToArray();
            if (!Domain.WallCovering.WallDimensions.TryParseLength(wallWidthCm, out var width))
                throw new WallConfigurationException(new Dictionary<string, string> { ["width"] = "Duvar genişliğini cm olarak girin." });
            decimal? height = Domain.WallCovering.WallDimensions.TryParseLength(wallHeightCm, out var h) ? h : null;

            await using var stream = photo.OpenReadStream();
            var scene = await sender.Send(new CreateUserRoomSceneCommand(await WallVisitor.GetOwnerKeyAsync(http, sender), stream, points,
                imageWidth, imageHeight, width, height, name));
            return Results.Created($"/api/v1/scenes/{scene.Id}", scene);
        }).RequireRateLimiting(RoomUploadRateLimitPolicy)
          .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(11 * 1024 * 1024));

        api.MapPost("/room-previews/{id:guid}/mask", async (Guid id, IFormFile mask, HttpContext http, ISender sender) =>
        {
            await using var stream = mask.OpenReadStream();
            return Results.Ok(await sender.Send(new SetUserRoomMaskCommand(await WallVisitor.GetOwnerKeyAsync(http, sender), id, stream)));
        }).RequireRateLimiting(RoomMaskRateLimitPolicy)
          .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(11 * 1024 * 1024));

        api.MapDelete("/room-previews/{id:guid}", async (Guid id, HttpContext http, ISender sender) =>
        {
            await sender.Send(new DeleteUserRoomSceneCommand(await WallVisitor.GetOwnerKeyAsync(http, sender), id));
            return Results.NoContent();
        });

        // "Ayarları sıfırla": kendi odasındaki eşya işaretlemesini kaldırır (gölge haritası maskesiz yeniden üretilir).
        api.MapDelete("/room-previews/{id:guid}/mask", async (Guid id, HttpContext http, ISender sender) =>
            Results.Ok(await sender.Send(new ClearUserRoomMaskCommand(await WallVisitor.GetOwnerKeyAsync(http, sender), id))))
            .RequireRateLimiting(RoomMaskRateLimitPolicy);

        api.MapGet("/room-previews/quota", async (HttpContext http, ISender sender) =>
            Results.Ok(await sender.Send(new GetRoomQuotaQuery(await WallVisitor.GetOwnerKeyAsync(http, sender)))));
    }

    public sealed record WallEventRequest(string Type, string? Source, string? Product);

    private static void MapScenes(RouteGroupBuilder api)
    {
        api.MapGet("/scenes", async (HttpContext http, ISender sender) =>
        {
            // Kullanıcının kendi sahneleri yalnızca kendisine (üye/misafir anahtarı) listelenir.
            var ownerKey = await WallVisitor.GetOwnerKeyAsync(http, sender);
            return Results.Ok(await sender.Send(new GetRoomScenesQuery(ownerKey)));
        });

        // GET /scenes/{id}/render?product=&w_cm=&h_cm=&material=&fit=&mirror=&filter=&crop=&align=&size=&panels=1&download=1
        api.MapGet("/scenes/{id:guid}/render", async (Guid id, string product, string? align, int? size, string? panels, string? download, HttpContext http, ISender sender) =>
        {
            var productId = await ResolveProductAsync(sender, product);
            var input = WallConfigurationInput.FromQuery(key => http.Request.Query.TryGetValue(key, out var v) ? v.ToString() : null);
            var configuration = await ParseAsync(sender, input, productId);
            var isDownload = download is "1" or "true";
            var ownerKey = await WallVisitor.GetOwnerKeyAsync(http, sender);

            var result = await sender.Send(new RenderWallPreviewQuery(id, productId, configuration, ParseAlign(align),
                isDownload ? 1600 : size ?? 1200, Watermark: true, PanelLines: panels is "1" or "true", OwnerKey: ownerKey));

            if (!isDownload) return Results.Redirect(result.Url);

            var store = http.RequestServices.GetRequiredService<IWallImageStore>();
            var stream = store.OpenPublic(result.Url) ?? throw new KeyNotFoundException("Render bulunamadı.");
            return Results.File(stream, "image/jpeg", $"duvarinda-gor-{product}.jpg");
        }).RequireRateLimiting(QuoteRateLimitPolicy);

        // Normalize "Duvarında Gör" bağlantısı (e-posta ve harici kullanım için) - spec 1.9.
        api.MapGet("/wall-preview/link", async (string product, string? scene, string? align, string? @return, string? src, HttpContext http, ISender sender) =>
        {
            var productId = await ResolveProductAsync(sender, product);
            var input = WallConfigurationInput.FromQuery(key => http.Request.Query.TryGetValue(key, out var v) ? v.ToString() : null);
            var configuration = await ParseAsync(sender, input, productId);
            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            return Results.Ok(new
            {
                url = WallPreviewUrl.Build(product, configuration, Guid.TryParse(scene, out var s) ? s : null, align, @return, src, baseUrl),
                shortUrl = $"{baseUrl}/p/{Uri.EscapeDataString(product)}/duvarinda-gor"
            });
        });

        api.MapPost("/events/wall-preview", async (WallEventRequest request, HttpContext http, ISender sender) =>
        {
            Guid? productId = string.IsNullOrWhiteSpace(request.Product) ? null : await sender.Send(new GetWallProductIdQuery(request.Product));
            // Ziyaretçi anahtarı çerezden okunur ama yeni çerez OLUŞTURULMAZ (ölçüm kimseyi izlemek için çerez bırakmasın).
            var visitor = WallVisitor.GetIdentityUserId(http.User) is { } uid ? "u:" + uid[..Math.Min(12, uid.Length)] : WallVisitor.GetGuestKey(http) is { } g ? "g:" + g[..12] : null;
            await sender.Send(new TrackWallPreviewEventCommand(request.Type, request.Source, productId, visitor));
            return Results.Accepted();
        }).RequireRateLimiting(QuoteRateLimitPolicy);
    }

    public static Domain.WallCovering.WallAlign ParseAlign(string? align) => align?.ToLowerInvariant() switch
    {
        "left" or "sol" => Domain.WallCovering.WallAlign.Left,
        "right" or "sag" or "sağ" => Domain.WallCovering.WallAlign.Right,
        _ => Domain.WallCovering.WallAlign.Center
    };

    private static void MapCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/products", async (string? tags, string? type, string? orientation, string? color, string? q, string? sort, int? page, int? pageSize, string? ids, string? category, ISender sender) =>
        {
            // ids: virgülle ayrılmış ürün kimlikleri (görüntüleyicideki "Favorilerim" sekmesi) - en fazla 60.
            IReadOnlyCollection<Guid>? only = string.IsNullOrWhiteSpace(ids)
                ? null
                : ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Take(60).ToList();
            return Results.Ok(await sender.Send(new GetWallCatalogQuery(
                string.IsNullOrWhiteSpace(tags) ? null : [tags], type, orientation, color, q, sort, page ?? 1, pageSize ?? 24, only, category)));
        });

        // Kategori seçimi (Duvar Kağıtları / Posterler + alt kategoriler). product verilirse ürünün kök kategorisi de döner.
        api.MapGet("/categories", async (string? product, ISender sender) =>
        {
            Guid? productId = string.IsNullOrWhiteSpace(product) ? null : await sender.Send(new GetWallProductIdQuery(product));
            return Results.Ok(await sender.Send(new GetWallCategoriesQuery(productId)));
        });

        api.MapGet("/products/{slug}", async (string slug, ISender sender) =>
            await sender.Send(new GetWallProductDetailQuery(slug)) is { } product
                ? Results.Ok(product)
                : Results.Problem("Ürün bulunamadı.", statusCode: StatusCodes.Status404NotFound));
    }

    public sealed record FavoriteRequest(Guid ProductId);

    public sealed record TryOnAddRequest(Guid? ProductId, string? Product, WallConfigurationInput? Configuration);

    public sealed record TryOnOrderRequest(IReadOnlyList<Guid> ProductIds);

    private static void MapLists(RouteGroupBuilder api)
    {
        // Favoriler: üyede Wishlist tablosu, misafirde HttpOnly çerez (girişte birleştirilir).
        api.MapGet("/favorites", async (HttpContext http, ISender sender) =>
            Results.Ok(new { productIds = await GetFavoritesAsync(http, sender) }));

        api.MapPost("/favorites", async (FavoriteRequest request, HttpContext http, ISender sender) =>
        {
            if (WallVisitor.GetIdentityUserId(http.User) is { } userId)
            {
                await sender.Send(new Dekorras.Application.Customers.Commands.AddToWishlistCommand(userId, request.ProductId));
            }
            else
            {
                var list = WallVisitor.GetGuestFavorites(http);
                if (list.Count >= WallVisitor.MaxGuestFavorites)
                    throw new InvalidOperationException($"Misafir olarak en fazla {WallVisitor.MaxGuestFavorites} favori eklenebilir. Daha fazlası için giriş yapın.");
                list.Add(request.ProductId);
                WallVisitor.SetGuestFavorites(http, list);
            }
            return Results.NoContent();
        });

        api.MapDelete("/favorites/{productId:guid}", async (Guid productId, HttpContext http, ISender sender) =>
        {
            if (WallVisitor.GetIdentityUserId(http.User) is { } userId)
                await sender.Send(new Dekorras.Application.Customers.Commands.RemoveFromWishlistCommand(userId, productId));
            else
                WallVisitor.SetGuestFavorites(http, WallVisitor.GetGuestFavorites(http).Where(id => id != productId));
            return Results.NoContent();
        });

        api.MapGet("/try-on-list/items", async (HttpContext http, ISender sender) =>
            Results.Ok(await sender.Send(new GetTryOnListQuery(await WallVisitor.GetOwnerKeyAsync(http, sender)))));

        api.MapPost("/try-on-list/items", async (TryOnAddRequest request, HttpContext http, ISender sender) =>
        {
            var productId = request.ProductId ?? await ResolveProductAsync(sender, request.Product);
            string? configurationJson = null;
            if (request.Configuration is not null)
                configurationJson = (await ParseAsync(sender, request.Configuration, productId)).ToJson();

            var ownerKey = await WallVisitor.GetOwnerKeyAsync(http, sender);
            await sender.Send(new AddTryOnItemCommand(ownerKey, productId, configurationJson));
            return Results.Ok(await sender.Send(new GetTryOnListQuery(ownerKey)));
        });

        api.MapDelete("/try-on-list/items/{productId:guid}", async (Guid productId, HttpContext http, ISender sender) =>
        {
            var ownerKey = await WallVisitor.GetOwnerKeyAsync(http, sender);
            await sender.Send(new RemoveTryOnItemCommand(ownerKey, productId));
            return Results.Ok(await sender.Send(new GetTryOnListQuery(ownerKey)));
        });

        api.MapPatch("/try-on-list/order", async (TryOnOrderRequest request, HttpContext http, ISender sender) =>
        {
            await sender.Send(new ReorderTryOnListCommand(await WallVisitor.GetOwnerKeyAsync(http, sender), request.ProductIds ?? []));
            return Results.NoContent();
        });

        api.MapPost("/try-on-list/share", async (HttpContext http, ISender sender) =>
        {
            var token = await sender.Send(new ShareTryOnListCommand(await WallVisitor.GetOwnerKeyAsync(http, sender)));
            return Results.Ok(new { token, url = $"{http.Request.Scheme}://{http.Request.Host}/duvarimda-dene/{token}" });
        });
    }

    public static async Task<IReadOnlyList<Guid>> GetFavoritesAsync(HttpContext http, ISender sender) =>
        WallVisitor.GetIdentityUserId(http.User) is { } userId
            ? await sender.Send(new GetFavoriteProductIdsQuery(userId))
            : WallVisitor.GetGuestFavorites(http);

    private static async Task<Guid> ResolveProductAsync(ISender sender, string? slug) =>
        (string.IsNullOrWhiteSpace(slug) ? null : await sender.Send(new GetWallProductIdQuery(slug)))
        ?? throw new WallConfigurationException(new Dictionary<string, string> { ["product"] = "Ürün bulunamadı veya ölçüye özel sipariş edilemez." });

    /// <summary>Malzeme verilmemişse ilk aktif (sıralamadaki ilk) malzeme; ölçü verilmemişse ayar varsayılanı.
    /// Geçersiz ölçü burada varsayılana DÜŞÜRÜLMEZ: kullanıcı açıkça yanlış değer girdiyse fiyatlama alan hatası döner.</summary>
    private static async Task<Domain.WallCovering.WallConfiguration> ParseAsync(ISender sender, WallConfigurationInput? input, Guid productId)
    {
        input ??= new WallConfigurationInput();
        var materials = await sender.Send(new GetMaterialsQuery(productId));
        var defaultMaterial = materials.FirstOrDefault()?.Code
            ?? throw new WallConfigurationException(new Dictionary<string, string> { ["material"] = "Bu ürün için satışta malzeme yok." });

        var settings = await sender.Send(new GetWallCoveringSettingsQuery());
        var configuration = input.Parse(defaultMaterial, settings.DefaultWidthCm, settings.DefaultHeightCm);

        // Kullanıcı bir değer yazmış ama sayı değilse (ör. "abc") sessizce varsayılana düşmek yerine hata ver.
        var errors = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(input.Width) && !Domain.WallCovering.WallDimensions.TryParseLength(input.Width, out _)) errors["width"] = "En geçerli bir sayı olmalıdır.";
        if (!string.IsNullOrWhiteSpace(input.Height) && !Domain.WallCovering.WallDimensions.TryParseLength(input.Height, out _)) errors["height"] = "Boy geçerli bir sayı olmalıdır.";
        if (errors.Count > 0) throw new WallConfigurationException(errors);

        return configuration;
    }

    private static async ValueTask<object?> ProblemDetailsFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (WallConfigurationException ex)
        {
            return Results.ValidationProblem(ex.Errors.ToDictionary(e => e.Key, e => new[] { e.Value }), title: "Konfigürasyon geçersiz.");
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(
                ex.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                title: "İstek geçersiz.");
        }
        catch (RoomPreviewQuotaException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Oda önizleme kotası",
                extensions: new Dictionary<string, object?> { ["requiresLogin"] = ex.RequiresLogin });
        }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            return Results.Problem("Oturum doğrulaması başarısız. Sayfayı yenileyip tekrar deneyin.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı.");
        }
        catch (Domain.Common.DomainException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "İşlem yapılamadı.");
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "İşlem yapılamadı.");
        }
    }
}
