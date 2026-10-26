using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Api.Services;

public enum ReportStatus
{
    Accepted,      // stored, waiting for the opponent's report
    Finished,      // both reports agree, match finalized
    Disputed,      // reports disagree, nothing is rated
    AlreadyFinal,  // match was already finished/disputed/closed
    Invalid,       // scores or slot are not a legal Pong result
    SlotTaken,     // another player already claimed this slot
}

public sealed record ReportOutcome(ReportStatus Status, string? Message = null);

/// <summary>
/// Records Pong results. Clients run a deterministic simulation, so both honest players
/// report identical scores. A result is only rated once both players agree.
/// </summary>
public sealed class MatchService(AppDbContext db, IOptions<GameOptions> options)
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly GameOptions _game = options.Value;

    // Single-instance guard: striped in-process locks keyed by game id. Scaling the API out to
    // several replicas needs a database advisory lock instead (see docs/ARCHITECTURE.md).
    private static SemaphoreSlim GateFor(string gameId) => Gates[(gameId.GetHashCode() & int.MaxValue) % Gates.Length];

    public async Task<Match> GetOrCreateMatchAsync(string gameId, string? region, CancellationToken ct)
    {
        var match = await db.Matches.Include(m => m.Players).FirstOrDefaultAsync(m => m.GameId == gameId, ct);
        if (match is not null) return match;

        match = new Match { GameId = gameId, Region = region ?? "" };
        db.Matches.Add(match);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another request creating the same room.
            db.Entry(match).State = EntityState.Detached;
            match = await db.Matches.Include(m => m.Players).FirstAsync(m => m.GameId == gameId, ct);
        }
        return match;
    }

    public async Task CloseAsync(string gameId, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.GameId == gameId, ct);
        if (match is null || match.Status != MatchStatus.Created) return;
        match.Status = MatchStatus.Closed;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReportOutcome> ReportAsync(
        string gameId, Guid playerId, int slot, int scoreLeft, int scoreRight, CancellationToken ct)
    {
        if (!IsLegalResult(slot, scoreLeft, scoreRight))
            return new ReportOutcome(ReportStatus.Invalid, "Not a legal Pong result.");

        var gate = GateFor(gameId);
        await gate.WaitAsync(ct);
        try
        {
            var match = await GetOrCreateMatchAsync(gameId, null, ct);
            if (match.Status != MatchStatus.Created)
                return new ReportOutcome(ReportStatus.AlreadyFinal);

            if (match.Players.Any(p => p.Slot == slot && p.PlayerId != playerId))
                return new ReportOutcome(ReportStatus.SlotTaken);

            var mine = match.Players.FirstOrDefault(p => p.PlayerId == playerId);
            if (mine is null)
            {
                if (match.Players.Count >= 2)
                    return new ReportOutcome(ReportStatus.SlotTaken, "Match already has two players.");
                mine = new MatchPlayer { MatchId = match.Id, PlayerId = playerId, Slot = slot };
                db.MatchPlayers.Add(mine); // EF fix-up also adds it to match.Players
            }
            mine.Slot = slot;
            mine.ReportedScoreLeft = scoreLeft;
            mine.ReportedScoreRight = scoreRight;

            var reported = match.Players.Where(p => p.ReportedScoreLeft.HasValue).ToList();
            if (reported.Count < 2)
            {
                await db.SaveChangesAsync(ct);
                return new ReportOutcome(ReportStatus.Accepted);
            }

            var agree = reported.All(p =>
                p.ReportedScoreLeft == reported[0].ReportedScoreLeft &&
                p.ReportedScoreRight == reported[0].ReportedScoreRight);
            if (!agree)
            {
                match.Status = MatchStatus.Disputed;
                match.EndedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return new ReportOutcome(ReportStatus.Disputed);
            }

            await FinalizeAsync(match, scoreLeft, scoreRight, ct);
            return new ReportOutcome(ReportStatus.Finished);
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsLegalResult(int slot, int left, int right) =>
        slot is 0 or 1 &&
        left >= 0 && right >= 0 &&
        Math.Max(left, right) == _game.WinScore &&
        Math.Min(left, right) < _game.WinScore;

    private async Task FinalizeAsync(Match match, int left, int right, CancellationToken ct)
    {
        var winnerSlot = left > right ? 0 : 1;
        var winner = match.Players.Single(p => p.Slot == winnerSlot);
        var loser = match.Players.Single(p => p.Slot != winnerSlot);

        var players = await db.Players
            .Where(p => p.Id == winner.PlayerId || p.Id == loser.PlayerId)
            .ToDictionaryAsync(p => p.Id, ct);
        var w = players[winner.PlayerId];
        var l = players[loser.PlayerId];

        // Glicko-2: hitung kedua pemain dari rating lawan PRA-match (tangkap dulu sebelum menulis balik).
        var wR = new Glicko2Rating(w.Rating, w.Rd, w.Volatility);
        var lR = new Glicko2Rating(l.Rating, l.Rd, l.Volatility);
        var wNew = Glicko2.Update(wR, new[] { new Glicko2Game(lR.Rating, lR.Rd, 1.0) }, _game.Tau);
        var lNew = Glicko2.Update(lR, new[] { new Glicko2Game(wR.Rating, wR.Rd, 0.0) }, _game.Tau);

        var wBefore = w.Rating;
        var lBefore = l.Rating;
        w.Rating = (int)Math.Round(wNew.Rating); w.Rd = wNew.Rd; w.Volatility = wNew.Volatility;
        l.Rating = Math.Max(0, (int)Math.Round(lNew.Rating)); l.Rd = lNew.Rd; l.Volatility = lNew.Volatility;
        w.Wins++;
        l.Losses++;

        winner.IsWinner = true;
        winner.RatingDelta = w.Rating - wBefore;
        loser.RatingDelta = l.Rating - lBefore;

        match.ScoreLeft = left;
        match.ScoreRight = right;
        match.Status = MatchStatus.Finished;
        match.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
