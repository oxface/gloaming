using Gloaming.Agents.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

var dependencies = typeof(EfChatHistoryProvider<DbContext>).Assembly.GetReferencedAssemblies();
if (
    dependencies.Any(x =>
        x.Name!.Contains("Workflows", StringComparison.Ordinal)
        || x.Name.StartsWith("Npgsql", StringComparison.Ordinal)
    )
)
    throw new InvalidOperationException(
        "History must not select workflow or provider integration."
    );

Console.WriteLine("PASS independently packaged native agent history");
