using System.ComponentModel.DataAnnotations;

namespace Dekorras.Storefront.Models;

public sealed class CheckoutFormModel
{
    [Required(ErrorMessage = "Ad Soyad zorunludur.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    public string PhoneNumber { get; set; } = "";

    [Required]
    public string CountryCode { get; set; } = "TR";

    [Required(ErrorMessage = "Şehir zorunludur.")]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Adres zorunludur.")]
    public string AddressLine1 { get; set; } = "";

    [Required(ErrorMessage = "Bir ödeme yöntemi seçin.")]
    public string PaymentProviderKey { get; set; } = "";

    [Required(ErrorMessage = "Bir kargo yöntemi seçin.")]
    public string CargoProviderKey { get; set; } = "";
}
