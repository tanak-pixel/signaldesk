using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("signaldesk_tests")
            .WithUsername("signaldesk_test")
            .WithPassword("LocalTestDatabase!123")
            .Build();

    private TestApiFactory? factory;

    public async Task InitializeAsync()
    {
        await database.StartAsync();

        factory = new TestApiFactory(
            database.GetConnectionString());

        // Start the test host, then prepare its isolated database.
        using var client = factory.CreateClient();
        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SignalDeskDbContext>();

        // These tests verify application behavior against the current model.
        // They do not verify migration history.
        await db.Database.EnsureCreatedAsync();

        var roles = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        var result = await roles.CreateAsync(
            new IdentityRole("Technician"));

        EnsureIdentitySucceeded(result);
    }

    public HttpClient NewClient()
    {
        return factory!.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
    }

    public async Task<HttpClient> CreateAccountAsync(
        bool technician = false)
    {
        var client = NewClient();
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var password = $"Test!Aa9-{Guid.NewGuid():N}";

        using var registration = await client.PostAsJsonAsync(
            "/auth/register",
            new { email, password });

        await ExpectSuccess(registration);

        if (technician)
        {
            await using var scope =
                factory!.Services.CreateAsyncScope();

            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var account = await users.FindByEmailAsync(email);

            if (account is null)
                throw new InvalidOperationException(
                    "Registered test account was not found.");

            var result = await users.AddToRoleAsync(
                account, "Technician");

            EnsureIdentitySucceeded(result);
        }

        // Login happens after role assignment so the token has the role.
        using var login = await client.PostAsJsonAsync(
            "/auth/login?useCookies=false",
            new { email, password });

        await ExpectSuccess(login);

        var body = await login.Content
            .ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                body.GetProperty("accessToken").GetString());

        return client;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (factory is not null)
                await factory.DisposeAsync();
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    public static async Task ExpectSuccess(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Expected success, got {(int)response.StatusCode}: {body}");
    }

    private static void EnsureIdentitySucceeded(
        IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(
                    "; ",
                    result.Errors.Select(error => error.Description)));
        }
    }

    private sealed class TestApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            // Program.cs currently permits Development only.
            builder.UseEnvironment("Development");

            builder.UseSetting(
                "ConnectionStrings:SignalDesk",
                connectionString);

            builder.UseSetting(
                "Development:TechnicianEmail",
                "");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:SignalDesk"] =
                            connectionString,
                        ["Development:TechnicianEmail"] = ""
                    });
            });

            builder.ConfigureServices(services =>
            {
                // Explicitly replace the application's database registration.
                services.RemoveAll<SignalDeskDbContext>();
                services.RemoveAll<
                    DbContextOptions<SignalDeskDbContext>>();
                services.RemoveAll<
                    IDbContextOptionsConfiguration<SignalDeskDbContext>>();

                services.AddDbContext<SignalDeskDbContext>(
                    options => options.UseNpgsql(connectionString));
            });
        }
    }
}