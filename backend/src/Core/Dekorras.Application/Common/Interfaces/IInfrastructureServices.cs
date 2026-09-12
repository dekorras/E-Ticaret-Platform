namespace Dekorras.Application.Common.Interfaces;

public interface IFileStorage
{
    Task<string> UploadAsync(string containerName, string fileName, Stream content, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string fileUrl, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string bodyHtml, CancellationToken cancellationToken);
}

public interface IPushNotificationSender
{
    Task SendAsync(string deviceToken, string title, string body, IReadOnlyDictionary<string, string>? data, CancellationToken cancellationToken);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public interface IExchangeRateProvider
{
    Task<decimal> GetRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken);
}

/// <summary>Sağlayıcı anahtarları (API key/secret) her zaman bu servis üzerinden şifreli saklanır
/// (ASP.NET Core Data Protection API ile Infrastructure katmanında implemente edilir, bkz. §4.1/§11).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface ICurrentUserService
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
}
