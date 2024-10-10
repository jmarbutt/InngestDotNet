namespace Inngest;

public class InngestEvent
{
    public string Id { get; set; }
    public string Name { get; set; }
    public object Data { get; set; }
    public object User { get; set; }
    public long Timestamp { get; set; }
}