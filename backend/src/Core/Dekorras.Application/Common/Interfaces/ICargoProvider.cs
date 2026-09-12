namespace Dekorras.Application.Common.Interfaces;

public sealed record ShippingRateQuote(decimal PriceTry, int EstimatedDeliveryDays);
public sealed record ShipmentCreationResult(bool Success, string? TrackingNumber, string? FailureReason);
public sealed record ShipmentStatusResult(string StatusDescription, bool IsDelivered);

/// <summary>Kargo sağlayıcısı sözleşmesi: Yurtiçi, Aras, MNG (yurt içi) + DHL, UPS, FedEx (yurt dışı).
/// Yurt dışı gönderi/gümrük beyanı senaryolarını da bu soyutlama üzerinden kapsar (bkz. §7).</summary>
public interface ICargoProvider : IIntegrationConnector
{
    Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken cancellationToken);
    Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken cancellationToken);
    Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken cancellationToken);
}
