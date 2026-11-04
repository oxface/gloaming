using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RelationalOwnershipConsumer;

// Linked into provider tests and copied outside the checkout for exact-package adoption.
public static class RelationalOwnershipJourney
{
    public static async Task RunAsync(
        string connection,
        bool registerOwnershipFirst = false,
        CancellationToken token = default
    )
    {
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        foreach (var (tenant, id) in new[] { ("alpha", alphaId), ("beta", betaId) })
        {
            await using var context = Open(tenant);
            await context.Database.EnsureCreatedAsync(token);
            context.Add(
                new RelationalDocumentRow
                {
                    Id = id,
                    Tenant = tenant,
                    Defaults = [new("basic", "Basic")],
                }
            );
            await context.SaveChangesAsync(token);
        }

        await using (var context = Open("alpha"))
        {
            context.Add(
                new RelationalDocumentRow
                {
                    Id = Guid.NewGuid(),
                    Tenant = "alpha",
                    Archived = true,
                }
            );
            context.SaveChanges();
            Require(await context.Documents.CountAsync(token) == 1, "Composed filters changed.");
            Require(
                await context.Documents.IgnoreQueryFilters(["Archived"]).CountAsync(token) == 2,
                "Named filter composition changed."
            );
        }

        foreach (var asynchronous in new[] { false, true })
        {
            var label = asynchronous ? "async" : "sync";
            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(row.Text.TechnologyContext is null, "Null scalar leaf did not round trip.");
                row.Text = new(label, "PostgreSQL");
                await Save(context, asynchronous);
            }

            // The next save changes only a leaf two complex levels below the root.
            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(
                    row.Text == new TextValue(label, "PostgreSQL"),
                    "Scalar complex replacement was lost."
                );
                row.Review.Notes.Summary = label;
                await Save(context, asynchronous);
                Require(
                    !context.ChangeTracker.AutoDetectChangesEnabled,
                    "Save changed the caller's detection setting."
                );
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(row.Review.Notes.Summary == label, "Nested-only update was lost.");
                Require(
                    row.Selection is null && row.Review.SelectedType is null,
                    "Optional complex absence was lost."
                );
                row.Selection = new(label, 2);
                row.Review.SelectedType = new(label, 3);
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(
                    row.Selection == new SelectionValue(label, 2)
                        && row.Review.SelectedType == new SelectionValue(label, 3),
                    "Optional complex presence was lost."
                );
                row.Selection = row.Selection! with { Revision = 4 };
                row.Review.SelectedType = row.Review.SelectedType! with { Revision = 5 };
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(
                    row.Selection?.Revision == 4 && row.Review.SelectedType?.Revision == 5,
                    "Optional complex replacement was lost."
                );
                row.Selection = null;
                row.Review.SelectedType = null;
                row.Text = row.Text with { TechnologyContext = null };
                row.Snapshot = new(label);
                row.Defaults[0] = new("basic", label);
                await Save(context, asynchronous);
            }

