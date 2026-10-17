using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Keep database credentials in configuration or user secrets, not source code.
var connectionString =
    builder.Configuration.GetConnectionString("SignalDesk")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:SignalDesk. Configure user secrets.");

builder.Services.AddDbContext<SignalDeskDbContext>(options =>
    options.UseNpgsql(connectionString));

// Permit the local Vite development server to call this API from the browser.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:5174")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        // Require unique account emails and a stronger local password.
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SignalDeskDbContext>();

builder.Services.AddAuthorization();

var app = builder.Build();

// Keep this learning version local until deployment is configured.
if (!app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Learning starter: run locally in Development only.");
}

app.UseCors();
// Optionally grant the Technician role to a pre-registered local account.
var technicianEmail =
    builder.Configuration["Development:TechnicianEmail"];
    
if (app.Environment.IsDevelopment() &&
    !string.IsNullOrWhiteSpace(technicianEmail))
{
    await using var scope = app.Services.CreateAsyncScope();

    var roleManager =
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var userManager =
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    const string roleName = "Technician";

    if (!await roleManager.RoleExistsAsync(roleName))
    {
        var result = await roleManager.CreateAsync(
            new IdentityRole(roleName));

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    var technician = await userManager.FindByEmailAsync(technicianEmail);

    if (technician is null)
    {
        throw new InvalidOperationException(
            "Register the configured technician account before starting.");
    }

    if (!await userManager.IsInRoleAsync(technician, roleName))
    {
        var result = await userManager.AddToRoleAsync(
            technician, roleName);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
app.UseAuthentication();
app.UseAuthorization();

// Registration and login endpoints.
app.MapGroup("/auth").MapIdentityApi<ApplicationUser>();

// Public health check.
app.MapGet("/health", () =>
    Results.Ok(new { status = "healthy" }));

// Return the signed-in account.
app.MapGet("/api/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        id = user.FindFirstValue(ClaimTypes.NameIdentifier),
        email = user.FindFirstValue(ClaimTypes.Email)
    }))
    .RequireAuthorization();

// Reporters see their own reports.
// Technicians and admins can see all reports.
app.MapGet("/api/reports",
    async (SignalDeskDbContext db, ClaimsPrincipal user) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var query = db.Reports.AsNoTracking();

    // Apply ownership filtering before joining incident details.
    var canReviewAll =
        user.IsInRole("Technician") || user.IsInRole("Admin");

    if (!canReviewAll)
    {
        query = query.Where(r => r.ReporterId == userId);
    }

    var reports = await (
        from report in query
        join incident in db.Incidents.AsNoTracking()
            on report.IncidentId equals (Guid?)incident.Id
            into linkedIncidents
        from incident in linkedIncidents.DefaultIfEmpty()
        orderby report.CreatedAt descending
        select new
        {
            report.Id,
            report.Title,
            report.Description,
            report.Service,
            report.Location,
            report.Status,
            report.CreatedAt,
            report.Version,
            report.IncidentId,

            // Reporters receive only public incident summary fields here.
            LinkedIncident = incident == null
                ? null
                : new
                {
                    incident.Title,
                    incident.Status
                }
        }
    ).ToListAsync();

    return Results.Ok(reports);
}).RequireAuthorization();

// STEP 4: Create a report owned by the signed-in account.
app.MapPost("/api/reports",
    async (
        CreateReport input,
        SignalDeskDbContext db,
        ClaimsPrincipal user) =>
{
    var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

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

    // Assign ownership to the authenticated reporter, never to request input.
    var report = new SupportReport
    {
        Id = Guid.NewGuid(),
        ReporterId = userId,
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
}).RequireAuthorization();

// STEP 5: Only technicians and admins can change report status.
app.MapPatch("/api/reports/{id:guid}/status",
    async (
        Guid id,
        ChangeStatus input,
        SignalDeskDbContext db) =>
{
    if (input.Status is not ("New" or "Investigating" or "Resolved"))
    {
        return Results.BadRequest(new
        {
            message = "Unknown status."
        });
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

    // EF compares the original version at save time to catch concurrent writes.
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
}).RequireAuthorization(policy =>
    policy.RequireRole("Technician", "Admin"));

app.MapIncidentEndpoints();
app.MapIncidentSuggestionEndpoints();


// Lets the frontend tailor controls to the signed-in user's role.
app.MapGet("/api/access", (System.Security.Claims.ClaimsPrincipal user) =>
    Results.Ok(new
    {
        canManageIncidents =
            user.IsInRole("Technician") || user.IsInRole("Admin")
    }))
    .RequireAuthorization();

foreach (var url in new[]
{
    // Support both API URLs used during local development.
    "http://localhost:5080",
    "http://localhost:5081"
})
{
    if (!app.Urls.Contains(url))
    {
        app.Urls.Add(url);
    }
}

app.Run();
// The following record types are used to bind JSON request bodies to strongly typed parameters in the API endpoints.
record CreateReport(
    string? Title,
    string? Description,
    string? Service,
    string? Location);
// The version number is used to detect concurrent updates to the same report.  
record ChangeStatus(string? Status, int Version);
public partial class Program { }