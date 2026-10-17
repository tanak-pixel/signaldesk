using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

public static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this WebApplication app)
    {
        // Every incident-management route is restricted to technicians/admins.
        var incidents = app.MapGroup("/api/incidents")
            .RequireAuthorization(policy =>
                policy.RequireRole("Technician", "Admin"));

        // List incidents, including assignment details.
        incidents.MapGet("",
            async (SignalDeskDbContext db, ClaimsPrincipal user) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var items = await db.Incidents
                .AsNoTracking()
                // Return newest incidents first and include list-specific summaries.
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Description,
                    i.Service,
                    i.Status,
                    i.AssignedTechnicianId,

                    AssignedTechnicianName = db.Users
                        .Where(u => u.Id == i.AssignedTechnicianId)
                        .Select(u => u.UserName)
                        .FirstOrDefault(),

                    IsAssignedToMe =
                        i.AssignedTechnicianId == userId,

                    i.CreatedAt,
                    i.AcknowledgedAt,
                    i.ResolvedAt,
                    i.Version,

                    ReportCount = db.Reports.Count(
                        r => r.IncidentId == i.Id)
                })
                .ToListAsync();

            return Results.Ok(items);
        });

        // Create an incident.
        incidents.MapPost("",
            async (
                CreateIncident input,
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

            if (title.Length is < 5 or > 120 ||
                description.Length is < 10 or > 2000 ||
                service is not ("Wi-Fi" or "Email" or "Printing" or "Other"))
            {
                return Results.BadRequest(new
                {
                    message =
                        "Check title (5–120), description (10–2000), and service."
                });
            }

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                Title = title,
                Description = description,
                Service = service,
                Status = "New",
                CreatedAt = DateTimeOffset.UtcNow,
                Version = 1
            };

            db.Incidents.Add(incident);

            await AddActivity(
                db, user, incident.Id,
                "Created",
                "Created the incident.");

            // The incident and its initial history entry are saved together.
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/incidents/{incident.Id}",
                incident);
        });

        // View one incident and its linked reports.
        incidents.MapGet("/{id:guid}",
            async (
                Guid id,
                SignalDeskDbContext db,
                ClaimsPrincipal user) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var incident = await db.Incidents
                .AsNoTracking()
                .SingleOrDefaultAsync(i => i.Id == id);

            if (incident is null)
                return Results.NotFound();

            var assignedTechnicianName = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == incident.AssignedTechnicianId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();

            var reports = await db.Reports
                .AsNoTracking()
                .Where(r => r.IncidentId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Results.Ok(new
            {
                incident,
                reports,
                assignedTechnicianName,
                isAssignedToMe =
                    incident.AssignedTechnicianId == userId
            });
        });

        // Link a report while preserving its original contents.
        incidents.MapPost("/{id:guid}/reports/{reportId:guid}",
            async (
                Guid id,
                Guid reportId,
                LinkReport input,
                SignalDeskDbContext db,
                ClaimsPrincipal user) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var incident = await db.Incidents.FindAsync(id);

            if (incident is null)
            {
                return Results.NotFound(new
                {
                    message = "Incident not found."
                });
            }

            if (incident.Status == "Resolved")
            {
                return Results.Conflict(new
                {
                    message =
                        "Cannot link reports to a resolved incident."
                });
            }

            var report = await db.Reports.FindAsync(reportId);

            if (report is null)
            {
                return Results.NotFound(new
                {
                    message = "Report not found."
                });
            }

            if (report.Version != input.ReportVersion ||
                incident.Version != input.IncidentVersion)
            {
                return Results.Conflict(new
                {
                    message =
                        "Report or incident changed. Refresh and retry."
                });
            }

            if (report.IncidentId == id)
            {
                return Results.Ok(new
                {
                    message = "Report is already linked."
                });
            }

            if (report.IncidentId is not null)
            {
                return Results.Conflict(new
                {
                    message =
                        "Report already belongs to another incident."
                });
            }

            if (report.Service != incident.Service)
            {
                return Results.BadRequest(new
                {
                    message =
                        "Report and incident must use the same service."
                });
            }

            report.IncidentId = id;
            report.Version++;

            // Increment both versions so either stale record cancels the link.
            incident.Version++;

            await AddActivity(
                db, user, incident.Id,
                "ReportLinked",
                $"Linked report \"{report.Title}\" ({report.Id}).");

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new
                {
                    message =
                        "Report or incident changed. Refresh and retry."
                });
            }

            return Results.Ok(new
            {
                report.Id,
                report.IncidentId,
                report.Version,
                IncidentVersion = incident.Version
            });
        });

        // Assign an incident to the signed-in technician.
        incidents.MapPatch("/{id:guid}/assign-to-me",
            async (
                Guid id,
                IncidentVersion input,
                ClaimsPrincipal user,
                SignalDeskDbContext db) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var incident = await db.Incidents.FindAsync(id);

            if (incident is null)
                return Results.NotFound();

            if (incident.Version != input.Version)
                return IncidentChanged();

            if (incident.Status == "Resolved")
            {
                return Results.Conflict(new
                {
                    message =
                        "Resolved incidents cannot be assigned."
                });
            }

            if (incident.AssignedTechnicianId is not null &&
                incident.AssignedTechnicianId != userId)
            {
                return Results.Conflict(new
                {
                    message =
                        "Another technician already owns this incident."
                });
            }

            if (incident.AssignedTechnicianId == userId)
                // Treat repeating "assign to me" as a no-op, without new history.
                return Results.Ok(incident);

            incident.AssignedTechnicianId = userId;
            incident.Version++;

            await AddActivity(
                db, user, incident.Id,
                "Assigned",
                "Took ownership of the incident.");

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return IncidentChanged();
            }

            return Results.Ok(incident);
        });

        // Only the current owner can release an assignment.
        incidents.MapPatch("/{id:guid}/release-assignment",
            async (
                Guid id,
                IncidentVersion input,
                ClaimsPrincipal user,
                SignalDeskDbContext db) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var incident = await db.Incidents.FindAsync(id);

            if (incident is null)
                return Results.NotFound();

            if (incident.Version != input.Version)
                return IncidentChanged();

            if (incident.AssignedTechnicianId is null)
            {
                return Results.Conflict(new
                {
                    message = "This incident is already unassigned."
                });
            }

            if (incident.AssignedTechnicianId != userId)
            {
                return Results.Json(
                    new
                    {
                        message =
                            "Only the assigned technician can release this assignment."
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            incident.AssignedTechnicianId = null;
            incident.Version++;

            await AddActivity(
                db, user, incident.Id,
                "AssignmentReleased",
                "Released ownership of the incident.");

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return IncidentChanged();
            }

            return Results.Ok(incident);
        });

        // Update incident status independently of report statuses.
        incidents.MapPatch("/{id:guid}/status",
            async (
                Guid id,
                UpdateIncidentStatus input,
                SignalDeskDbContext db,
                ClaimsPrincipal user) =>
        {
            var userId =
                user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (input.Status is not ("New" or "Investigating" or "Resolved"))
            {
                return Results.BadRequest(new
                {
                    message =
                        "Choose New, Investigating, or Resolved."
                });
            }

            var incident = await db.Incidents.FindAsync(id);

            if (incident is null)
                return Results.NotFound();

            if (incident.Version != input.Version)
                return IncidentChanged();

            if (incident.Status == input.Status)
                return Results.Ok(incident);

            var now = DateTimeOffset.UtcNow;
            var previousStatus = incident.Status;

            if (input.Status == "Investigating")
                incident.AcknowledgedAt ??= now;

            incident.ResolvedAt =
                input.Status == "Resolved" ? now : null;

            // The incident workflow does not alter the linked reports' statuses.
            incident.Status = input.Status;
            incident.Version++;

            await AddActivity(
                db, user, incident.Id,
                "StatusChanged",
                $"Changed status from {previousStatus} to {input.Status}.");

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return IncidentChanged();
            }

            return Results.Ok(incident);
        });

        // Technician-only activity timeline.
        incidents.MapGet("/{id:guid}/activity",
            async (Guid id, SignalDeskDbContext db) =>
        {
            if (!await db.Incidents.AnyAsync(i => i.Id == id))
                return Results.NotFound();

            var activities = await db.IncidentActivities
                .AsNoTracking()
                .Where(a => a.IncidentId == id)
                // Use the ID as a stable tie-breaker for entries with equal times.
                .OrderByDescending(a => a.CreatedAt)
                .ThenByDescending(a => a.Id)
                .Select(a => new
                {
                    a.Id,
                    a.ActorName,
                    a.Action,
                    a.Message,
                    a.CreatedAt
                })
                .ToListAsync();

            return Results.Ok(activities);
        });
    }

    private static IResult IncidentChanged()
    {
        return Results.Conflict(new
        {
            message = "Incident changed. Refresh and retry."
        });
    }

    // Adds history to the same SaveChanges transaction as the change.
    private static async Task AddActivity(
        SignalDeskDbContext db,
        ClaimsPrincipal user,
        Guid incidentId,
        string action,
        string message)
    {
        var userId =
            user.FindFirstValue(ClaimTypes.NameIdentifier);

        var actorName = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();

        // Keep a display-name snapshot rather than exposing the user's account ID.
        db.IncidentActivities.Add(new IncidentActivity
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            ActorName = actorName ?? "Unknown technician",
            Action = action,
            Message = message,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public record CreateIncident(
        string? Title,
        string? Description,
        string? Service);

    public record LinkReport(
        int ReportVersion,
        int IncidentVersion);

    public record IncidentVersion(int Version);

    public record UpdateIncidentStatus(
        string Status,
        int Version);
}