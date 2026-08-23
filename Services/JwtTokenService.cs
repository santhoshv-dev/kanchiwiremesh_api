using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using Microsoft.IdentityModel.Tokens;

namespace KanchimeshAPI.Services;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    public static JwtOptions BindAndValidate(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(options.SigningKey) && environment.IsDevelopment())
        {
            // A generated key lets local development run without committing a JWT
            // signing secret. Tokens are intentionally invalidated on restart.
            options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException("Authentication:Jwt:Issuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            throw new InvalidOperationException("Authentication:Jwt:Audience must be configured.");
        }

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:SigningKey must be configured from a secret store and contain at least 32 bytes.");
        }

        if (options.AccessTokenLifetimeMinutes is < 5 or > 525600)
        {
            throw new InvalidOperationException("Authentication:Jwt:AccessTokenLifetimeMinutes must be between 5 and 525600.");
        }

        return options;
    }
}

public sealed record JwtTokenResult(string AccessToken, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    JwtTokenResult CreateAccessToken(ApplicationUser user);
}

public sealed class JwtTokenService(JwtOptions options) : IJwtTokenService
{
    public JwtTokenResult CreateAccessToken(ApplicationUser user)
    {
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(options.AccessTokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtClaimTypes.MustChangePassword, user.MustChangePassword ? "true" : "false"),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
