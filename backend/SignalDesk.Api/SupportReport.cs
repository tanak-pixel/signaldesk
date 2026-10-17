public class SupportReport
{
    // A report records one reporter's original observation of an issue.
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Service { get; set; } = "";
    public string Location { get; set; } = "";
    public string Status { get; set; } = "New";
    public DateTimeOffset CreatedAt { get; set; }
    // Used as a concurrency token to reject updates based on stale data.
    public int Version { get; set; } = 1;
    // Nullable because reports may have no signed-in owner after account removal.
    public string? ReporterId { get; set; }

    // Null means the report has not been grouped into an incident.
    public Guid? IncidentId { get; set; }
}