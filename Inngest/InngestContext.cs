using System.Text.Json;

namespace Inngest;

public class InngestContext(
    InngestEvent evt,
    IEnumerable<InngestEvent> events,
    Dictionary<string, object> steps,
    CallRequestContext ctx)
{
    public InngestEvent Event { get; } = evt;
    public IEnumerable<InngestEvent> Events { get; } = events;
    public Dictionary<string, object> Steps { get; } = steps;
    public CallRequestContext Ctx { get; } = ctx;

    public async Task<T> Step<T>(string id, Func<Task<T>> action)
    {
        if (Steps.TryGetValue(id, out var stepResult))
        {
            return JsonSerializer.Deserialize<T>(stepResult.ToString());
        }

        var result = await action();
        Steps[id] = result;
        return result;
    }

    public async Task Sleep(string id, TimeSpan duration)
    {
        await Step(id, async () =>
        {
            await Task.Delay(duration);
            return true;
        });
    }
}