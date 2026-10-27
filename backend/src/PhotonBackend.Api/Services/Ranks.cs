using System;

namespace PhotonBackend.Api.Services;

/// <summary>
/// Pemetaan progres pemain (doc 24 §2.3 + doc 15 §3):
/// - Solo 1v1 ladder tier (dari Glicko-2 rating): Rookie→Nexus Legend (tema).
/// - Account Rank (dari accountXp/level): Trainee→Colonel (pangkat, nol efek kompetitif).
/// </summary>
public static class Ranks
{
    // Tier ladder Solo 1v1 (rating Glicko-2, center 1500).
    public static string TierFor(int rating) => rating switch
    {
        < 1200 => "Rookie",
        < 1400 => "Contender",
        < 1600 => "Rival",
        < 1800 => "Gladiator",
        < 2000 => "Conqueror",
        _ => "Nexus Legend",
    };

    // Account Rank (pangkat militer dari level akun).
    public static string AccountRankFor(int level) => level switch
    {
        < 5 => "Trainee",
        < 10 => "Private",
        < 20 => "Corporal",
        < 35 => "Sergeant",
        < 55 => "Lieutenant",
        < 80 => "Captain",
        < 110 => "Major",
        _ => "Colonel",
    };

    public const int XpPerLevel = 100;
    public static int LevelFor(int xp) => Math.Max(1, xp / XpPerLevel + 1);
}
