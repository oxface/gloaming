using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Persistence.Postgres.Tests;

public sealed class RelationalRowSecurityTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedInlineOnlyWritesRetainOwnershipAndRestrictedRowSecurity(
        bool asynchronous
    )
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using (var alpha = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await alpha.Database.Database.BeginTransactionAsync(
                Token
            );
            var row = await alpha.Database.Notes.IgnoreQueryFilters().SingleAsync(Token);
            Assert.Equal(proof.AlphaId, row.Id);
            alpha.Database.ChangeTracker.AutoDetectChangesEnabled = false;
            row.Review.Notes.Text = "Changed nested inline leaf";
            await Save(alpha.Database);
            await transaction.CommitAsync(Token);
        }

        await using (var alpha = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await alpha.Database.Database.BeginTransactionAsync(
                Token
            );
            var foreign = new Note { Id = proof.BetaId, Partition = ProofDatabase.Beta };
            alpha.Database.Attach(foreign);
            alpha.Database.ChangeTracker.AutoDetectChangesEnabled = false;
            foreign.Review.Notes.Text = "Foreign nested inline edit";
            var failure = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                Save(alpha.Database)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
            await transaction.RollbackAsync(Token);
        }

        // Without the EF guard, RLS must still hide the stored foreign row with its real owner.
        await using (
            var faulty = new Operation(proof.Runtime, ProofDatabase.Alpha, validateWrites: false)
        )
        {
            await using var transaction = await faulty.Database.Database.BeginTransactionAsync(
                Token
            );
            var foreign = new Note { Id = proof.BetaId, Partition = ProofDatabase.Beta };
            faulty.Database.Attach(foreign);
            foreign.Review.Notes.Text = "Bypassed guard";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(faulty.Database));
            await transaction.RollbackAsync(Token);
        }

        await using (var fresh = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await fresh.Database.Database.BeginTransactionAsync(
                Token
            );
            var row = await fresh.Database.Notes.SingleAsync(Token);
            Assert.Equal("Changed nested inline leaf", row.Review.Notes.Text);
            Assert.Equal("Unreviewed", row.Review.Summary);
            row.Review.Notes.Text = "Rolled back nested inline leaf";
            await Save(fresh.Database);
            await transaction.RollbackAsync(Token);
        }

        await using (var fresh = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await fresh.Database.Database.BeginTransactionAsync(
                Token
            );
            Assert.Equal(
                "Changed nested inline leaf",
                (await fresh.Database.Notes.SingleAsync(Token)).Review.Notes.Text
            );
            await transaction.CommitAsync(Token);
        }

        await using var beta = new Operation(proof.Runtime, ProofDatabase.Beta);
        await using var read = await beta.Database.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            "Original note",
            (await beta.Database.Notes.SingleAsync(Token)).Review.Notes.Text
        );
        await read.CommitAsync(Token);

        Task<int> Save(ConsumerDatabase context) =>
            asynchronous ? context.SaveChangesAsync(Token) : Task.FromResult(context.SaveChanges());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
