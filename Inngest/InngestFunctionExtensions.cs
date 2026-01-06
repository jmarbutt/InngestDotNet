using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Inngest
{
    /// <summary>
    /// Extension methods for building Inngest functions
    /// </summary>
    public static class InngestFunctionExtensions
    {
        /// <summary>
        /// Define a function step during function registration
        /// </summary>
        /// <param name="function">The function definition</param>
        /// <param name="stepId">The ID of the step</param>
        /// <param name="name">Optional display name for the step</param>
        /// <param name="retryOptions">Optional retry configuration for the step</param>
        /// <returns>The updated function definition</returns>
        public static FunctionDefinition WithStep(this FunctionDefinition function, string stepId, string? name = null, RetryOptions? retryOptions = null)
        {
            function.AddStep(stepId, name, retryOptions);
            return function;
        }
        
        /// <summary>
        /// Define a sleep step during function registration
        /// </summary>
        /// <param name="function">The function definition</param>
        /// <param name="stepId">The ID of the step</param>
        /// <param name="durationSeconds">Duration of the sleep in seconds (informational only)</param>
        /// <returns>The updated function definition</returns>
        public static FunctionDefinition WithSleep(this FunctionDefinition function, string stepId, int durationSeconds)
        {
            function.AddStep(stepId, $"Sleep for {durationSeconds}s");
            return function;
        }
    }
}
