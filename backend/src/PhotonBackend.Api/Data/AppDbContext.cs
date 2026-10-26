using Microsoft.EntityFrameworkCore;

namespace PhotonBackend.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Player>(e =>
        {
            e.HasIndex(x => x.DeviceId).IsUnique();
            e.HasIndex(x => x.Rating);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.Property(x => x.DisplayName).HasMaxLength(24);
        });

        b.Entity<Match>(e =>
        {
            e.HasIndex(x => x.GameId).IsUnique();
            e.Property(x => x.GameId).HasMaxLength(128);
            e.Property(x => x.Region).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
        });

        b.Entity<MatchPlayer>(e =>
        {
            e.HasKey(x => new { x.MatchId, x.PlayerId });
            e.HasIndex(x => new { x.MatchId, x.Slot }).IsUnique();
            e.HasOne(x => x.Match).WithMany(m => m.Players).HasForeignKey(x => x.MatchId);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId);
        });
    }
}
