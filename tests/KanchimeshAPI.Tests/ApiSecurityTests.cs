using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KanchimeshAPI.Tests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task PasswordChange_InvalidatesThePreviousAccessToken()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await SeedTestUser(factory);
        var login = await Login(client, TestApiFactory.InitialPassword);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var changedResponse = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = TestApiFactory.InitialPassword,
            newPassword = "A-different-secure-password-456!",
        });
        changedResponse.EnsureSuccessStatusCode();
        var changed = await changedResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(changed);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task LoopbackCorsPreflight_IsAllowedOnlyWhenConfigured()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "http://localhost:57815");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:57815",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        await SeedTestUser(factory);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                emailOrPhone = TestApiFactory.Email,
                password = "incorrect-password",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/login", new
        {
            emailOrPhone = TestApiFactory.Email,
            password = "incorrect-password",
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private static async Task<LoginResponseDto> Login(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            emailOrPhone = TestApiFactory.Email,
            password,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    }

    private static async Task SeedTestUser(TestApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KanchimeshDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        database.ApplicationUsers.RemoveRange(await database.ApplicationUsers.ToListAsync());
        var user = new ApplicationUser
        {
            Email = TestApiFactory.Email,
            NormalizedEmail = TestApiFactory.Email.ToUpperInvariant(),
            DisplayName = "Integration Administrator",
            Role = ApplicationRoles.Administrator,
            IsActive = true,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, TestApiFactory.InitialPassword);
        database.ApplicationUsers.Add(user);
        await database.SaveChangesAsync();
    }
}

internal sealed class TestApiFactory : WebApplicationFactory<Program>
{
    public const string Email = "admin.integration@example.test";
    public const string InitialPassword = "Integration-test-password-123!";
    private readonly string databaseName = $"KanchimeshTests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["Database:InMemoryName"] = databaseName,
                ["BootstrapAdministrator:Email"] = Email,
                ["BootstrapAdministrator:DisplayName"] = "Integration Administrator",
                ["BootstrapAdministrator:InitialPassword"] = InitialPassword,
                ["Authentication:Jwt:Issuer"] = "KanchimeshAPI.Tests",
                ["Authentication:Jwt:Audience"] = "KanchimeshAPI.Tests.Client",
                ["Authentication:Jwt:SigningKey"] = "integration-tests-only-signing-key-with-more-than-32-bytes",
                ["Authentication:Jwt:AccessTokenLifetimeMinutes"] = "60",
                ["Cors:AllowedOrigins:0"] = "https://web.example.test",
                ["Cors:AllowLoopbackOrigins"] = "true",
            });
        });
    }
}
