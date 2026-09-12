using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Dekorras.Api.Auth;

/// <summary>
/// Yalnızca Dekorras.Api'ye özgüdür - Admin/Storefront çerez tabanlı kimlik doğrulama kullanır,
/// bu yüzden bu üretici bilinçli olarak paylaşılan Dekorras.Infrastructure yerine burada yaşar.
/// İmzalama anahtarı Program.cs'teki JwtBearer doğrulama yapılandırmasıyla AYNI olmalıdır.
/// </summary>
public sealed class JwtAccessTokenGenerator(IConfiguration configuration)
{
    public (string AccessToken, DateTime ExpiresAtUtc) Generate(string identityUserId, string email)
    {
        var minutes = configuration.GetValue("Jwt:AccessTokenMinutes", 30);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, identityUserId),
            new Claim(ClaimTypes.NameIdentifier, identityUserId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
