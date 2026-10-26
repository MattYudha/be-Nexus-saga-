using Microsoft.Extensions.Options;
using PhotonBackend.Api;
using PhotonBackend.Api.Services;

namespace PhotonBackend.Tests;

public class MatchmakingServiceTests
{
    private static MatchmakingService New(int window = 400) =>
        new(Options.Create(new GameOptions { MatchmakingWindow = window }));

    [Fact]
    public void TwoPlayersInWindow_Paired_OppositeSlots_SameGame()
    {
        var mm = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var ra = mm.Join(a, 1500, "asia");
        Assert.Equal(MatchmakingState.Searching, ra.State);

        var rb = mm.Join(b, 1520, "asia");
        Assert.Equal(MatchmakingState.Matched, rb.State);

        var sa = mm.Status(a); // yang menunggu belajar lewat status
        Assert.Equal(MatchmakingState.Matched, sa.State);
        Assert.Equal(rb.Assignment!.GameId, sa.Assignment!.GameId);
        Assert.Equal(0, sa.Assignment!.Slot); // yang menunggu = slot 0
        Assert.Equal(1, rb.Assignment!.Slot); // pendatang = slot 1
        Assert.Equal(a, rb.Assignment!.OpponentId);
        Assert.Equal(b, sa.Assignment!.OpponentId);
    }

    [Fact]
    public void OutOfWindow_NotPaired()
    {
        var mm = New(window: 100);
        mm.Join(Guid.NewGuid(), 1500, "asia");
        var rb = mm.Join(Guid.NewGuid(), 2000, "asia");
        Assert.Equal(MatchmakingState.Searching, rb.State);
    }

    [Fact]
    public void DifferentRegion_NotPaired()
    {
        var mm = New();
        mm.Join(Guid.NewGuid(), 1500, "asia");
        var rb = mm.Join(Guid.NewGuid(), 1500, "eu");
        Assert.Equal(MatchmakingState.Searching, rb.State);
    }

    [Fact]
    public void ClosestRating_IsChosen()
    {
        // window 250: a & b di LUAR window satu sama lain (|1350-1700|=350) → tak saling pasang,
        // jadi keduanya menunggu. c(1500) dalam window keduanya → pilih yang terdekat (a, selisih 150).
        var mm = New(window: 250);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.Equal(MatchmakingState.Searching, mm.Join(a, 1350, "asia").State);
        Assert.Equal(MatchmakingState.Searching, mm.Join(b, 1700, "asia").State);
        var rc = mm.Join(c, 1500, "asia");

        Assert.Equal(MatchmakingState.Matched, rc.State);
        Assert.Equal(a, rc.Assignment!.OpponentId);          // a (selisih 150) lebih dekat dari b (200)
        Assert.Equal(MatchmakingState.Searching, mm.Status(b).State); // b masih menunggu
    }

    [Fact]
    public void Leave_RemovesFromQueue()
    {
        var mm = New();
        var a = Guid.NewGuid();
        mm.Join(a, 1500, "asia");
        mm.Leave(a);
        Assert.Equal(MatchmakingState.Idle, mm.Status(a).State);
    }
}
