using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public sealed class PermissionTests(ApiFixture fixture)
    : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Anonymous_users_cannot_access_protected_routes()
    {
        using var anonymous = fixture.NewClient();

        using var reports =
            await anonymous.GetAsync("/api/reports");

        using var incidents =
            await anonymous.GetAsync("/api/incidents");

        Assert.Equal(HttpStatusCode.Unauthorized, reports.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, incidents.StatusCode);
    }

    [Fact]
    public async Task Reporters_see_only_their_own_reports()
    {
        using var first = await fixture.CreateAccountAsync();
        using var second = await fixture.CreateAccountAsync();
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var firstReport = await CreateReport(first);
        var secondReport = await CreateReport(second);

        var firstList = await Get(first, "/api/reports");
        var secondList = await Get(second, "/api/reports");
        var technicianList =
            await Get(technician, "/api/reports");

        Assert.Single(firstList.EnumerateArray());
        Assert.Single(secondList.EnumerateArray());

        Assert.True(ContainsId(firstList, Id(firstReport)));
        Assert.False(ContainsId(firstList, Id(secondReport)));

        Assert.True(ContainsId(secondList, Id(secondReport)));
        Assert.False(ContainsId(secondList, Id(firstReport)));

        Assert.True(ContainsId(technicianList, Id(firstReport)));
        Assert.True(ContainsId(technicianList, Id(secondReport)));
    }

    [Fact]
    public async Task Reporters_cannot_manage_incidents_or_report_status()
    {
        using var reporter = await fixture.CreateAccountAsync();
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var report = await CreateReport(reporter);
        var incident = await CreateIncident(technician);
        var incidentId = Id(incident);

        var historyBefore =
            await GetHistory(technician, incidentId);

        using var list =
            await reporter.GetAsync("/api/incidents");

        using var create = await reporter.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title = "Unauthorized incident",
                description = "This request should be rejected.",
                service = "Wi-Fi"
            });

        using var reportStatus = await reporter.PatchAsJsonAsync(
            $"/api/reports/{Id(report)}/status",
            new { status = "Resolved", version = 1 });

        using var incidentStatus = await reporter.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status",
            new { status = "Investigating", version = 1 });

        using var assign = await reporter.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/assign-to-me",
            new { version = 1 });

        using var release = await reporter.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/release-assignment",
            new { version = 1 });

        using var link = await reporter.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/reports/{Id(report)}",
            new { reportVersion = 1, incidentVersion = 1 });

        using var history =
            await reporter.GetAsync(
                $"/api/incidents/{incidentId}/activity");

        foreach (var response in new[]
        {
            list, create, reportStatus, incidentStatus,
            assign, release, link, history
        })
        {
            Assert.Equal(
                HttpStatusCode.Forbidden,
                response.StatusCode);
        }

        var historyAfter =
            await GetHistory(technician, incidentId);

        Assert.Equal(historyBefore.Length, historyAfter.Length);

        var reportList = await Get(reporter, "/api/reports");
        var unchangedReport = reportList.EnumerateArray().Single();

        Assert.Equal(
            "New",
            unchangedReport.GetProperty("status").GetString());

        Assert.Equal(
            1,
            unchangedReport.GetProperty("version").GetInt32());

        Assert.Equal(
            JsonValueKind.Null,
            unchangedReport.GetProperty("incidentId").ValueKind);
    }

    [Fact]
    public async Task Only_the_owner_can_release_assignment()
    {
        using var owner =
            await fixture.CreateAccountAsync(technician: true);

        using var other =
            await fixture.CreateAccountAsync(technician: true);

        var incident = await CreateIncident(owner);
        var id = Id(incident);

        using var assignment = await owner.PatchAsJsonAsync(
            $"/api/incidents/{id}/assign-to-me",
            new { version = 1 });

        await ApiFixture.ExpectSuccess(assignment);

        var before = await GetHistory(owner, id);

        using var rejected = await other.PatchAsJsonAsync(
            $"/api/incidents/{id}/release-assignment",
            new { version = 2 });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            rejected.StatusCode);

        var afterRejection = await GetHistory(owner, id);
        Assert.Equal(before.Length, afterRejection.Length);

        var ownerList = await Get(owner, "/api/incidents");
        var ownerView = Find(ownerList, id);

        Assert.True(
            ownerView.GetProperty("isAssignedToMe").GetBoolean());

        Assert.False(string.IsNullOrWhiteSpace(
            ownerView.GetProperty("assignedTechnicianName")
                .GetString()));

        var otherList = await Get(other, "/api/incidents");

        Assert.False(
            Find(otherList, id)
                .GetProperty("isAssignedToMe")
                .GetBoolean());

        using var released = await owner.PatchAsJsonAsync(
            $"/api/incidents/{id}/release-assignment",
            new { version = 2 });

        await ApiFixture.ExpectSuccess(released);

        var releasedBody = await released.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            JsonValueKind.Null,
            releasedBody.GetProperty("assignedTechnicianId")
                .ValueKind);

        Assert.Equal(
            3,
            releasedBody.GetProperty("version").GetInt32());

        var history = await GetHistory(owner, id);

        Assert.Single(
            history,
            entry => entry.GetProperty("action").GetString() ==
                "AssignmentReleased");
    }

    [Fact]
    public async Task Stale_updates_and_links_do_not_create_history()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var incident = await CreateIncident(technician);
        var report = await CreateReport(technician);
        var id = Id(incident);

        using var update = await technician.PatchAsJsonAsync(
            $"/api/incidents/{id}/status",
            new { status = "Investigating", version = 1 });

        await ApiFixture.ExpectSuccess(update);

        var before = await GetHistory(technician, id);

        using var staleUpdate = await technician.PatchAsJsonAsync(
            $"/api/incidents/{id}/status",
            new { status = "Resolved", version = 1 });

        using var staleLink = await technician.PostAsJsonAsync(
            $"/api/incidents/{id}/reports/{Id(report)}",
            new { reportVersion = 1, incidentVersion = 1 });

        Assert.Equal(
            HttpStatusCode.Conflict,
            staleUpdate.StatusCode);

        Assert.Equal(
            HttpStatusCode.Conflict,
            staleLink.StatusCode);

        var after = await GetHistory(technician, id);
        Assert.Equal(before.Length, after.Length);

        var details = await Get(
            technician, $"/api/incidents/{id}");

        Assert.Equal(
            "Investigating",
            details.GetProperty("incident")
                .GetProperty("status").GetString());

        Assert.Empty(
            details.GetProperty("reports").EnumerateArray());

        // Report status also rejects stale versions.
        using var reportUpdate = await technician.PatchAsJsonAsync(
            $"/api/reports/{Id(report)}/status",
            new { status = "Investigating", version = 1 });

        await ApiFixture.ExpectSuccess(reportUpdate);

        using var staleReport = await technician.PatchAsJsonAsync(
            $"/api/reports/{Id(report)}/status",
            new { status = "Resolved", version = 1 });

        Assert.Equal(
            HttpStatusCode.Conflict,
            staleReport.StatusCode);
    }

    [Fact]
    public async Task Concurrent_updates_allow_only_one_winner()
    {
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var incident = await CreateIncident(technician);
        var id = Id(incident);

        var responses = await Task.WhenAll(
            technician.PatchAsJsonAsync(
                $"/api/incidents/{id}/status",
                new { status = "Investigating", version = 1 }),
            technician.PatchAsJsonAsync(
                $"/api/incidents/{id}/status",
                new { status = "Resolved", version = 1 }));

        try
        {
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.OK);

            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Conflict);

            var history = await GetHistory(technician, id);

            Assert.Single(
                history,
                entry => entry.GetProperty("action").GetString() ==
                    "StatusChanged");

            var details = await Get(
                technician, $"/api/incidents/{id}");

            Assert.Equal(
                2,
                details.GetProperty("incident")
                    .GetProperty("version").GetInt32());
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Linking_exposes_only_public_incident_summary_to_reporter()
    {
        using var reporter = await fixture.CreateAccountAsync();
        using var technician =
            await fixture.CreateAccountAsync(technician: true);

        var report = await CreateReport(reporter);
        var incident = await CreateIncident(technician);
        var id = Id(incident);

        using var linked = await technician.PostAsJsonAsync(
            $"/api/incidents/{id}/reports/{Id(report)}",
            new { reportVersion = 1, incidentVersion = 1 });

        await ApiFixture.ExpectSuccess(linked);

        using var resolved = await technician.PatchAsJsonAsync(
            $"/api/incidents/{id}/status",
            new { status = "Resolved", version = 2 });

        await ApiFixture.ExpectSuccess(resolved);

        var reports = await Get(reporter, "/api/reports");
        var ownReport = reports.EnumerateArray().Single();
        var summary = ownReport.GetProperty("linkedIncident");

        Assert.Equal(
            incident.GetProperty("title").GetString(),
            summary.GetProperty("title").GetString());

        Assert.Equal(
            "Resolved",
            summary.GetProperty("status").GetString());

        Assert.Equal(2, summary.EnumerateObject().Count());

        // Linking and resolving an incident preserve the report's status.
        Assert.Equal(
            "New",
            ownReport.GetProperty("status").GetString());

        var history = await GetHistory(technician, id);

        Assert.Single(
            history,
            entry => entry.GetProperty("action").GetString() ==
                "Created");

        Assert.Single(
            history,
            entry => entry.GetProperty("action").GetString() ==
                "ReportLinked");

        Assert.Single(
            history,
            entry => entry.GetProperty("action").GetString() ==
                "StatusChanged");

        Assert.All(history, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                entry.GetProperty("actorName").GetString()));

            Assert.NotEqual(
                default(DateTimeOffset),
                entry.GetProperty("createdAt").GetDateTimeOffset());
        });
    }

    private static async Task<JsonElement> CreateReport(
        HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/reports",
            new
            {
                title = $"Wi-Fi issue {Guid.NewGuid():N}",
                description = "The network repeatedly disconnects.",
                service = "Wi-Fi",
                location = "STEM 212"
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
                description = "Investigating repeated Wi-Fi disconnections.",
                service = "Wi-Fi"
            });

        await ApiFixture.ExpectSuccess(response);

        return await response.Content
            .ReadFromJsonAsync<JsonElement>();
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

    private static async Task<JsonElement[]> GetHistory(
        HttpClient client,
        Guid incidentId)
    {
        var result = await Get(
            client,
            $"/api/incidents/{incidentId}/activity");

        return result.EnumerateArray().ToArray();
    }

    private static Guid Id(JsonElement item) =>
        item.GetProperty("id").GetGuid();

    private static bool ContainsId(JsonElement items, Guid id) =>
        items.EnumerateArray().Any(item => Id(item) == id);

    private static JsonElement Find(JsonElement items, Guid id) =>
        items.EnumerateArray().Single(item => Id(item) == id);
}