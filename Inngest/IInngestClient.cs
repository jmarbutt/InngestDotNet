namespace Inngest;

/// <summary>
/// Interface for interacting with the Inngest service
/// </summary>
public interface IInngestClient
{
    /// <summary>
    /// Send an event with a name and data to Inngest
    /// </summary>
    Task<bool> SendEventAsync(string eventName, object eventData);
    
    /// <summary>
    /// Send a pre-configured event to Inngest
    /// </summary>
    Task<bool> SendEventAsync(InngestEvent evt);
    
    /// <summary>
    /// Send multiple events to Inngest in a single request
    /// </summary>
    Task<bool> SendEventsAsync(IEnumerable<InngestEvent> events);
    
    /// <summary>
    /// Add a secret that will be accessible to your functions at runtime
    /// </summary>
    void AddSecret(string key, string value);
    
    /// <summary>
    /// Register a new function using the simplified API
    /// </summary>
    /// <returns>The function definition for chaining step definitions</returns>
    FunctionDefinition CreateFunction(string functionId, Func<InngestContext, Task<object>> handler);
    
    /// <summary>
    /// Register a new function with full configuration options
    /// </summary>
    /// <returns>The function definition for chaining step definitions</returns>
    FunctionDefinition CreateFunction(string id, string name, FunctionTrigger[] triggers, Func<InngestContext, Task<object>> handler, FunctionOptions? options = null);
    
    /// <summary>
    /// Handle incoming requests from the Inngest service
    /// </summary>
    Task HandleRequestAsync(Microsoft.AspNetCore.Http.HttpContext context);
}
