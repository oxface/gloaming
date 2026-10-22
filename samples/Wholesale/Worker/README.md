# Wholesale worker and operations

This is the product's single worker/maintenance executable. It selects an explicit delivery
mode or finite business operation; module Contracts, contexts and native transactions remain
distinct. It does not reference the integration-test scenarios.

For durable stock issues and reservations use the [broker roles](Broker/README.md). `broker` is
the default transport; `--transport broker` makes it explicit. For broker-free reservations use
the [local roles](Local/README.md) with `--transport local`. Both use the same product binary:

```bash
dotnet run --project samples/Wholesale/Worker -- --transport broker --role run
dotnet run --project samples/Wholesale/Worker -- --transport local --role run
```

Choose one transport topology for a deployment. Sharing an assembly does not make mixed local
and broker relays safe against the same outgoing queue. Restricted `dispatch-sales` and
`process-sales-replies` retain their dedicated connections, admission and rejection of alternate
credentials. Local mode creates no broker connection; its relay namespace cannot depend on
module persistence or broker APIs, even though the executable also contains broker roles.

## Module operations

These finite commands are trusted local operator entry points. OS/database access controls
who can execute them and which Organizations they may select. They explicitly establish one
tenant and the `wholesale.operations` system actor; context establishment grants no permission.
They are not externally authenticated APIs and do not impersonate a human.

Provision storage using the API's finite product setup or module migrations first. Supply only
the selected module's native database credential (`ConnectionStrings__Inventory` or
`ConnectionStrings__Purchasing`), `--Organization`, `--Operation`, and a JSON `--Input` file for
writes. Runtime never migrates, seeds or resets storage. Each accepted write saves and commits
in its own owning native transaction; a business rejection does not save/commit.

```bash
dotnet run --project samples/Wholesale/Worker -- --role inventory --Organization wholesale-alpha --Operation open --Input /absolute/path/open-stock.json
dotnet run --project samples/Wholesale/Worker -- --role inventory --Organization wholesale-alpha --Operation receive --Input /absolute/path/receipt.json
dotnet run --project samples/Wholesale/Worker -- --role inventory --Organization wholesale-alpha --Operation issue --Input /absolute/path/issue.json
dotnet run --project samples/Wholesale/Worker -- --role inventory --Organization wholesale-alpha --Operation read --Id 8c6e282e-d9ec-455a-8fab-d114c176a7cd
dotnet run --project samples/Wholesale/Worker -- --role purchasing --Organization wholesale-alpha --Operation draft --Input /absolute/path/purchase.json
dotnet run --project samples/Wholesale/Worker -- --role purchasing --Organization wholesale-alpha --Operation change-lines --Input /absolute/path/lines.json
```

Example stock-open input (all identities are caller-owned):

```json
{"id":"8c6e282e-d9ec-455a-8fab-d114c176a7cd","stockItemId":"d1f4fcb9-7680-42df-84c8-873eab3accb3","stockingLocationId":"83614faa-6cdb-4dc2-b55a-f12bf9892c67","baseUnitCode":"EA"}
```

Receipt and issue inputs use `id`, `expectedVersion` and `receipts`/`issues`, each containing
`quantity`; receipt entries may include `deliveryReference`. Purchasing draft input uses `id`,
`code`, `supplierReference` and `currency`. Line replacement uses `id`, `expectedVersion` and
`lines` containing `itemCode`, `quantity`, `unitPrice`. These are the existing module Contract
shapes, not a second business implementation. Read uses `--Operation read --Id ...` for either module.

Writes output JSON with `committed`, `outcome` and the native result. Exit 0 acknowledges commit;
exit 2 reports a rejected business change or missing read target. Provider/commit failure can
leave an uncertain outcome; inspect the current version before repeating. Stable command IDs,
operator authorization, scheduling and retries remain caller policy.

The [stock-regeneration command](Maintenance/stock-regeneration.md) retains its explicit
`--regenerate-stock <organization-key> <stock-position-id>` syntax, progress, cancellation and
exit codes. It uses `WHOLESALE_DEMO_CONNECTION_STRING` for its existing Inventory target and
never provisions storage.
