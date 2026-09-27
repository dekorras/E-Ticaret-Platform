using System.ComponentModel.DataAnnotations;

namespace Dekorras.Storefront.Models;

public sealed class RegisterFormModel
{
    [Required(ErrorMessage = "Ad Soyad zorunludur.")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public sealed class LoginFormModel
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

/// <summary>Hesabım > Kullanıcı bilgilerim formu. E-posta oturum kimliği olduğu için burada değiştirilmez (salt okunur).</summary>
public sealed class ProfileFormModel
{
    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [StringLength(200)]
    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    public bool NewsletterSubscribed { get; set; }
}
