using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.DirectoryDemo.Tests;

public sealed class WireContractTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid Customer = Guid.Parse("80000000-0000-0000-0000-000000000001");

    // This used to deserialize as an apparently valid revision-999 withdrawal.
    private const string MissingListed = """
        {"customerId":"80000000-0000-0000-0000-000000000001","revision":999}
        """;

    [Fact]
    public async Task RelayRejectsOmittedListedWithoutAcceptingOrCompletingDelivery()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.RegisterAsync(Customer, "FIRST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        progress = await proof.ApplyAsync(progress, await proof.PageAsync(null, 100, Token), Token);
        await proof.DrainAsync(Token);
        using var payload = JsonDocument.Parse(MissingListed);
        await using (var scope = proof.Services.CreateAsyncScope())
        {
            DirectoryComposition.Establish(scope.ServiceProvider, DirectoryProof.Alpha);
            var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            scope
                .ServiceProvider.GetRequiredService<IOutbox<SalesDbContext>>()
                .Enqueue(
                    new OutgoingMessage(
                        Guid.NewGuid(),
                        DispatchRegistration.DirectorySubscription,
                        "sales.customer-delivery-state",
                        1,
                        payload.RootElement,
                        DirectoryProof.Alpha
                    )
                );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        var error = await Assert.ThrowsAsync<JsonException>(() => proof.DispatchAsync(Token));
        Assert.Contains("listed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.SourceConnection,
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL",
                Token
            )
        );
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.inbox_messages",
                Token
            )
        );
        await AssertUnchangedAsync(proof, progress);
    }

    [Fact]
    public async Task DurableIntakeWithOmittedListedCannotAdvanceStateOrCompleteItsReceipt()
    {
        await using var proof = await DirectoryProof.CreateAsync(postgres, Token);
        await proof.RegisterAsync(Customer, "FIRST", DirectoryProof.Alpha, Token);
        var progress = await proof.BeginAsync(DirectoryProof.Alpha, Token);
        progress = await proof.ApplyAsync(progress, await proof.PageAsync(null, 100, Token), Token);
        await proof.DrainAsync(Token);
        using var payload = JsonDocument.Parse(MissingListed);
        // Exercise decoding of retained wire data independently of the typed local relay.
        await using (var scope = proof.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<DispatchDbContext>>()
                .ReceiveAsync(
                    DispatchRegistration.DirectorySubscription,
                    new IncomingMessage(
                        Guid.NewGuid(),
                        "sales",
                        "sales.customer-delivery-state",
                        1,
                        payload.RootElement,
                        DirectoryProof.Alpha
                    ),
                    Token
                );
            await transaction.CommitAsync(Token);
        }

        var error = await Assert.ThrowsAsync<JsonException>(() => proof.ProcessAsync(Token));
        Assert.Contains("listed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.inbox_messages WHERE processed_at IS NULL",
                Token
            )
        );
        await AssertUnchangedAsync(proof, progress);
    }

    private static async Task AssertUnchangedAsync(DirectoryProof proof, DirectoryProgress progress)
    {
        Assert.Equal(progress, await proof.BeginAsync(DirectoryProof.Alpha, Token));
        var customer = Assert.Single(await proof.SearchAsync(DirectoryProof.Alpha, Token));
        Assert.Equal(Customer, customer.CustomerId);
        Assert.Equal("FIRST", customer.Code);
        Assert.Equal("FIRST Customer", customer.DisplayName);
        Assert.Equal("FIRST Street", customer.AddressLine);
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.customer_directory WHERE \"Listed\" AND \"Revision\" = 1",
                Token
            )
        );
        Assert.Equal(
            1,
            await DirectoryProof.CountAsync(
                proof.ReceiverConnection,
                "SELECT count(*) FROM dispatch.inbox_messages WHERE processed_at IS NOT NULL",
                Token
            )
        );
    }
}
