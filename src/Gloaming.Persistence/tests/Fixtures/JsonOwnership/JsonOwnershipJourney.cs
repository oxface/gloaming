using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JsonOwnershipConsumer;

// Independent executable consumer: no test-framework or Umbrine dependency.
public static class JsonOwnershipJourney
{
    public static async Task RunAsync(string connection, CancellationToken token = default)
    {
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        foreach (var (tenant, id) in new[] { ("alpha", alphaId), ("beta", betaId) })
        {
            await using var context = Open(tenant);
            await context.Database.EnsureCreatedAsync(token);
            context.Add(
                new JsonDocumentRow
                {
                    Id = id,
                    Tenant = tenant,
                    Defaults = [new("basic", "Basic")],
                }
            );
            await context.SaveChangesAsync(token);
        }

        foreach (var asynchronous in new[] { false, true })
        {
            var label = asynchronous ? "async" : "sync";
            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                context.ChangeTracker.AutoDetectChangesEnabled = false;
                row.Content = row.Content with
                {
                    Name = label,
                    Levels = [new("basic", label), new("advanced", "Advanced")],
                };
                row.Draft = row.Draft with
                {
                    SelectedType = new(label, 2),
                    References = [new(label, 3)],
                    Requirements = [new("basic", Priority.Normal), new("advanced", null)],
                };
                row.Defaults = [new("basic", label), new("advanced", "Advanced")];
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(
                    row.Id == alphaId && row.Marker == "unchanged scalar",
                    "Tenant filter/root scalar changed."
                );
                Require(
                    row.Content.Name == label
                        && row.Content.Levels.Count == 2
                        && row.Content.Levels[0].Description == label,
                    "JSON content did not round trip."
                );
                Require(
                    row.Defaults.Count == 2 && row.Defaults[0].Label == label,
                    "Root JSON collection did not round trip."
                );
                Require(
                    row.Draft.SelectedType == new SelectionValue(label, 2)
                        && row.Draft.References.Single() == new ReferenceValue(label, 3),
                    "Nested JSON did not round trip."
                );
                Require(
                    row.Draft.Requirements[0].Priority == Priority.Normal
                        && row.Draft.Requirements[1].Priority is null,
                    "Nullable enum conversion changed."
                );
                row.Draft = row.Draft with { SelectedType = null };
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var foreign = await context
                    .Documents.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == betaId, token);
                foreign.Defaults[0] = new("basic", "foreign");
                var error = await Rejected<TenantOwnershipException>(() =>
                    Save(context, asynchronous)
                );
                Require(
                    error.Reason == TenantOwnershipFailure.ForeignOwner,
                    "Foreign JSON write escaped validation."
                );
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                row.Tenant = "beta";
                var error = await Rejected<TenantOwnershipException>(() =>
                    Save(context, asynchronous)
                );
                Require(
                    error.Reason == TenantOwnershipFailure.OwnershipChanged,
                    "Tenant mutation escaped validation."
                );
            }

            await using (var context = Open("alpha"))
            {
                var forged = new JsonDocumentRow { Id = betaId, Tenant = "alpha" };
                context.Attach(forged);
                forged.Content = forged.Content with { Name = "forged JSON" };
                await Rejected<DbUpdateConcurrencyException>(() => Save(context, asynchronous));
            }

            await using (var context = Open("alpha"))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);
                var row = await context.Documents.SingleAsync(token);
                row.Content = row.Content with { Name = "rolled back" };
                await Save(context, asynchronous);
                await transaction.RollbackAsync(token);
            }

            await using var fresh = Open("alpha");
            var saved = await fresh.Documents.SingleAsync(token);
            Require(
                saved.Content.Name == label && saved.Draft.SelectedType is null,
                "Rollback/optional JSON restoration failed."
            );
        }

        JsonDocumentsContext Open(string tenant) =>
            new(new DbContextOptionsBuilder().UseNpgsql(connection).Options, tenant);
        Task<int> Save(JsonDocumentsContext context, bool asynchronous) =>
            asynchronous ? context.SaveChangesAsync(token) : Task.FromResult(context.SaveChanges());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static async Task<T> Rejected<T>(Func<Task<int>> operation)
        where T : Exception
    {
        try
        {
            await operation();
        }
        catch (T error)
        {
            return error;
        }

        throw new InvalidOperationException(
            "The invalid consumer operation unexpectedly succeeded."
        );
    }
}
