using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotonBackend.Api;
using PhotonBackend.Api.Auth;
using PhotonBackend.Api.Data;
using PhotonBackend.Api.Endpoints;
using PhotonBackend.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.Configure<PhotonOptions>(builder.Configuration.GetSection(PhotonOptions.Section));
builder.Services.Configure<GameOptions>(builder.Configuration.GetSection(GameOptions.Section));

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Secret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be set and at least 32 characters (env var Jwt__Secret).");

// Dev tanpa Postgres: Database:UseInMemory=true → DB in-memory (data hilang saat restart).
// Produksi/real: Npgsql. (Tes integrasi memakai InMemory lewat ApiFactory.)
var useInMemory = builder.Configuration.GetValue("Database:UseInMemory", false);
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (useInMemory)
        o.UseInMemoryDatabase("nexus-dev");
    else
        o.UseNpgsql(builder.Configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("ConnectionStrings:Default is not set."));
});

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.PasswordHasher<PhotonBackend.Api.Data.Player>>();
builder.Services.AddScoped<MatchService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenService(Options.Create(jwt)).ValidationParameters;
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) db.Database.Migrate();
    else db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapApi();
app.MapPhoton();

app.Run();

public partial class Program;
