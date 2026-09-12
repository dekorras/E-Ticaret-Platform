using System.Security.Cryptography;
using Dekorras.Api.Auth;
using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Identity.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dekorras.Api.Controllers;

/// <summary>
/// Mobil uygulama ve üçüncü taraf entegrasyonlar için JWT tabanlı kimlik doğrulama - Admin/
/// Storefront'un çerez tabanlı girişinden TAMAMEN AYRIDIR (aynı AspNetUsers tablosu paylaşılır).
/// Yenileme jetonu rotasyonu kullanılır: her /refresh çağrısı eskisini iptal edip yenisini verir
/// (bkz. Dekorras.Domain.Identity.RefreshToken belgesi).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<IdentityUser> userManager,
    ISender sender,
    JwtAccessTokenGenerator accessTokenGenerator,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new IdentityUser { UserName = request.Email, Email = request.Email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ValidationProblem(string.Join(", ", result.Errors.Select(e => e.Description)));

        await sender.Send(new CreateCustomerProfileCommand(user.Id, request.FullName, request.Email), cancellationToken);

        return Ok(await IssueTokenPairAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { message = "Geçersiz e-posta veya şifre." });

        return Ok(await IssueTokenPairAsync(user, cancellationToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var oldTokenHash = Hash(request.RefreshToken);
        var (newRawToken, newTokenHash, newExpiresAtUtc) = GenerateRefreshToken();

        var identityUserId = await sender.Send(new RotateRefreshTokenCommand(oldTokenHash, newTokenHash, newExpiresAtUtc), cancellationToken);
        if (identityUserId is null)
            return Unauthorized(new { message = "Yenileme jetonu geçersiz veya süresi dolmuş." });

        var user = await userManager.FindByIdAsync(identityUserId);
        if (user is null) return Unauthorized(new { message = "Kullanıcı bulunamadı." });

        var (accessToken, accessExpiresAtUtc) = accessTokenGenerator.Generate(user.Id, user.Email!);
        return Ok(new AuthResponse(accessToken, accessExpiresAtUtc, newRawToken, newExpiresAtUtc));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeRefreshTokenCommand(Hash(request.RefreshToken)), cancellationToken);
        return NoContent();
    }

    private async Task<AuthResponse> IssueTokenPairAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        var (accessToken, accessExpiresAtUtc) = accessTokenGenerator.Generate(user.Id, user.Email!);
        var (rawRefreshToken, refreshTokenHash, refreshExpiresAtUtc) = GenerateRefreshToken();

        await sender.Send(new IssueRefreshTokenCommand(user.Id, refreshTokenHash, refreshExpiresAtUtc), cancellationToken);

        return new AuthResponse(accessToken, accessExpiresAtUtc, rawRefreshToken, refreshExpiresAtUtc);
    }

    private (string RawToken, string TokenHash, DateTime ExpiresAtUtc) GenerateRefreshToken()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var days = configuration.GetValue("Jwt:RefreshTokenDays", 14);
        return (rawToken, Hash(rawToken), DateTime.UtcNow.AddDays(days));
    }

    private static string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}

public sealed record RegisterRequest(string Email, string Password, string FullName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);
