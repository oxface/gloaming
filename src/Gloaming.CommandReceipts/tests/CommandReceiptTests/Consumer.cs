using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.CommandReceipts.Tests;

// Independent native adopter: no Sales, Messaging, actor, ownership or tenancy dependency.
internal sealed class PublishDatabase(DbContextOptions<PublishDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureCommandReceipts("publishing", "results");
        model.Entity<Publication>().ToTable("publications", "publishing").HasKey(row => row.Id);
        model.Entity<Publication>().Property(row => row.Version).IsConcurrencyToken();
        model
            .Entity<DispatchIntent>()
            .ToTable("dispatch_intents", "publishing")
            .HasKey(row => row.Id);
    }
}

internal sealed class Publication
{
    public Guid Id { get; set; }
    public int Version { get; set; }
}

internal sealed class DispatchIntent
{
    public Guid Id { get; set; }
    public Guid PublicationId { get; set; }
}

internal static class Consumer
{
    internal static ServiceProvider Services(string connection, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddPostgresCommandReceipts<PublishDatabase>();
        services.AddDbContext<PublishDatabase>(
            (provider, options) =>
                options
                    .UseNpgsql(connection)
                    .AddInterceptors(interceptors)
                    .ConfigureGloamingCapabilities<PublishDatabase>(provider)
        );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    internal static async Task<string> PublishAsync(
        ServiceProvider services,
        CommandReceiptKey key,
        Guid publication,
        string input,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PublishDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var attempt = await scope
            .ServiceProvider.GetRequiredService<PostgresCommandReceipts<PublishDatabase>>()
            .AcquireAsync(key, input, token);
        if (attempt.Result is { } retained)
            return retained.Payload;

        database.Add(new Publication { Id = publication, Version = 1 });
        database.Add(new DispatchIntent { Id = Guid.NewGuid(), PublicationId = publication });
        string result = "published:" + publication.ToString("D");
        attempt.Retain(new(1, result));
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return result;
    }
}
