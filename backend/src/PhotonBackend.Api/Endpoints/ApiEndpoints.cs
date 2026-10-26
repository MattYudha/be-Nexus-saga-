using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Auth;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Api.Endpoints;

public sealed record GuestLoginRequest(string? DeviceId, string? DisplayName);
public sealed record ReportResultRequest(string? GameId, int Slot, int ScoreLeft, int ScoreRight);
public sealed record JoinQueueRequest(string? Region);

/// <summary>REST API used by the Unity client.</summary>
public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/auth/guest", GuestLogin);

        var secured = api.MapGroup("").RequireAuthorization();
        secured.MapGet("/me", Me);
        secured.MapGet("/matches", MyMatches);
        secured.MapPost("/matches/result", ReportResult);
        secured.MapPost("/matchmaking/join", JoinQueue);
        secured.MapGet("/matchmaking/status", QueueStatus);
        secured.MapPost("/matchmaking/leave", LeaveQueue);

        api.MapGet("/leaderboard", Leaderboard);
    }

    private static object Serialize(MatchmakingResult r) => r.Assignment is { } a
        ? new { state = r.State.ToString(), gameId = a.GameId, slot = a.Slot, region = a.Region, opponentId = a.OpponentId }
        : new { state = r.State.ToString(), gameId = (string?)null, slot = (int?)null, region = (string?)null, opponentId = (Guid?)null };

    private static async Task<IResult> JoinQueue(
        JoinQueueRequest req, ClaimsPrincipal user, AppDbContext db, MatchmakingService mm, CancellationToken ct)
    {
        var region = req.Region?.Trim() ?? "";
        if (region.Length is < 1 or > 32)
            return Results.BadRequest(new { error = "region must be 1-32 characters." });

        var id = PlayerId(user);
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Results.NotFound();

        return Results.Ok(Serialize(mm.Join(id, p.Rating, region)));
    }

    private static IResult QueueStatus(ClaimsPrincipal user, MatchmakingService mm) =>
        Results.Ok(Serialize(mm.Status(PlayerId(user))));

    private static IResult LeaveQueue(ClaimsPrincipal user, MatchmakingService mm)
    {
        mm.Leave(PlayerId(user));
        return Results.Ok(new { state = MatchmakingState.Idle.ToString() });
    }

    private static Guid PlayerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> GuestLogin(
        GuestLoginRequest req, AppDbContext db, TokenService tokens, IOptions<GameOptions> game, CancellationToken ct)
    {
        var deviceId = req.DeviceId?.Trim() ?? "";
        var name = req.DisplayName?.Trim() ?? "";
        if (deviceId.Length is < 8 or > 128)
            return Results.BadRequest(new { error = "deviceId must be 8-128 characters." });
        if (name.Length is < 1 or > 24)
            return Results.BadRequest(new { error = "displayName must be 1-24 characters." });

        var player = await db.Players.FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct);
        if (player is null)
        {
            player = new Player
            {
                DeviceId = deviceId, DisplayName = name,
                Rating = game.Value.StartRating, Rd = game.Value.StartRd, Volatility = game.Value.StartVolatility,
            };
            db.Players.Add(player);
        }
        else
        {
            player.DisplayName = name;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first-time logins for the same device raced; the winner's row is the one to use.
            db.Entry(player).State = EntityState.Detached;
            player = await db.Players.FirstAsync(p => p.DeviceId == deviceId, ct);
        }

        return Results.Ok(new
        {
            playerId = player.Id,
            displayName = player.DisplayName,
            rating = player.Rating,
            token = tokens.Create(player.Id, player.DisplayName),
        });
    }

    private static async Task<IResult> Me(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = PlayerId(user);
        var p = await db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null
            ? Results.NotFound()
            : Results.Ok(new { playerId = p.Id, displayName = p.DisplayName, rating = p.Rating, wins = p.Wins, losses = p.Losses });
    }

    private static async Task<IResult> MyMatches(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = PlayerId(user);
        var rows = await db.MatchPlayers.AsNoTracking()
            .Where(mp => mp.PlayerId == id && mp.Match.Status == MatchStatus.Finished)
            .OrderByDescending(mp => mp.Match.EndedAt)
            .Take(20)
            .Select(mp => new
            {
                gameId = mp.Match.GameId,
                endedAt = mp.Match.EndedAt,
                slot = mp.Slot,
                scoreLeft = mp.Match.ScoreLeft,
                scoreRight = mp.Match.ScoreRight,
                won = mp.IsWinner,
                ratingDelta = mp.RatingDelta,
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> Leaderboard(AppDbContext db, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 20, 1, 100);
        var rows = await db.Players.AsNoTracking()
            .OrderByDescending(p => p.Rating).ThenBy(p => p.CreatedAt)
            .Take(take)
            .Select(p => new { displayName = p.DisplayName, rating = p.Rating, wins = p.Wins, losses = p.Losses })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ReportResult(
        ReportResultRequest req, ClaimsPrincipal user, MatchService matches, CancellationToken ct)
    {
        var gameId = req.GameId?.Trim() ?? "";
        if (gameId.Length is < 1 or > 128)
            return Results.BadRequest(new { error = "gameId must be 1-128 characters." });

        var outcome = await matches.ReportAsync(gameId, PlayerId(user), req.Slot, req.ScoreLeft, req.ScoreRight, ct);
        var body = new { status = outcome.Status.ToString(), message = outcome.Message };
        return outcome.Status switch
        {
            ReportStatus.Invalid => Results.BadRequest(body),
            ReportStatus.SlotTaken or ReportStatus.AlreadyFinal => Results.Conflict(body),
            _ => Results.Ok(body),
        };
    }
}
