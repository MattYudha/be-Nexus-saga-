using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotonBackend.Api.Data;

namespace PhotonBackend.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "test-webhook-secret";
    public const string AuthSecret = "test-auth-secret";

    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=unused");
        builder.UseSetting("Jwt:Secret", "test-secret-test-secret-test-secret-123");
        builder.UseSetting("Photon:WebhookSecret", WebhookSecret);
        builder.UseSetting("Photon:AuthSecret", AuthSecret);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    public record Login(Guid PlayerId, string DisplayName, int Rating, string Token);

    public async Task<(HttpClient Client, Login Login)> LoginAsync(string deviceId, string name)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/guest", new { deviceId, displayName = name });
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<Login>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, login);
    }
}
