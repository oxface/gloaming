using System.Text.Json;
using Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#if EARLIER_BUILD
namespace Gloaming.CommandReceipts.Deployment.Earlier;

#else
namespace Gloaming.CommandReceipts.Deployment.Current;

#endif

public static class Application
{
    public static async Task<int> Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("RECEIPT_DEPLOYMENT_CONNECTION")
            ?? throw new InvalidOperationException("A disposable connection is required.");
        var services = new ServiceCollection();
        services.AddPostgresCommandReceipts<DeploymentDatabase>();
        services.AddDbContext<DeploymentDatabase>(
            (provider, options) =>
                options
                    .UseNpgsql(connection)
                    .ConfigureGloamingCapabilities<DeploymentDatabase>(provider)
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DeploymentDatabase>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        if (args is ["setup"])
        {
            await database.Database.EnsureCreatedAsync(token);
            return 0;
        }

        try
        {
            Guid id = Guid.Parse(args[0]);
            string label = args[1];
            // V1 comparison bytes survive the additive V2 consumer request shape.
#if EARLIER_BUILD
            string input = JsonSerializer.Serialize(new InputV1(label));
#else
            var request = new InputV2(label, "new-client-trace");
            string input = JsonSerializer.Serialize(new InputV1(request.Label));
#endif
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var attempt = await scope
                .ServiceProvider.GetRequiredService<PostgresCommandReceipts<DeploymentDatabase>>()
                .AcquireAsync(new("admitted-tenant", "publish", id), input, token);
            Console.WriteLine("ACQUIRED");
            if (attempt.Result is { } retained)
            {
                Console.WriteLine("REPLAY");
#if EARLIER_BUILD
                if (retained.FormatVersion != 1)
                    throw new InvalidDataException(
                        "Deploy a reader supporting this result format."
                    );

                Console.WriteLine(
                    JsonSerializer
                        .Deserialize<ResultV1>(retained.Payload, JsonSerializerOptions.Web)!
                        .Label
                );
#else
                string value = retained.FormatVersion switch
                {
                    1 => JsonSerializer
                        .Deserialize<ResultV1>(retained.Payload, JsonSerializerOptions.Web)!
                        .Label,
                    2 => JsonSerializer
                        .Deserialize<ResultV2>(retained.Payload, JsonSerializerOptions.Web)!
                        .Label,
                    _ => throw new InvalidDataException("Unsupported receipt result."),
                };
                Console.WriteLine(value);
#endif
                return 0;
            }

            Console.WriteLine("EXECUTE");
            database.Add(new Effect { Id = id, Label = label });
#if EARLIER_BUILD
            attempt.Retain(
                new(1, JsonSerializer.Serialize(new ResultV1(label), JsonSerializerOptions.Web))
            );
#else
            attempt.Retain(
                new(
                    2,
                    JsonSerializer.Serialize(
                        new ResultV2(label, "reviewed"),
                        JsonSerializerOptions.Web
                    )
                )
            );
#endif
            await database.SaveChangesAsync(token);
            if (Environment.GetEnvironmentVariable("RECEIPT_DEPLOYMENT_PAUSE") == "before")
            {
                Console.WriteLine("BEFORE_COMMIT");
                string release =
                    Environment.GetEnvironmentVariable("RECEIPT_DEPLOYMENT_RELEASE_FILE")
                    ?? throw new InvalidOperationException(
                        "The crash barrier needs a release signal."
                    );
                while (!File.Exists(release))
                    await Task.Delay(25, token);
            }

            await transaction.CommitAsync(token);
            Console.WriteLine("COMMITTED");
            if (Environment.GetEnvironmentVariable("RECEIPT_DEPLOYMENT_PAUSE") == "after")
                await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return 0;
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("INCOMPATIBLE_READER");
            return 10;
        }
        catch (CommandReceiptConflictException)
        {
            Console.WriteLine("INPUT_CONFLICT");
            return 11;
        }
    }
}

internal sealed class DeploymentDatabase(DbContextOptions<DeploymentDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureCommandReceipts("deployment", "logical_actions");
        model.Entity<Effect>().ToTable("effects", "deployment").HasKey(row => row.Id);
    }
}

internal sealed class Effect
{
    public Guid Id { get; set; }
    public string Label { get; set; } = null!;
}

internal sealed record InputV1(string Label);

#if !EARLIER_BUILD
internal sealed record InputV2(string Label, string ClientTrace);

internal sealed record ResultV2(string Label, string Review);
#endif

internal sealed record ResultV1(string Label);
