using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Sales.DraftOrders;

namespace Gloaming.Samples.Wholesale.Sales;

// Explicit finite HTTP demo setup, not a grant-administration or reconciliation API.
public static class DraftOrderAccessDemoSeed
{
    public static Guid OrderId { get; } = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    public static Guid LineId { get; } = Guid.Parse("a2000000-0000-0000-0000-000000000002");

    public static void Stage(SalesDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        string organization = database.RequiredOrganizationKey;
        bool alpha = organization == "wholesale-alpha";
        if (!alpha && organization != "wholesale-beta")
            throw new ArgumentException("The demo supports Alpha and Beta only.", nameof(database));

        var draft = DraftOrderAggregate.Create(
            new(
                OrderId,
                alpha ? SalesDemoSeed.AlphaCustomerId : SalesDemoSeed.BetaCustomerId,
                "PRIVATE-DEMO-DRAFT",
                "EUR",
                100,
                [new(LineId, "DEMO-NOTEBOOK", 1, alpha ? 40 : 70)]
            )
        );
        database.DraftOrders.Add(DraftOrderRow.Create(organization, draft));
        database.DraftOrderGrants.Add(
            new()
            {
                OrganizationKey = organization,
                OrderId = OrderId,
                SubjectKind = ActorKind.Human,
                SubjectKey = alpha ? "application-alpha" : "application-beta",
                Permission = DraftOrderAction.Edit,
            }
        );
    }
}
