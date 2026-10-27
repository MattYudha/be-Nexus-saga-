namespace PhotonBackend.Api.Services;

/// <summary>Pemetaan rating Glicko-2 → tier peringkat (doc 24: Rookie → Nexus Legend).</summary>
public static class Ranks
{
    public static string TierFor(int rating) => rating switch
    {
        < 1300 => "Rookie",
        < 1500 => "Bronze",
        < 1700 => "Silver",
        < 1900 => "Gold",
        < 2100 => "Platinum",
        < 2300 => "Diamond",
        < 2500 => "Master",
        _ => "Nexus Legend",
    };
}
