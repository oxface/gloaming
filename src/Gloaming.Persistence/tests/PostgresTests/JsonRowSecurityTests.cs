using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Persistence.Postgres.Tests;

public sealed class JsonRowSecurityTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JsonOnlyEdits_PreserveRuntimeRlsAndBothOwnershipSavePaths(bool asynchronous)
    {
        await using var proof = await ProofDatabase.CreateAsync(postgres, Token);
        await using (var alpha = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await alpha.Database.Database.BeginTransactionAsync(
                Token
            );
            var row = await alpha.Database.Notes.SingleAsync(Token);
            Assert.Null(row.Detail);
            row.Detail = new("JSON title", [new("Original")]);
            await SaveAsync(alpha.Database, asynchronous);
            await transaction.CommitAsync(Token);
        }

        await using (var alpha = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await alpha.Database.Database.BeginTransactionAsync(
                Token
            );
            var row = await alpha.Database.Notes.IgnoreQueryFilters().SingleAsync(Token);
            Assert.Equal(proof.AlphaId, row.Id);
            Assert.NotNull(row.Detail);
            alpha.Database.ChangeTracker.AutoDetectChangesEnabled = false;
            row.Detail.Parts[0] = new("Only nested JSON changed");
            await SaveAsync(alpha.Database, asynchronous);
            await transaction.CommitAsync(Token);
        }

        await using (var alpha = new Operation(proof.Runtime, ProofDatabase.Alpha))
        {
            await using var transaction = await alpha.Database.Database.BeginTransactionAsync(
                Token
            );
            var foreign = new Note
            {
                Id = proof.BetaId,
                Partition = ProofDatabase.Beta,
                Detail = new("Original", []),
            };
            alpha.Database.Attach(foreign);
            foreign.Detail = new("Foreign JSON edit", []);
            var error = await Assert.ThrowsAsync<TenantOwnershipException>(() =>
                SaveAsync(alpha.Database, asynchronous)
            );
            Assert.Equal(TenantOwnershipFailure.ForeignOwner, error.Reason);
            await transaction.RollbackAsync(Token);
        }

        // Deliberately omit the EF ownership validator; PostgreSQL must still exclude the foreign ID.
        await using (
            var faulty = new Operation(proof.Runtime, ProofDatabase.Alpha, validateWrites: false)
        )
        {
            await using var transaction = await faulty.Database.Database.BeginTransactionAsync(
                Token
            );
            var forged = new Note { Id = proof.BetaId, Partition = ProofDatabase.Alpha };
            faulty.Database.Attach(forged);
            forged.Detail = new("Forged JSON edit", []);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                SaveAsync(faulty.Database, asynchronous)
            );
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = new Operation(proof.Runtime, ProofDatabase.Alpha);
        await using var read = await fresh.Database.Database.BeginTransactionAsync(Token);
        var saved = await fresh.Database.Notes.SingleAsync(Token);
        Assert.Equal("Alpha", saved.Body);
        Assert.Equal("Only nested JSON changed", saved.Detail!.Parts[0].Text);
        await read.CommitAsync(Token);
    }

    private static Task<int> SaveAsync(ConsumerDatabase context, bool asynchronous) =>
        asynchronous ? context.SaveChangesAsync(Token) : Task.FromResult(context.SaveChanges());

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
