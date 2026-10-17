public class IncidentActivity
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    // Store the display name so history remains readable if the account changes.
    public string ActorName { get; set; } = "";
    // Machine-readable action plus a human-readable summary for the timeline.
    public string Action { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}