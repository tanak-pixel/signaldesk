public class IncidentActivity
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}