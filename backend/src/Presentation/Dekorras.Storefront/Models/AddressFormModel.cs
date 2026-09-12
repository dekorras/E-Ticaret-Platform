using System.ComponentModel.DataAnnotations;

namespace Dekorras.Storefront.Models;

public sealed class AddressFormModel
{
    [Required(ErrorMessage = "Alıcı adı zorunludur.")]
    public string RecipientName { get; set; } = "";

    [Required]
    public string CountryCode { get; set; } = "TR";

    public string? State { get; set; }

    [Required(ErrorMessage = "Şehir zorunludur.")]
    public string City { get; set; } = "";

    public string? District { get; set; }

    public string? PostalCode { get; set; }

    [Required(ErrorMessage = "Adres zorunludur.")]
    public string AddressLine1 { get; set; } = "";

    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    public string PhoneNumber { get; set; } = "";
}
