using Gloaming.Conversations.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

if (
    typeof(EfConversationTranscript<DbContext>)
        .Assembly.GetReferencedAssemblies()
        .Any(x =>
            x.Name!.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
            || x.Name.StartsWith("Npgsql", StringComparison.Ordinal)
        )
)
    throw new InvalidOperationException(
        "Visible conversation storage must remain framework/provider independent."
    );

Console.WriteLine("PASS independently packaged visible conversation storage");
