using System.Security.Cryptography;
using System.Text;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>Harici görsel indirme reddedildiğinde (SSRF koruması, boyut, biçim) fırlatılır.</summary>
public sealed class ExternalImageRejectedException(string message) : Exception(message);

/// <summary>SSRF korumalı harici görsel indirici (spec 1.6.6-D): yalnızca https, yalnızca izinli hostlar,
/// özel/loopback/link-local IP'ler engelli (bağlantı anında kontrol - DNS rebinding'e karşı), yönlendirme
/// izlenmez, boyut ve süre sınırlı.</summary>
public interface IExternalImageFetcher
{
    Task<byte[]> DownloadAsync(Uri url, IReadOnlyCollection<string> allowedHosts, CancellationToken cancellationToken);
}

public sealed record EmbedClientInfo(Guid Id, string Name, string PublicKey, IReadOnlyList<string> Origins, IReadOnlyList<string> ImageHosts);

/// <summary>Aktif bir embed istemcisini anahtarla bulur; origin verilirse izin listesinde olmalıdır.</summary>
public sealed record GetEmbedClientQuery(string PublicKey, string? Origin = null) : IRequest<EmbedClientInfo?>;

public sealed class GetEmbedClientQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetEmbedClientQuery, EmbedClientInfo?>
{
    public Task<EmbedClientInfo?> Handle(GetEmbedClientQuery request, CancellationToken cancellationToken)
    {
        var client = unitOfWork.Repository<EmbedClient>().Query().FirstOrDefault(c => c.PublicKey == request.PublicKey && c.IsActive);
        if (client is null || (request.Origin is not null && !client.IsOriginAllowed(request.Origin)))
            return Task.FromResult<EmbedClientInfo?>(null);
        return Task.FromResult<EmbedClientInfo?>(new EmbedClientInfo(client.Id, client.Name, client.PublicKey, client.Origins, client.ImageHosts));
    }
}

/// <summary>CORS ön kontrolü (preflight gövde taşımaz): origin herhangi bir aktif istemcinin izin listesinde mi.</summary>
public sealed record IsEmbedOriginAllowedQuery(string Origin) : IRequest<bool>;

public sealed class IsEmbedOriginAllowedQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<IsEmbedOriginAllowedQuery, bool>
{
    public Task<bool> Handle(IsEmbedOriginAllowedQuery request, CancellationToken cancellationToken)
    {
        var normalized = request.Origin.TrimEnd('/').ToLowerInvariant();
        var candidates = unitOfWork.Repository<EmbedClient>().Query().Where(c => c.IsActive && c.AllowedOrigins.Contains(normalized)).ToList();
        return Task.FromResult(candidates.Any(c => c.IsOriginAllowed(normalized)));
    }
}

/// <summary>Günlük kotadan bir birim düşer (görüntüleyici her açıldığında); kota doluysa false.</summary>
public sealed record ConsumeEmbedQuotaCommand(string PublicKey) : IRequest<bool>;

public sealed class ConsumeEmbedQuotaCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ConsumeEmbedQuotaCommand, bool>
{
    public async Task<bool> Handle(ConsumeEmbedQuotaCommand request, CancellationToken cancellationToken)
    {
        var client = unitOfWork.Repository<EmbedClient>().Query().FirstOrDefault(c => c.PublicKey == request.PublicKey && c.IsActive);
        if (client is null) return false;
        var ok = client.TryConsumeQuota(DateOnly.FromDateTime(DateTime.UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ok;
    }
}

public sealed record ExternalImageDto(Guid Id, string PreviewUrl, string ThumbUrl, int WidthPx, int HeightPx);

/// <summary>Bu sistemde kayıtlı olmayan harici posteri (data-image) izinli alan adından indirir, türevlerini
/// üretir ve kaydeder. Aynı istemci + aynı URL için idempotenttir (önbellek).</summary>
public sealed record RegisterExternalImageCommand(string PublicKey, string SourceUrl) : IRequest<ExternalImageDto>;

public sealed class RegisterExternalImageCommandHandler(IUnitOfWork unitOfWork, IExternalImageFetcher fetcher, IImageDerivativeService images)
    : IRequestHandler<RegisterExternalImageCommand, ExternalImageDto>
{
    public async Task<ExternalImageDto> Handle(RegisterExternalImageCommand request, CancellationToken cancellationToken)
    {
        var client = unitOfWork.Repository<EmbedClient>().Query().FirstOrDefault(c => c.PublicKey == request.PublicKey && c.IsActive)
            ?? throw new ExternalImageRejectedException("Geçersiz veya pasif gömme anahtarı.");

        if (!Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ExternalImageRejectedException("Görsel adresi https ile başlayan geçerli bir URL olmalıdır.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        var repository = unitOfWork.Repository<ExternalImage>();
        var cached = repository.Query().FirstOrDefault(i => i.EmbedClientId == client.Id && i.SourceUrlHash == hash);
        if (cached is not null) return ToDto(cached);

        var bytes = await fetcher.DownloadAsync(uri, client.ImageHosts, cancellationToken);
        using var stream = new MemoryStream(bytes);
        var inspection = images.Inspect(stream);
        if (!inspection.IsValid) throw new ExternalImageRejectedException(inspection.Error ?? "Dosya geçerli bir görsel değil.");

        stream.Position = 0;
        var derivatives = await images.CreateDerivativesAsync(stream, "external", hash[..24], cancellationToken);
        var image = new ExternalImage(client.Id, uri.AbsoluteUri, hash, derivatives.PreviewUrl, derivatives.ThumbUrl, derivatives.WidthPx, derivatives.HeightPx);
        await repository.AddAsync(image, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(image);
    }

    public static ExternalImageDto ToDto(ExternalImage i) => new(i.Id, i.PreviewUrl, i.ThumbUrl, i.WidthPx, i.HeightPx);
}

public sealed record GetExternalImageQuery(Guid Id, string PublicKey) : IRequest<ExternalImageDto?>;

public sealed class GetExternalImageQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetExternalImageQuery, ExternalImageDto?>
{
    public Task<ExternalImageDto?> Handle(GetExternalImageQuery request, CancellationToken cancellationToken)
    {
        var clientId = unitOfWork.Repository<EmbedClient>().Query().Where(c => c.PublicKey == request.PublicKey && c.IsActive).Select(c => (Guid?)c.Id).FirstOrDefault();
        var image = clientId is null ? null : unitOfWork.Repository<ExternalImage>().Query().FirstOrDefault(i => i.Id == request.Id && i.EmbedClientId == clientId);
        return Task.FromResult(image is null ? null : RegisterExternalImageCommandHandler.ToDto(image));
    }
}
