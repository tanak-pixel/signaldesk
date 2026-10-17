public class Incident
{
    // An incident groups related reports into one coordinated investigation.
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Service { get; set; } = "";
    public string Status { get; set; } = "New";

    // Identity user ID of the technician currently responsible for the incident.
    public string? AssignedTechnicianId { get; set; }

    // These timestamps mark when work was created, acknowledged, and resolved.
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    // Incremented on updates so stale clients cannot silently overwrite changes.
    public int Version { get; set; } = 1;
}