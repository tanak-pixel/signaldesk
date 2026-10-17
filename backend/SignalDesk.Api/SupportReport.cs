public class SupportReport
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Service { get; set; } = "";
    public string Location { get; set; } = "";
    public string Status { get; set; } = "New";
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
    public string? ReporterId { get; set; }

    // Null means the report isn't linked to an incident.
    public Guid? IncidentId { get; set; }
}