using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
string source =
    Environment.GetEnvironmentVariable("ConnectionStrings__Sales")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Sales.");
string receiver =
    Environment.GetEnvironmentVariable("ConnectionStrings__Journal")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Journal.");
var registrations = new ServiceCollection().AddIntegrationFeedDemo(source, receiver);
if (args.Contains("--pause-before-commit", StringComparer.Ordinal))
    registrations.AddDbContext<FeedJournalDbContext>(options =>
        options.AddInterceptors(new JournalCommitPause())
    );

await using var services = registrations.BuildServiceProvider();
string command = args.FirstOrDefault() ?? "help";
if (command == "setup")
{
    await FeedComposition.SetupAsync(services, deadline.Token);
    Console.WriteLine("Feed setup complete.");
    return;
}

await using var scope = services.CreateAsyncScope();
string organization = args.Length > 1 ? args[1] : FeedComposition.Alpha;
FeedComposition.Admit(scope.ServiceProvider, organization);
var feed = scope.ServiceProvider.GetRequiredService<ICustomerProfileFeed>();
var journal = scope.ServiceProvider.GetRequiredService<FeedJournal>();
if (command == "write")
{
    var profiles = scope.ServiceProvider.GetRequiredService<ICustomerProfiles>();
    Guid id =
        organization == FeedComposition.Alpha
            ? SalesDemoSeed.AlphaCustomerId
            : SalesDemoSeed.BetaCustomerId;
    var profile =
        await profiles.ReadAsync(id, deadline.Token)
        ?? throw new InvalidOperationException("Seed profile missing.");
    var result = await profiles.ChangeAsync(
        new(
            id,
            profile.AddressId,
            profile.Version,
            "Feed edit " + (profile.Version + 1).ToString(CultureInfo.InvariantCulture),
            profile.AddressLine
        ),
        deadline.Token
    );
    Console.WriteLine(
        result is ProfileChangeResult.Updated ? "Profile edit committed." : "Profile edit refused."
    );
}
else if (command is "subscribe" or "read")
{
    Guid subscription = Guid.Parse(args[2]);
    if (command == "subscribe")
    {
        var bounds = await feed.DescribeAsync(deadline.Token);
        if (bounds.EarliestAvailable.AfterPosition != 0)
            throw new InvalidOperationException(
                "The demo subscribes only from genesis; expired history requires explicit reconciliation."
            );

        await journal.InitializeAsync(
            subscription,
            organization,
            bounds.EarliestAvailable,
            deadline.Token
        );
        Console.WriteLine("Subscription initialized.");
    }
    else
    {
        var cursor = await journal.ReadAsync(subscription, organization, deadline.Token);
        var read = await feed.ReadAsync(cursor, 2, deadline.Token);
        if (read is not CustomerProfileFeedRead.Page page)
            throw new InvalidOperationException(
                read is CustomerProfileFeedRead.CursorExpired
                    ? "Cursor expired: stop and reconcile."
                    : "Invalid feed cursor."
            );

        // An optional diagnostic barrier lets process proofs kill this actual executable before/after commit.
        if (args.Contains("--pause-before-save", StringComparer.Ordinal))
        {
            Console.WriteLine("Page read before journal save.");
            await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
        }

        await journal.RecordAndCheckpointAsync(
            subscription,
            organization,
            cursor,
            page,
            deadline.Token
        );
        Console.WriteLine(
            $"Journal committed: {page.Next.AfterPosition.ToString(CultureInfo.InvariantCulture)}."
        );
        if (args.Contains("--pause-after-commit", StringComparer.Ordinal))
            await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
    }
}
else if (command == "prune")
{
    var bounds = await feed.DescribeAsync(deadline.Token);
    await CustomerProfileFeedSetup.PruneThroughAsync(
        scope.ServiceProvider.GetRequiredService<SalesDbContext>(),
        new(bounds.Head.Generation, long.Parse(args[2], CultureInfo.InvariantCulture)),
        deadline.Token
    );
    Console.WriteLine("Retained prefix pruned.");
}
else
    Console.WriteLine(
        "Commands: setup | write [organization] | subscribe organization subscription-uuid | read organization subscription-uuid | prune organization position"
    );
