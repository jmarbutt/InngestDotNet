namespace Inngest.Steps;

/// <summary>
/// Exception thrown to interrupt function execution when a step needs to be scheduled.
/// This is the core flow control mechanism for Inngest's incremental execution model.
///
/// When a step is not memoized (hasn't been executed yet), the SDK throws this exception
/// with the operation descriptor. The handler catches it and returns a 206 Partial Content
/// response to tell Inngest what to schedule next.
/// </summary>
public class StepInterruptException : Exception
{
    /// <summary>
    /// The step operations that need to be scheduled by Inngest
    /// </summary>
    public IReadOnlyList<StepOperation> Operations { get; }

    /// <summary>
    /// Creates a new StepInterruptException for a single operation
    /// </summary>
    /// <param name="operation">The step operation to schedule</param>
    public StepInterruptException(StepOperation operation)
        : base($"Step '{operation.Id}' requires scheduling (op: {operation.Op})")
    {
        Operations = new List<StepOperation> { operation }.AsReadOnly();
    }

    /// <summary>
    /// Creates a new StepInterruptException for multiple parallel operations
    /// </summary>
    /// <param name="operations">The step operations to schedule in parallel</param>
    public StepInterruptException(IEnumerable<StepOperation> operations)
        : base("Multiple steps require scheduling")
    {
        Operations = operations.ToList().AsReadOnly();
    }
}
