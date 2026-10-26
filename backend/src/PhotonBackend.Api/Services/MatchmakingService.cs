using Microsoft.Extensions.Options;

namespace PhotonBackend.Api.Services;

public enum MatchmakingState { Idle, Searching, Matched }

public sealed record MatchAssignment(string GameId, int Slot, string Region, Guid OpponentId);
public sealed record MatchmakingResult(MatchmakingState State, MatchAssignment? Assignment);

/// <summary>
/// Antrean matchmaking MMR untuk Solo 1v1 (in-memory, singleton). Memasangkan pemain dengan
/// rating terdekat dalam <see cref="GameOptions.MatchmakingWindow"/> pada region yang sama,
/// lalu memberi <c>gameId</c> (nama room Photon) bersama + slot (0/1). Match row dibuat lazy
/// saat hasil dilapor (<see cref="MatchService"/>), jadi service ini tak butuh DB.
///
/// Catatan skala: state in-process → valid untuk satu instance API. Beberapa replika butuh
/// store bersama (Redis) — lihat MatchService untuk pola lock yang sama.
/// </summary>
public sealed class MatchmakingService(IOptions<GameOptions> options)
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, (int Rating, string Region, DateTime At)> _waiting = new();
    private readonly Dictionary<Guid, MatchAssignment> _assignments = new();
    private readonly int _window = options.Value.MatchmakingWindow;

    public MatchmakingResult Join(Guid playerId, int rating, string region)
    {
        lock (_lock)
        {
            if (_assignments.TryGetValue(playerId, out var already))
                return new MatchmakingResult(MatchmakingState.Matched, already);

            // Lawan menunggu terbaik: region sama, selisih rating terkecil dalam window.
            Guid bestId = default; int bestDiff = int.MaxValue; bool found = false;
            foreach (var (id, w) in _waiting)
            {
                if (id == playerId) continue;
                if (!string.Equals(w.Region, region, StringComparison.OrdinalIgnoreCase)) continue;
                int diff = Math.Abs(w.Rating - rating);
                if (diff <= _window && diff < bestDiff) { bestId = id; bestDiff = diff; found = true; }
            }

            if (found)
            {
                _waiting.Remove(bestId);
                _waiting.Remove(playerId);
                var gameId = "mm-" + Guid.NewGuid().ToString("N")[..12];
                _assignments[bestId] = new MatchAssignment(gameId, 0, region, playerId);   // yang menunggu = slot 0
                _assignments[playerId] = new MatchAssignment(gameId, 1, region, bestId);    // pendatang = slot 1
                return new MatchmakingResult(MatchmakingState.Matched, _assignments[playerId]);
            }

            _waiting[playerId] = (rating, region, DateTime.UtcNow); // overwrite = perbarui rating/region
            return new MatchmakingResult(MatchmakingState.Searching, null);
        }
    }

    public MatchmakingResult Status(Guid playerId)
    {
        lock (_lock)
        {
            if (_assignments.TryGetValue(playerId, out var a)) return new MatchmakingResult(MatchmakingState.Matched, a);
            if (_waiting.ContainsKey(playerId)) return new MatchmakingResult(MatchmakingState.Searching, null);
            return new MatchmakingResult(MatchmakingState.Idle, null);
        }
    }

    /// <summary>Keluar antre / lepas assignment (mis. sudah connect atau batal).</summary>
    public void Leave(Guid playerId)
    {
        lock (_lock)
        {
            _waiting.Remove(playerId);
            _assignments.Remove(playerId);
        }
    }
}
