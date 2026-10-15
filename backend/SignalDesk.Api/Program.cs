var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
// This first lesson intentionally has no authentication or durable storage.
// Refuse public/production hosting until those milestones are implemented.
if (!app.Environment.IsDevelopment())
    throw new InvalidOperationException("Learning starter: run locally in Development only.");
app.UseCors();
var gate = new object();
var reports = new Dictionary<Guid, SupportReport>();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/reports", () =>
{
    lock (gate) return Results.Ok(reports.Values.OrderByDescending(r => r.CreatedAt).ToArray());
});
app.MapPost("/api/reports", (CreateReport input) =>
{
    var title = input.Title?.Trim() ?? "";
    var description = input.Description?.Trim() ?? "";
    var service = input.Service?.Trim() ?? "";
    var location = input.Location?.Trim() ?? "";
    if (title.Length is < 5 or > 120 || description.Length is < 10 or > 2000 ||
        location.Length is < 2 or > 120 || service is not ("Wi-Fi" or "Email" or "Printing" or "Other"))
        return Results.BadRequest(new { message = "Check title (5–120), description (10–2000), location (2–120), and service." });
    var report = new SupportReport(Guid.NewGuid(), title, description, service, location,
        "New", DateTimeOffset.UtcNow, 1);
    lock (gate) reports.Add(report.Id, report);
    return Results.Created($"/api/reports/{report.Id}", report);
});
app.MapPatch("/api/reports/{id:guid}/status", (Guid id, ChangeStatus input) =>
{
    if (input.Status is not ("New" or "Investigating" or "Resolved"))
        return Results.BadRequest(new { message = "Unknown status." });
    lock (gate)
    {
        if (!reports.TryGetValue(id, out var existing)) return Results.NotFound();
        // Compare and update inside the same lock so concurrent requests cannot silently overwrite.
        if (input.Version != existing.Version)
            return Results.Conflict(new { message = "This report changed. Refresh and try again." });
        var updated = existing with { Status = input.Status, Version = existing.Version + 1 };
        reports[id] = updated;
        return Results.Ok(updated);
    }
});
app.Run();
record CreateReport(string? Title, string? Description, string? Service, string? Location);
record ChangeStatus(string? Status, int Version);
record SupportReport(Guid Id, string Title, string Description, string Service,
    string Location, string Status, DateTimeOffset CreatedAt, int Version);
