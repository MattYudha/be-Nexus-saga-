namespace PhotonBackend.Api.Data;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Rating { get; set; }
    public double Rd { get; set; } = 350.0;        // Glicko-2 rating deviation (keyakinan)
    public double Volatility { get; set; } = 0.06; // Glicko-2 volatility
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class MatchStatus
{
    public const string Created = "Created";
    public const string Finished = "Finished";
    public const string Disputed = "Disputed";
    public const string Closed = "Closed";
}

public sealed class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Photon room name / game id. Unique per match.</summary>
    public string GameId { get; set; } = "";
    public string Region { get; set; } = "";
    public string Status { get; set; } = MatchStatus.Created;
    public int? ScoreLeft { get; set; }
    public int? ScoreRight { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    /// <summary>Raw body of the last Photon GameResult webhook, kept for auditing only.</summary>
    public string? PhotonResultRaw { get; set; }

    public List<MatchPlayer> Players { get; set; } = new();
}

public sealed class MatchPlayer
{
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>0 = left paddle, 1 = right paddle.</summary>
    public int Slot { get; set; }
    public int? ReportedScoreLeft { get; set; }
    public int? ReportedScoreRight { get; set; }
    public bool IsWinner { get; set; }
    public int RatingDelta { get; set; }
}
