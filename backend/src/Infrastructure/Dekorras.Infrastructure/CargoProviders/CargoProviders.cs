using Dekorras.Application.Common.Interfaces;
using Dekorras.Infrastructure.Common;

namespace Dekorras.Infrastructure.CargoProviders;

file static class SharedFields
{
    public static readonly ConfigFieldDefinition[] Domestic =
    [
        new ConfigFieldDefinition("AccountNumber", "Cari Hesap No", ConfigFieldType.Text, true),
        new ConfigFieldDefinition("ApiKey", "API Anahtarı", ConfigFieldType.Password, true)
    ];

    public static readonly ConfigFieldDefinition[] International =
    [
        new ConfigFieldDefinition("AccountNumber", "Hesap Numarası", ConfigFieldType.Text, true),
        new ConfigFieldDefinition("ApiKey", "API Anahtarı", ConfigFieldType.Password, true),
        new ConfigFieldDefinition("ApiSecret", "API Gizli Anahtarı", ConfigFieldType.Password, true)
    ];
}

public sealed class YurticiKargoProvider() : ConnectorBase("yurtici-kargo", "Yurtiçi Kargo", SharedFields.Domestic), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 15m, 2));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"YK{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Dağıtıma çıktı", false));
}

public sealed class ArasKargoProvider() : ConnectorBase("aras-kargo", "Aras Kargo", SharedFields.Domestic), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 14m, 2));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"AR{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Dağıtıma çıktı", false));
}

public sealed class MngKargoProvider() : ConnectorBase("mng-kargo", "MNG Kargo", SharedFields.Domestic), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 14.5m, 2));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"MNG{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Dağıtıma çıktı", false));
}

public sealed class DhlCargoProvider() : ConnectorBase("dhl", "DHL", SharedFields.International), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 120m, 5));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"DHL{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Gümrükte", false));
}

public sealed class UpsCargoProvider() : ConnectorBase("ups", "UPS", SharedFields.International), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 125m, 5));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"UPS{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Gümrükte", false));
}

public sealed class FedExCargoProvider() : ConnectorBase("fedex", "FedEx", SharedFields.International), ICargoProvider
{
    public Task<ShippingRateQuote> GetRateAsync(IReadOnlyDictionary<string, string> config, string countryCode, decimal weightKg, CancellationToken ct) =>
        Task.FromResult(new ShippingRateQuote(weightKg * 130m, 5));
    public Task<ShipmentCreationResult> CreateShipmentAsync(IReadOnlyDictionary<string, string> config, Guid orderId, CancellationToken ct) =>
        Task.FromResult(new ShipmentCreationResult(true, $"FDX{orderId:N}"[..14].ToUpperInvariant(), null));
    public Task<ShipmentStatusResult> GetTrackingStatusAsync(IReadOnlyDictionary<string, string> config, string trackingNumber, CancellationToken ct) =>
        Task.FromResult(new ShipmentStatusResult("Gümrükte", false));
}
