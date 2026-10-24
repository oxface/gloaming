using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.Wholesale.Access.Persistence;

public sealed class AccessDesignTimeFactory : IDesignTimeDbContextFactory<AccessDbContext>
{
    public AccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccessDbContext>();
        AccessDatabase.Configure(
            options,
            Environment.GetEnvironmentVariable("ConnectionStrings__Access")
                ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new AccessDbContext(options.Options);
    }
}
