using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var isDevelopment = builder.Environment.IsDevelopment();

// Database credentials come from user secrets or host configuration.
var connectionString =
    builder.Configuration.GetConnectionString("SignalDesk");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing ConnectionStrings:SignalDesk. " +
        "Use user secrets locally or ConnectionStrings__SignalDesk on the host.");
}

builder.Services.AddDbContext<SignalDeskDbContext>(options =>
    options.UseNpgsql(connectionString));

// Explicit frontend origins for cross-origin browser requests.
var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

var allowedOrigins = (
    configuredOrigins ??
    (isDevelopment
        ? new[]
        {
            "http://localhost:5173",
            "http://localhost:5174"
        }
        : Array.Empty<string>()))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

foreach (var origin in allowedOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp &&
         uri.Scheme != Uri.UriSchemeHttps) ||
        uri.AbsolutePath != "/" ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment) ||
        !string.IsNullOrEmpty(uri.UserInfo))
    {
        throw new InvalidOperationException(
            "Each Cors:AllowedOrigins value must be an HTTP/HTTPS origin " +
            "without a path, query, or fragment.");
    }

    if (!isDevelopment && uri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException(
            "Deployed frontend origins must use HTTPS.");
    }
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        // No configured origins means no cross-origin access.
        // Same-origin requests do not require CORS.
    });
});

// Identity bearer tokens depend on Data Protection keys.
var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("SignalDesk");

var keysPath =
    builder.Configuration["DataProtection:KeysPath"];

if (!string.IsNullOrWhiteSpace(keysPath))
{
    var absoluteKeysPath = Path.GetFullPath(keysPath);

    Directory.CreateDirectory(absoluteKeysPath);

    dataProtection.PersistKeysToFileSystem(
        new DirectoryInfo(absoluteKeysPath));
}
else if (!isDevelopment)
{
    throw new InvalidOperationException(
        "Configure DataProtection:KeysPath using a private persistent " +
        "directory before starting outside Development.");
}

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<SignalDeskDbContext>();

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (!isDevelopment)
{
    // Return generic problem responses for unexpected production errors.
    app.UseExceptionHandler();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Local-only technician bootstrap.
// Production accounts must be provisioned separately.
var technicianEmail =
    builder.Configuration["Development:TechnicianEmail"];

if (isDevelopment &&
    !string.IsNullOrWhiteSpace(technicianEmail))
{
    await using var scope =
        app.Services.CreateAsyncScope();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<ApplicationUser>>();

    const string roleName = "Technician";

    if (!await roleManager.RoleExistsAsync(roleName))
    {
        var result = await roleManager.CreateAsync(
            new IdentityRole(roleName));

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join(
                    "; ",
                    result.Errors.Select(error => error.Description)));
        }
    }

    var technician =
        await userManager.FindByEmailAsync(technicianEmail);

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
                string.Join(
                    "; ",
                    result.Errors.Select(error => error.Description)));
        }
    }
}

// Registration and login endpoints.
app.MapGroup("/auth").MapIdentityApi<ApplicationUser>();

// Liveness check; this does not check database connectivity.
app.MapGet("/health", () =>
    Results.Ok(new { status = "healthy" }));

app.MapGet("/api/me", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        id = user.FindFirstValue(ClaimTypes.NameIdentifier),
        email = user.FindFirstValue(ClaimTypes.Email)
    }))
    .RequireAuthorization();

// Apply ownership filtering before joining incident summaries.
app.MapGet("/api/reports",
    async (
        SignalDeskDbContext db,
        ClaimsPrincipal user) =>
{
    var userId =
        user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

    var query = db.Reports.AsNoTracking();

    var canReviewAll =
        user.IsInRole("Technician") ||
        user.IsInRole("Admin");

    if (!canReviewAll)
    {
        query = query.Where(
            report => report.ReporterId == userId);
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

// Ownership always comes from the authenticated account.
app.MapPost("/api/reports",
    async (
        CreateReport input,
        SignalDeskDbContext db,
        ClaimsPrincipal user) =>
{
    var userId =
        user.FindFirstValue(ClaimTypes.NameIdentifier);

    if (string.IsNullOrWhiteSpace(userId))
        return Results.Unauthorized();

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

    return Results.Created(
        $"/api/reports/{report.Id}",
        report);
}).RequireAuthorization();

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
        return Results.NotFound();

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
}).RequireAuthorization(policy =>
    policy.RequireRole("Technician", "Admin"));

app.MapIncidentEndpoints();
app.MapIncidentSuggestionEndpoints();

app.MapGet("/api/access", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        canManageIncidents =
            user.IsInRole("Technician") ||
            user.IsInRole("Admin")
    }))
    .RequireAuthorization();

// Listening URLs now come from --urls or hosting configuration.
app.Run();

record CreateReport(
    string? Title,
    string? Description,
    string? Service,
    string? Location);

record ChangeStatus(string? Status, int Version);

public partial class Program { }