using Gloaming.Agents.Workflows;

if (
    typeof(NativeWorkflowExecution)
        .Assembly.GetReferencedAssemblies()
        .Any(x => x.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
)
    throw new InvalidOperationException("Native lifecycle helpers must remain EF-free.");

Console.WriteLine("PASS independently packaged native workflow helpers");
