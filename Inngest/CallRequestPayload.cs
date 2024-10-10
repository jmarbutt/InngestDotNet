namespace Inngest;

public class CallRequestPayload
{
    public InngestEvent Event { get; set; }
    public IEnumerable<InngestEvent> Events { get; set; }
    public Dictionary<string, object> Steps { get; set; }
    public CallRequestContext Ctx { get; set; }
}