namespace Dekorras.Storefront.Models;

/// <summary>Plan §2.1 - "sayfa başına gösterim (12/25/50/75/100)" - yalnızca bu 5 değer geçerlidir,
/// sorgu dizesiyle rastgele bir değer geçilirse en yakın varsayılana (24 - mevcut "varsayılan"
/// davranışla uyumlu) düşer.</summary>
public static class ProductListingHelper
{
    public static readonly int[] AllowedPageSizes = [12, 25, 50, 75, 100];
    public const int DefaultPageSize = 25;

    public static int NormalizePageSize(int requested) =>
        AllowedPageSizes.Contains(requested) ? requested : DefaultPageSize;
}
