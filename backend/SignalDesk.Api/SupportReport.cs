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
}