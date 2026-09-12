using Dekorras.Application.Content.Queries;

namespace Dekorras.Storefront.Views.Shared.Components.BannerZone;

/// <summary>
/// _Node.cshtml özyinelemeli partial'ının @model'i - DTO'nun kendisi ağaçtaki derinliği bilmediği
/// için (sonsuz döngü/aşırı derin ağaç koruması burada `RecursionDepth` ile yapılıyor, bkz. Domain'de
/// Depth alanı zaten en fazla 6 ile sınırlı ama savunma amaçlı burada da kesiliyor).
/// </summary>
public sealed record BannerNodeViewModel(BannerNodeTreeDto Node, int RecursionDepth);