            // A JSON-only save on a mixed entity still follows the same owner guard.
            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                Require(
                    row.Snapshot.Label == label && row.Defaults[0].Label == label,
                    "Mixed JSON round trip failed."
                );
                row.Snapshot = new(label + " JSON only");
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                context.Add(new RelationalDocumentRow { Id = Guid.NewGuid(), Tenant = "beta" });
                await Guarded(context, asynchronous, TenantOwnershipFailure.ForeignOwner);
            }

            foreach (var deleting in new[] { false, true })
            {
                await using (var context = Open("alpha"))
                {
                    var foreign = new RelationalDocumentRow { Id = betaId, Tenant = "beta" };
                    context.Attach(foreign);
                    if (deleting)
                        context.Remove(foreign);
                    else
                        foreign.Review.Notes.Summary = "foreign nested edit";

                    await Guarded(context, asynchronous, TenantOwnershipFailure.ForeignOwner);
                }

                // In-memory ownership matches alpha; the stored-row predicate must refuse beta.
                await using (var context = Open("alpha"))
                {
                    var forged = new RelationalDocumentRow { Id = betaId, Tenant = "alpha" };
                    context.Attach(forged);
                    if (deleting)
                        context.Remove(forged);
                    else
                        forged.Review.Notes.Summary = "forged nested edit";

                    await Rejected<DbUpdateConcurrencyException>(() => Save(context, asynchronous));
                }
            }

            await using (var context = Open("alpha"))
            {
                var foreign = await context
                    .Documents.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == betaId, token);
                foreign.Review.Notes.Comment = "foreign tracked edit";
                await Guarded(context, asynchronous, TenantOwnershipFailure.ForeignOwner);
            }

            await using (var context = Open("alpha"))
            {
                var row = await context.Documents.SingleAsync(token);
                row.Tenant = "beta";
                row.Review.Notes.Comment = "changed owner";
                await Guarded(context, asynchronous, TenantOwnershipFailure.OwnershipChanged);
            }

            await using (var context = Open("alpha"))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);
                var row = await context.Documents.SingleAsync(token);
                row.Review.Notes.Summary = "rolled back";
                await Save(context, asynchronous);
                await transaction.RollbackAsync(token);
            }

            await using (var fresh = Open("alpha"))
            {
                var saved = await fresh.Documents.SingleAsync(token);
                Require(
                    saved.Id == alphaId && saved.Marker == "unchanged scalar",
                    "Root identity/scalar changed."
                );
                Require(
                    saved.Selection is null
                        && saved.Review.SelectedType is null
                        && saved.Text.TechnologyContext is null,
                    "Optional complex/leaf removal was lost."
                );
                Require(saved.Review.Notes.Summary == label, "Nested rollback failed.");
                Require(
                    saved.Snapshot.Label == label + " JSON only",
                    "Mixed JSON-only save was lost."
                );
            }

            await using (var fresh = Open("beta"))
            {
                var saved = await fresh.Documents.SingleAsync(token);
                Require(
                    saved.Text.Name == "Original"
                        && saved.Review.Notes.Summary == "Original summary"
                        && saved.Review.Notes.Comment is null,
                    "Foreign row was changed or deleted."
                );
            }

            var deleteId = Guid.NewGuid();
            await using (var context = Open("alpha"))
            {
                context.Add(new RelationalDocumentRow { Id = deleteId, Tenant = "alpha" });
                await Save(context, asynchronous);
            }

            await using (var context = Open("alpha"))
            {
                context.Remove(new RelationalDocumentRow { Id = deleteId, Tenant = "alpha" });
                await Save(context, asynchronous);
            }

            await using var afterDelete = Open("alpha");
            Require(
                !await afterDelete.Documents.AnyAsync(x => x.Id == deleteId, token),
                "Same-owner detached delete failed."
            );
        }

        RelationalDocumentsContext Open(string tenant)
        {
            var options = new DbContextOptionsBuilder().UseNpgsql(connection).Options;
            RelationalDocumentsContext context = registerOwnershipFirst
                ? new EarlyOwnershipContext(options, tenant)
                : new RelationalDocumentsContext(options, tenant);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            return context;
        }

        Task<int> Save(RelationalDocumentsContext context, bool asynchronous) =>
            asynchronous ? context.SaveChangesAsync(token) : Task.FromResult(context.SaveChanges());

        async Task Guarded(
            RelationalDocumentsContext context,
            bool asynchronous,
            TenantOwnershipFailure reason
        )
        {
            var failure = await Rejected<TenantOwnershipException>(() =>
                Save(context, asynchronous)
            );
            Require(failure.Reason == reason, "Unexpected tenant guard failure.");
        }
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
        catch (T failure)
        {
            return failure;
        }

        throw new InvalidOperationException(
            "The invalid consumer operation unexpectedly succeeded."
        );
    }
}
