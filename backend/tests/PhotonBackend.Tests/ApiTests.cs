using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PhotonBackend.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GuestLogin_RejectsShortDeviceId()
    {
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/guest", new { deviceId = "x", displayName = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task GuestLogin_IsIdempotentPerDevice()
    {
        var (_, a) = await factory.LoginAsync("device-idempotent-1", "Alice");
        var (_, b) = await factory.LoginAsync("device-idempotent-1", "Alice2");
        Assert.Equal(a.PlayerId, b.PlayerId);
        Assert.Equal("Alice2", b.DisplayName);
    }

    [Fact]
    public async Task Me_RequiresToken()
    {
        var res = await factory.CreateClient().GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task PhotonAuth_AcceptsValidToken()
    {
        var (_, login) = await factory.LoginAsync("device-photon-auth", "Bob");
        var res = await factory.CreateClient()
            .GetAsync($"/photon/auth?secret={ApiFactory.AuthSecret}&token={Uri.EscapeDataString(login.Token)}");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("ResultCode").GetInt32());
        Assert.Equal(login.PlayerId.ToString(), json.GetProperty("UserId").GetString());
        Assert.Equal("Bob", json.GetProperty("Nickname").GetString());
    }

    [Theory]
    [InlineData("secret=wrong&token=abc", 2)]
    [InlineData("secret=test-auth-secret&token=garbage", 2)]
    [InlineData("secret=test-auth-secret", 3)]
    public async Task PhotonAuth_RejectsBadRequests(string query, int expectedCode)
    {
        var res = await factory.CreateClient().GetAsync($"/photon/auth?{query}");
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, json.GetProperty("ResultCode").GetInt32());
    }

    [Fact]
    public async Task Webhook_RequiresSecret()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/photon/webhooks/game/create", new { GameId = "room-1" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        client.DefaultRequestHeaders.Add("X-SecretKey", ApiFactory.WebhookSecret);
        res = await client.PostAsJsonAsync("/photon/webhooks/game/create", new { GameId = "room-1" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task MatchResult_BothPlayersAgree_UpdatesRatingsAndLeaderboard()
    {
        var (alice, a) = await factory.LoginAsync("device-match-alice", "MatchAlice");
        var (bob, b) = await factory.LoginAsync("device-match-bob", "MatchBob");

        var r1 = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 0, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal("Accepted", (await r1.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 1, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var meA = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        var meB = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        // Glicko-2: pemenang naik, pecundang turun; dari start identik perubahannya simetris (±1 pembulatan).
        var ratingA = meA.GetProperty("rating").GetInt32();
        var ratingB = meB.GetProperty("rating").GetInt32();
        Assert.True(ratingA > 1500, $"winner should rise above start, got {ratingA}");
        Assert.True(ratingB < 1500, $"loser should fall below start, got {ratingB}");
        Assert.True(Math.Abs((ratingA - 1500) - (1500 - ratingB)) <= 1, "symmetric swing from identical start");
        Assert.Equal(1, meA.GetProperty("wins").GetInt32());
        Assert.Equal(1, meB.GetProperty("losses").GetInt32());

        var board = await alice.GetFromJsonAsync<JsonElement>("/api/leaderboard?limit=50");
        var names = board.EnumerateArray().Select(x => x.GetProperty("displayName").GetString()).ToList();
        Assert.True(names.IndexOf("MatchAlice") < names.IndexOf("MatchBob"));

        // Reporting again after finalization is rejected, so ratings cannot be farmed.
        var again = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-agree", slot = 0, scoreLeft = 5, scoreRight = 2 });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var history = await alice.GetFromJsonAsync<JsonElement>("/api/matches");
        Assert.Equal(1, history.GetArrayLength());
        Assert.True(history[0].GetProperty("won").GetBoolean());
    }

    [Fact]
    public async Task MatchResult_Disagreement_IsDisputedAndNotRated()
    {
        var (alice, _) = await factory.LoginAsync("device-dispute-alice", "DispAlice");
        var (bob, _) = await factory.LoginAsync("device-dispute-bob", "DispBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-dispute", slot = 0, scoreLeft = 5, scoreRight = 0 });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-dispute", slot = 1, scoreLeft = 0, scoreRight = 5 });
        Assert.Equal("Disputed", (await res.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var me = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1500, me.GetProperty("rating").GetInt32());
    }

    [Theory]
    [InlineData(3, 2)]  // nobody reached the win score
    [InlineData(5, 5)]  // tie
    [InlineData(6, 0)]  // above win score
    [InlineData(-1, 5)] // negative
    public async Task MatchResult_RejectsIllegalScores(int left, int right)
    {
        var (alice, _) = await factory.LoginAsync("device-illegal-alice", "IllAlice");
        var res = await alice.PostAsJsonAsync("/api/matches/result", new { gameId = $"room-illegal-{left}-{right}", slot = 0, scoreLeft = left, scoreRight = right });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task MatchResult_SameSlotCannotBeClaimedTwice()
    {
        var (alice, _) = await factory.LoginAsync("device-slot-alice", "SlotAlice");
        var (bob, _) = await factory.LoginAsync("device-slot-bob", "SlotBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-slot", slot = 0, scoreLeft = 5, scoreRight = 1 });
        var res = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-slot", slot = 0, scoreLeft = 5, scoreRight = 1 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Matchmaking_PairsTwoPlayers_SameGameOppositeSlots()
    {
        var (alice, _) = await factory.LoginAsync("device-mm-alice", "MmAlice");
        var (bob, _) = await factory.LoginAsync("device-mm-bob", "MmBob");

        var r1 = await alice.PostAsJsonAsync("/api/matchmaking/join", new { region = "asia" });
        Assert.Equal("Searching", (await r1.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("state").GetString());

        var r2 = await bob.PostAsJsonAsync("/api/matchmaking/join", new { region = "asia" });
        var m2 = await r2.Content.ReadFromJsonAsync<JsonElement>(Web);
        Assert.Equal("Matched", m2.GetProperty("state").GetString());
        var gameId = m2.GetProperty("gameId").GetString();
        Assert.False(string.IsNullOrEmpty(gameId));

        var s1 = await alice.GetFromJsonAsync<JsonElement>("/api/matchmaking/status");
        Assert.Equal("Matched", s1.GetProperty("state").GetString());
        Assert.Equal(gameId, s1.GetProperty("gameId").GetString());
        Assert.NotEqual(s1.GetProperty("slot").GetInt32(), m2.GetProperty("slot").GetInt32());
    }

    private async Task<HttpClient> RegisterAsync(string username, string password, string? name = null)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new { username, password, displayName = name ?? username });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(Web);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }

    [Fact]
    public async Task Register_Then_Me_ReturnsProfileWithRankTier()
    {
        var client = await RegisterAsync("acct_alice", "secret123", "AliceA");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("acct_alice", me.GetProperty("username").GetString());
        Assert.Equal("AliceA", me.GetProperty("displayName").GetString());
        Assert.Equal("Rival", me.GetProperty("rankTier").GetString());       // start 1500 = Rival (doc 24)
        Assert.Equal("Trainee", me.GetProperty("accountRank").GetString());  // 0 XP
        Assert.Equal(0, me.GetProperty("matchesPlayed").GetInt32());
        Assert.Equal(0, me.GetProperty("kills").GetInt32());
        Assert.Equal(0, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task Register_DuplicateUsername_Conflict()
    {
        await RegisterAsync("dup_user", "secret123");
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { username = "dup_user", password = "another123", displayName = "X" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Login_CorrectAndWrongPassword()
    {
        await RegisterAsync("login_bob", "rightpass1");
        var client = factory.CreateClient();
        var ok = await client.PostAsJsonAsync("/api/auth/login", new { username = "login_bob", password = "rightpass1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var bad = await client.PostAsJsonAsync("/api/auth/login", new { username = "login_bob", password = "wrongpass" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_BadRequest()
    {
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { username = "shortpw_user", password = "123", displayName = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SetFaction_UpdatesProfile()
    {
        var client = await RegisterAsync("faction_user", "secret123");
        var res = await client.PostAsJsonAsync("/api/me/faction", new { faction = 1 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(1, me.GetProperty("faction").GetInt32());
    }

    [Fact]
    public async Task MatchReport_AccumulatesKillsDeathsKdaAndXp()
    {
        var (alice, _) = await factory.LoginAsync("device-stat-alice", "StatAlice");
        var (bob, _) = await factory.LoginAsync("device-stat-bob", "StatBob");

        await alice.PostAsJsonAsync("/api/matches/result", new { gameId = "room-stat", slot = 0, scoreLeft = 5, scoreRight = 2, kills = 5, deaths = 2, assists = 1 });
        var r2 = await bob.PostAsJsonAsync("/api/matches/result", new { gameId = "room-stat", slot = 1, scoreLeft = 5, scoreRight = 2, kills = 2, deaths = 5, assists = 0 });
        Assert.Equal("Finished", (await r2.Content.ReadFromJsonAsync<JsonElement>(Web)).GetProperty("status").GetString());

        var meA = await alice.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(5, meA.GetProperty("kills").GetInt32());
        Assert.Equal(2, meA.GetProperty("deaths").GetInt32());
        Assert.Equal(1, meA.GetProperty("wins").GetInt32());
        Assert.Equal(3.0, meA.GetProperty("kda").GetDouble());          // (5+1)/2
        Assert.True(meA.GetProperty("accountXp").GetInt32() >= 100);    // menang → +100

        var meB = await bob.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(2, meB.GetProperty("kills").GetInt32());
        Assert.Equal(5, meB.GetProperty("deaths").GetInt32());
    }

    [Fact]
    public async Task Health_IsOk()
    {
        var res = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
