using Microsoft.EntityFrameworkCore;

public static class IncidentSuggestionEndpoints
{
    public static void MapIncidentSuggestionEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/incidents/{id:guid}/suggestions",
            async (Guid id, SignalDeskDbContext db) =>
        {
            var incident = await db.Incidents
                .AsNoTracking()
                .SingleOrDefaultAsync(i => i.Id == id);

            if (incident is null)
                return Results.NotFound();

            if (incident.Status == "Resolved")
            {
                return Results.Ok(new
                {
                    incidentVersion = incident.Version,
                    candidateLimitReached = false,
                    suggestions = Array.Empty<Suggestion>()
                });
            }

            var linkedReports = await db.Reports
                .AsNoTracking()
                .Where(r => r.IncidentId == id)
                .Select(r => new Anchor(r.Location, r.CreatedAt))
                .ToListAsync();

            var hasLinkedReports = linkedReports.Count > 0;

            var anchors = hasLinkedReports
                ? linkedReports
                : new List<Anchor>
                {
                    new("", incident.CreatedAt)
                };

            var window = TimeSpan.FromHours(2);

            var earliest = anchors.Min(a => a.CreatedAt) - window;
            var latest = anchors.Max(a => a.CreatedAt) + window;

            // Bound the amount of data scored in memory.
            // The extra record tells the UI whether the limit was reached.
            var candidates = await db.Reports
                .AsNoTracking()
                .Where(r =>
                    r.IncidentId == null &&
                    r.Status != "Resolved" &&
                    r.Service == incident.Service &&
                    r.CreatedAt >= earliest &&
                    r.CreatedAt <= latest)
                .OrderByDescending(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .Take(201)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Service,
                    r.Location,
                    r.Status,
                    r.CreatedAt,
                    r.Version
                })
                .ToListAsync();

            var suggestions = new List<Suggestion>();

            foreach (var report in candidates.Take(200))
            {
                var nearbyAnchors = anchors
                    .Where(anchor =>
                        (report.CreatedAt - anchor.CreatedAt)
                            .Duration() <= window)
                    .ToList();

                if (nearbyAnchors.Count == 0)
                    continue;

                var locationMatches =
                    hasLinkedReports &&
                    !string.IsNullOrWhiteSpace(report.Location) &&
                    nearbyAnchors.Any(anchor =>
                        NormalizeLocation(anchor.Location) ==
                        NormalizeLocation(report.Location));

                var reasons = new List<string>
                {
                    $"Same service: {report.Service}.",
                    hasLinkedReports
                        ? "Submitted within 2 hours of a linked report."
                        : "Submitted within 2 hours of incident creation."
                };

                if (locationMatches)
                {
                    reasons.Add(
                        "Same location as a nearby linked report.");
                }

                suggestions.Add(new Suggestion(
                    report.Id,
                    report.Title,
                    report.Service,
                    report.Location,
                    report.Status,
                    report.CreatedAt,
                    report.Version,
                    locationMatches ? "Strong" : "Possible",
                    locationMatches ? 3 : 2,
                    reasons.ToArray()));
            }

            var ranked = suggestions
                .OrderByDescending(s => s.Score)
                .ThenByDescending(s => s.CreatedAt)
                .ThenBy(s => s.Id)
                .Take(10)
                .ToArray();

            return Results.Ok(new
            {
                incidentVersion = incident.Version,
                candidateLimitReached = candidates.Count > 200,
                suggestions = ranked
            });
        })
        .RequireAuthorization(policy =>
            policy.RequireRole("Technician", "Admin"));
    }

    private static string NormalizeLocation(string location)
    {
        return string.Join(
            " ",
            location.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private sealed record Anchor(
        string Location,
        DateTimeOffset CreatedAt);

    private sealed record Suggestion(
        Guid Id,
        string Title,
        string Service,
        string Location,
        string Status,
        DateTimeOffset CreatedAt,
        int Version,
        string Match,
        int Score,
        string[] Reasons);
}