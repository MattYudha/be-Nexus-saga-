namespace PhotonBackend.Api;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "photon-backend";
    public string Secret { get; set; } = "";
    public int LifetimeHours { get; set; } = 24;
}

public sealed class PhotonOptions
{
    public const string Section = "Photon";

    /// <summary>Shared secret Photon sends in the X-SecretKey header (dashboard key "WebHookSecret").</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Shared secret appended as ?secret= to the custom authentication URL in the dashboard.</summary>
    public string AuthSecret { get; set; } = "";
}

public sealed class GameOptions
{
    public const string Section = "Game";

    public int WinScore { get; set; } = 5;          // target KO (Nexus 1v1) / target poin (Pong)
    public int EloK { get; set; } = 32;             // lama (Elo) — tak dipakai sejak Glicko-2
    public int StartRating { get; set; } = 1500;    // konvensi Glicko-2
    public double StartRd { get; set; } = 350.0;    // RD awal (pemain baru = tak pasti)
    public double StartVolatility { get; set; } = 0.06;
    public double Tau { get; set; } = 0.5;          // konstanta sistem Glicko-2 (0.3–1.2)
    public int MatchmakingWindow { get; set; } = 400; // selisih rating maksimum untuk dipasangkan
}
