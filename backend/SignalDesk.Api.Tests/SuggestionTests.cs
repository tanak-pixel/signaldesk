using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public sealed class SuggestionTests(ApiFixture fixture)
    : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Matching_location_and_time_produce_strong_match()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var location = $"STEM {Guid.NewGuid():N}";

        var anchor = await CreateReport(
            technician, location);

        var candidate = await CreateReport(
            technician, $"  {location.ToLowerInvariant()}   ");

        var incident = await CreateIncident(technician);

        await Link(
            technician,
            Id(incident),
            Id(anchor),
            incidentVersion: 1);

        var result = await GetSuggestions(
            technician, Id(incident));

        var suggestion = result
            .GetProperty("suggestions")
            .EnumerateArray()
            .Single(item => Id(item) == Id(candidate));

        Assert.Equal(
            "Strong",
            suggestion.GetProperty("match").GetString());

        Assert.Contains(
            suggestion.GetProperty("reasons").EnumerateArray(),
            reason => reason.GetString() ==
                "Same location as a nearby linked report.");

        Assert.Equal(
            2,
            result.GetProperty("incidentVersion").GetInt32());
    }

    [Fact]
    public async Task Different_location_produces_possible_match()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var anchor = await CreateReport(
            technician, $"Anchor {Guid.NewGuid():N}");

        var candidate = await CreateReport(
            technician, $"Different {Guid.NewGuid():N}");

        var incident = await CreateIncident(technician);

        await Link(
            technician,
            Id(incident),
            Id(anchor),
            incidentVersion: 1);

        var result = await GetSuggestions(
            technician, Id(incident));

        var suggestion = result
            .GetProperty("suggestions")
            .EnumerateArray()
            .Single(item => Id(item) == Id(candidate));

        Assert.Equal(
            "Possible",
            suggestion.GetProperty("match").GetString());

        Assert.DoesNotContain(
            suggestion.GetProperty("reasons").EnumerateArray(),
            reason => reason.GetString() ==
                "Same location as a nearby linked report.");
    }

    [Fact]
    public async Task Wrong_service_resolved_and_linked_reports_are_excluded()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var location = $"STEM {Guid.NewGuid():N}";
        var incident = await CreateIncident(technician);

        var wrongService = await CreateReport(
            technician, location, service: "Email");

        var resolved = await CreateReport(
            technician, location);

        var alreadyLinked = await CreateReport(
            technician, location);

        using var status = await technician.PatchAsJsonAsync(
            $"/api/reports/{Id(resolved)}/status",
            new { status = "Resolved", version = 1 });

        await ApiFixture.ExpectSuccess(status);

        await Link(
            technician,
            Id(incident),
            Id(alreadyLinked),
            incidentVersion: 1);

        var result = await GetSuggestions(
            technician, Id(incident));

        var suggestions = result.GetProperty("suggestions");

        Assert.False(ContainsId(suggestions, Id(wrongService)));
        Assert.False(ContainsId(suggestions, Id(resolved)));
        Assert.False(ContainsId(suggestions, Id(alreadyLinked)));
    }

    [Fact]
    public async Task Reports_outside_time_window_are_excluded()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var location = $"STEM {Guid.NewGuid():N}";
        var incident = await CreateIncident(technician);

        var anchor = await CreateReport(technician, location);
        var oldReport = await CreateReport(technician, location);
        var futureReport = await CreateReport(technician, location);
        var nearbyReport = await CreateReport(technician, location);

        var anchorTime = anchor
            .GetProperty("createdAt")
            .GetDateTimeOffset();

        await fixture.SetReportCreatedAtAsync(
            Id(oldReport), anchorTime.AddHours(-3));

        await fixture.SetReportCreatedAtAsync(
            Id(futureReport), anchorTime.AddHours(3));

        await Link(
            technician,
            Id(incident),
            Id(anchor),
            incidentVersion: 1);

        var result = await GetSuggestions(
            technician, Id(incident));

        var suggestions = result.GetProperty("suggestions");

        Assert.False(ContainsId(suggestions, Id(oldReport)));
        Assert.False(ContainsId(suggestions, Id(futureReport)));
        Assert.True(ContainsId(suggestions, Id(nearbyReport)));
    }

    [Fact]
    public async Task Suggestion_access_requires_technician_role()
    {
        using var anonymous = fixture.NewClient();
        using var reporter = await fixture.CreateAccountAsync();

        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var incident = await CreateIncident(technician);
        var path = $"/api/incidents/{Id(incident)}/suggestions";

        using var anonymousResponse =
            await anonymous.GetAsync(path);

        using var reporterResponse =
            await reporter.GetAsync(path);

        using var technicianResponse =
            await technician.GetAsync(path);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            anonymousResponse.StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            reporterResponse.StatusCode);

        await ApiFixture.ExpectSuccess(technicianResponse);
    }

    [Fact]
    public async Task Approval_removes_suggestion_and_records_history()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var candidate = await CreateReport(
            technician, $"STEM {Guid.NewGuid():N}");

        var incident = await CreateIncident(technician);
        var incidentId = Id(incident);

        // With no linked reports, incident creation is the time anchor.
        var before = await GetSuggestions(
            technician, incidentId);

        var suggestion = before
            .GetProperty("suggestions")
            .EnumerateArray()
            .Single(item => Id(item) == Id(candidate));

        Assert.Equal(
            "Possible",
            suggestion.GetProperty("match").GetString());

        // Reading suggestions must not link anything.
        var detailsBefore = await Get(
            technician, $"/api/incidents/{incidentId}");

        Assert.Empty(
            detailsBefore.GetProperty("reports").EnumerateArray());

        using var approval = await technician.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/reports/{Id(candidate)}",
            new
            {
                reportVersion =
                    suggestion.GetProperty("version").GetInt32(),
                incidentVersion =
                    before.GetProperty("incidentVersion").GetInt32()
            });

        await ApiFixture.ExpectSuccess(approval);

        var after = await GetSuggestions(
            technician, incidentId);

        Assert.False(
            ContainsId(
                after.GetProperty("suggestions"),
                Id(candidate)));

        var detailsAfter = await Get(
            technician, $"/api/incidents/{incidentId}");

        Assert.True(
            ContainsId(
                detailsAfter.GetProperty("reports"),
                Id(candidate)));

        var history = await Get(
            technician,
            $"/api/incidents/{incidentId}/activity");

        Assert.Single(
            history.EnumerateArray(),
            entry => entry.GetProperty("action").GetString() ==
                "ReportLinked");

        // Resolved incidents return no suggestions.
        using var resolved = await technician.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status",
            new { status = "Resolved", version = 2 });

        await ApiFixture.ExpectSuccess(resolved);

        var resolvedSuggestions = await GetSuggestions(
            technician, incidentId);

        Assert.Empty(
            resolvedSuggestions
                .GetProperty("suggestions")
                .EnumerateArray());
    }

    private static async Task<JsonElement> CreateReport(
        HttpClient client,
        string location,
        string service = "Wi-Fi")
    {
        using var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                title = $"Service issue {Guid.NewGuid():N}",
                description = "Repeated service interruptions reported.",
                service,
                location
            });

        await ApiFixture.ExpectSuccess(response);

        return await response.Content
            .ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> CreateIncident(
        HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title = $"Network outage {Guid.NewGuid():N}",
                description = "Investigating repeated network disconnections.",
                service = "Wi-Fi"
            });

        await ApiFixture.ExpectSuccess(response);

        return await response.Content
            .ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Link(
        HttpClient client,
        Guid incidentId,
        Guid reportId,
        int incidentVersion)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/reports/{reportId}",
            new
            {
                reportVersion = 1,
                incidentVersion
            });

        await ApiFixture.ExpectSuccess(response);
    }

    private static Task<JsonElement> GetSuggestions(
        HttpClient client,
        Guid incidentId)
    {
        return Get(
            client,
            $"/api/incidents/{incidentId}/suggestions");
    }

    private static async Task<JsonElement> Get(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);

        await ApiFixture.ExpectSuccess(response);

        return await response.Content
            .ReadFromJsonAsync<JsonElement>();
    }

    private static Guid Id(JsonElement item) =>
        item.GetProperty("id").GetGuid();

    private static bool ContainsId(JsonElement items, Guid id) =>
        items.EnumerateArray().Any(item => Id(item) == id);
}