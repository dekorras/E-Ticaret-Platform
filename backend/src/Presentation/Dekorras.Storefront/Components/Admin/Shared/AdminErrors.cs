namespace Dekorras.Storefront.Components.Admin.Shared;

/// <summary>Admin sayfalarında hata mesajını kullanıcıya okunur hâle getirir: FluentValidation hatalarında
/// uzun "Validation failed: -- Prop: ..." metni yerine yalnızca kural mesajları gösterilir.</summary>
public static class AdminErrors
{
    public static string Message(Exception ex) => ex switch
    {
        FluentValidation.ValidationException v when v.Errors.Any() => string.Join(" ", v.Errors.Select(e => e.ErrorMessage).Distinct()),
        _ => ex.Message
    };
}
