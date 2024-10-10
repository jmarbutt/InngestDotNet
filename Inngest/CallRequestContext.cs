namespace Inngest;

public class CallRequestContext
{
    public string RunId { get; set; }
    public int Attempt { get; set; }
    public bool DisableImmediateExecution { get; set; }
    public bool UseApi { get; set; }
    public string FunctionId { get; set; }
}