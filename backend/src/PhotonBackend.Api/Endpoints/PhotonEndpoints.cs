using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Auth;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Api.Endpoints;

/// <summary>Endpoints called by Photon Cloud: custom authentication and Quantum webhooks.</summary>
public static class PhotonEndpoints
{
    // Photon matches property names exactly (ResultCode, UserId, ...), so keep PascalCase.
    private static readonly JsonSerializerOptions PhotonJson = new() { PropertyNamingPolicy = null };

    public static void MapPhoton(this IEndpointRouteBuilder app)
    {
        // Custom authentication. Configure the URL in the Photon dashboard as
        //   https://<host>/photon/auth?secret=<Photon:AuthSecret>
        // and have the client send AuthValues parameter "token" (the JWT from /api/auth/guest).
        app.MapMethods("/photon/auth", ["GET", "POST"], PhotonAuth);

        var hooks = app.MapGroup("/photon/webhooks").AddEndpointFilter(RequireWebhookSecret);
        hooks.MapPost("/game/create", GameCreate);
        hooks.MapPost("/game/close", GameClose);
        hooks.MapPost("/game/result", GameResult);

        // Accepted so Photon's default-enabled hooks do not fail; nothing to store yet.
        foreach (var path in new[]
                 {
                     "/game/join", "/game/leave", "/game/configs",
                     "/player/add", "/player/added", "/player/removed",
                 })
            hooks.MapPost(path, () => Results.Ok(new { }));

        // Replay capture is not stored in this sample; acknowledge so Photon does not retry.
        hooks.MapPost("/replay/start", () => Results.Json(new { Skip = true }, PhotonJson));
        hooks.MapPost("/replay/chunk", () => Results.Ok(new { }));
    }

    private static async ValueTask<object?> RequireWebhookSecret(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var expected = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<PhotonOptions>>().Value.WebhookSecret;
        var given = ctx.HttpContext.Request.Headers["X-SecretKey"].ToString();
        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PhotonWebhook");
        if (!SecretsEqual(expected, given))
        {
            log.LogWarning("Photon webhook {Path} refused: bad X-SecretKey", ctx.HttpContext.Request.Path);
            return Results.Json(new { Status = 401, Error = "Unauthorized", Message = "Bad secret." }, PhotonJson, statusCode: 400);
        }
        log.LogInformation("Photon webhook {Path}", ctx.HttpContext.Request.Path);
        return await next(ctx);
    }

    internal static bool SecretsEqual(string expected, string given)
    {
        if (expected.Length == 0) return false; // an unset secret must never match
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static IResult PhotonAuth(HttpRequest req, TokenService tokens, IOptions<PhotonOptions> photon, ILoggerFactory loggers)
    {
        // Logged so the host's log shows whether Photon Cloud reaches this endpoint and why it is refused.
        var log = loggers.CreateLogger("PhotonAuth");

        if (!SecretsEqual(photon.Value.AuthSecret, req.Query["secret"].ToString()))
        {
            log.LogWarning("Photon auth refused: wrong or missing secret (from {Remote})", req.HttpContext.Connection.RemoteIpAddress);
            return Results.Json(new { ResultCode = 2, Message = "Unauthorized caller." }, PhotonJson);
        }

        var token = req.Query["token"].ToString();
        if (token.Length == 0)
        {
            log.LogWarning("Photon auth refused: no token parameter");
            return Results.Json(new { ResultCode = 3, Message = "Invalid parameters." }, PhotonJson);
        }

        var identity = tokens.TryValidate(token);
        if (identity is null)
        {
            log.LogWarning("Photon auth refused: invalid or expired token");
            return Results.Json(new { ResultCode = 2, Message = "Invalid or expired token." }, PhotonJson);
        }

        log.LogInformation("Photon auth accepted for player {PlayerId}", identity.Value.PlayerId);
        return Results.Json(new
        {
            ResultCode = 1,
            UserId = identity.Value.PlayerId.ToString(),
            Nickname = identity.Value.DisplayName,
            AuthCookie = new { playerId = identity.Value.PlayerId },
        }, PhotonJson);
    }

    private static async Task<IResult> GameCreate(JsonElement body, MatchService matches, CancellationToken ct)
    {
        var gameId = Str(body, "GameId");
        if (gameId.Length == 0) return Results.Ok(new { });
        await matches.GetOrCreateMatchAsync(gameId, Str(body, "Region"), ct);
        return Results.Ok(new { });
    }

    private static async Task<IResult> GameClose(JsonElement body, MatchService matches, CancellationToken ct)
    {
        var gameId = Str(body, "GameId");
        if (gameId.Length > 0) await matches.CloseAsync(gameId, ct);
        return Results.Ok(new { });
    }

    private static async Task<IResult> GameResult(JsonElement body, AppDbContext db, ILogger<MatchService> log, CancellationToken ct)
    {
        // Stored for auditing only. Ratings come from the player-reported, cross-checked results.
        var gameId = Str(body, "GameId");
        var match = gameId.Length == 0 ? null : db.Matches.FirstOrDefault(m => m.GameId == gameId);
        if (match is not null)
        {
            var raw = body.GetRawText();
            match.PhotonResultRaw = raw.Length > 64_000 ? raw[..64_000] : raw;
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("Photon GameResult webhook for game {GameId}", gameId);
        return Results.Ok(new { });
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object &&
        body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
