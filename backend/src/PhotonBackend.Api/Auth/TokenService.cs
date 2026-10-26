using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PhotonBackend.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public static SymmetricSecurityKey KeyFor(string secret) => new(Encoding.UTF8.GetBytes(secret));

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = KeyFor(_options.Secret),
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    public string Create(Guid playerId, string displayName)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, displayName),
        };
        var token = new JwtSecurityToken(
            _options.Issuer,
            audience: null,
            claims,
            expires: DateTime.UtcNow.AddHours(_options.LifetimeHours),
            signingCredentials: new SigningCredentials(KeyFor(_options.Secret), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Validates a token outside the ASP.NET auth pipeline (used by the Photon auth callback).</summary>
    public (Guid PlayerId, string DisplayName)? TryValidate(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, ValidationParameters, out _);
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var name = principal.FindFirstValue(JwtRegisteredClaimNames.Name) ?? "";
            return Guid.TryParse(sub, out var id) ? (id, name) : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
