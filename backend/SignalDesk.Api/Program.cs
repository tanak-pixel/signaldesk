using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("SignalDesk")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:SignalDesk. Configure user secrets.");

builder.Services.AddDbContext<SignalDeskDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()));
builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SignalDeskDbContext>();

builder.Services.AddAuthorization();

var app = builder.Build();

// Authentication and permissions are still a later milestone.
if (!app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Learning starter: run locally in Development only.");
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/auth").MapIdentityApi<ApplicationUser>();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        id = user.FindFirstValue(ClaimTypes.NameIdentifier),
        email = user.FindFirstValue(ClaimTypes.Email)
    }))
    .RequireAuthorization();

app.MapGet("/health", () =>
    Results.Ok(new { status = "healthy" }));

app.MapGet("/api/reports", async (SignalDeskDbContext db) =>
{
    var reports = await db.Reports
        .AsNoTracking()
        .OrderByDescending(r => r.CreatedAt)
        .ToListAsync();

    return Results.Ok(reports);
});

app.MapPost("/api/reports",
    async (CreateReport input, SignalDeskDbContext db) =>
{
    var title = input.Title?.Trim() ?? "";
    var description = input.Description?.Trim() ?? "";
    var service = input.Service?.Trim() ?? "";
    var location = input.Location?.Trim() ?? "";

    if (title.Length is < 5 or > 120 ||
        description.Length is < 10 or > 2000 ||
        location.Length is < 2 or > 120 ||
        service is not ("Wi-Fi" or "Email" or "Printing" or "Other"))
    {
        return Results.BadRequest(new
        {
            message =
                "Check title (5–120), description (10–2000), " +
                "location (2–120), and service."
        });
    }

    var report = new SupportReport
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = description,
        Service = service,
        Location = location,
        Status = "New",
        CreatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    db.Reports.Add(report);
    await db.SaveChangesAsync();

    return Results.Created($"/api/reports/{report.Id}", report);
});

app.MapPatch("/api/reports/{id:guid}/status",
    async (Guid id, ChangeStatus input, SignalDeskDbContext db) =>
{
    if (input.Status is not ("New" or "Investigating" or "Resolved"))
    {
        return Results.BadRequest(new { message = "Unknown status." });
    }

    var report = await db.Reports.FindAsync(id);

    if (report is null)
    {
        return Results.NotFound();
    }

    if (input.Version != report.Version)
    {
        return Results.Conflict(new
        {
            message = "This report changed. Refresh and try again."
        });
    }

    report.Status = input.Status;
    report.Version++;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new
        {
            message = "This report changed. Refresh and try again."
        });
    }

    return Results.Ok(report);
});

app.Run();

record CreateReport(
    string? Title,
    string? Description,
    string? Service,
    string? Location);

record ChangeStatus(string? Status, int Version);