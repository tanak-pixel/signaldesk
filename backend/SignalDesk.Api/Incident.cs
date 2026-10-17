public class Incident
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Service { get; set; } = "";
    public string Status { get; set; } = "New";

    public string? AssignedTechnicianId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public int Version { get; set; } = 1;
}