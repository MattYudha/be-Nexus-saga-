using PhotonBackend.Api.Services;

namespace PhotonBackend.Tests;

public class Glicko2Tests
{
    // Contoh kerja resmi dari paper Glickman "Example of the Glicko-2 system":
    // pemain (1500, 200, 0.06) vs 3 lawan → (1464.05, 151.52, 0.05999), tau=0.5.
    [Fact]
    public void Update_MatchesGlickmanWorkedExample()
    {
        var player = new Glicko2Rating(1500, 200, 0.06);
        var games = new[]
        {
            new Glicko2Game(1400, 30, 1),
            new Glicko2Game(1550, 100, 0),
            new Glicko2Game(1700, 300, 0),
        };

        var r = Glicko2.Update(player, games, tau: 0.5);

        Assert.Equal(1464.05, r.Rating, 0.5);
        Assert.Equal(151.52, r.Rd, 0.5);
        Assert.Equal(0.05999, r.Volatility, 1e-4);
    }

    [Fact]
    public void Update_NoGames_RdGrows_RatingStable()
    {
        var player = new Glicko2Rating(1500, 50, 0.06);
        var r = Glicko2.Update(player, Array.Empty<Glicko2Game>());

        Assert.Equal(1500, r.Rating, 1e-6);
        Assert.True(r.Rd > 50, "RD harus membesar saat tak ada game.");
        Assert.True(r.Rd <= Glicko2.DefaultRd);
        Assert.Equal(0.06, r.Volatility, 1e-9);
    }

    [Fact]
    public void Winner_Gains_Loser_Loses_Symmetric()
    {
        var a = new Glicko2Rating(1500, 200, 0.06);
        var b = new Glicko2Rating(1500, 200, 0.06);

        var aNew = Glicko2.Update(a, new[] { new Glicko2Game(1500, 200, 1) });
        var bNew = Glicko2.Update(b, new[] { new Glicko2Game(1500, 200, 0) });

        Assert.True(aNew.Rating > 1500);
        Assert.True(bNew.Rating < 1500);
        // simetris: kenaikan pemenang == penurunan pecundang saat identik.
        Assert.Equal(aNew.Rating - 1500, 1500 - bNew.Rating, 1e-6);
        // RD mengecil karena ada informasi game baru.
        Assert.True(aNew.Rd < 200);
    }
}
