# Dispatch customer directory in Wholesale

The canonical Worker owns the bounded population and local delivery composition. Sales owns
profiles and public whole-state publication; Dispatch owns its independent searchable directory.
They use separate PostgreSQL connections, contexts and native transactions. The
[Dispatch guide](../../modules/Dispatch/README.md) defines the revision, tombstone and readiness protocol.

Use `dotnet run --project samples/Wholesale/Worker -- --role directory --Operation NAME`.
Configure only the credentials required for that operation:

| Operation | Configuration | Effect |
| --- | --- | --- |
| setup | ConnectionStrings__Sales, ConnectionStrings__Dispatch | Explicit native migrations with setup credentials |
| admit | ConnectionStrings__Dispatch, Organization | Persist receiver admission before live intake |
| populate | ConnectionStrings__Sales, ConnectionStrings__Dispatch, Organization | Resume bounded public export pages and persist receiver progress |
| export | ConnectionStrings__Sales, Organization; optional After, Limit | Read one public source page |
| relay | ConnectionStrings__SalesDispatch, ConnectionStrings__Dispatch | Deliver at most one Directory outbox message to durable receiver intake |
| process | ConnectionStrings__Dispatch | Process at most one admitted Directory inbox message |
| run | ConnectionStrings__SalesDispatch, ConnectionStrings__Dispatch | Run existing outbox and inbox workers continuously |
| progress / search | ConnectionStrings__Dispatch, Organization; optional Text for search | Read persisted progress or search the independent directory |

These are trusted operator commands, using the named system actor `dispatch.directory` and an
explicit Organization. Dispatch currently admits only `wholesale-alpha` and `wholesale-beta`;
a payload tenant key grants no authority. This CLI does not fabricate a logged-in human.
Normal protected API profile edits retain application identity, membership and antiforgery checks
and publish ongoing directory state in the same Sales transaction. Registration and listing
commands remain public Sales module capabilities for admitted application callers.

Provision `ConnectionStrings__SalesDispatch` using the existing Worker `--role setup-sales-dispatch`,
with separate `ConnectionStrings__SalesDispatchSetup` owner credentials. Relay/run verify that
restricted login and its outbox-only column grants before starting. They do not require business
read credentials. Search/process/admit do not register or connect to Sales. Setup and population
credentials are separate operator concerns; the sample does not provision a receiver login policy.

Admit first, start `run`, then invoke `populate`. Population can restart after interruption;
Dispatch persists its cursor. Readiness means completed baseline traversal with eventual updates,
not a caught-up barrier. Search remains useful while Sales is unavailable. No broker is involved
in this role. Broker and local reservation roles claim only their Inventory request routes;
Directory claims only `dispatch.customer-directory`. Route selection is delivery policy, not
row authorization: native grants and RLS remain authoritative.

The [canonical integration suite](../../IntegrationTests/Directory) retains all fourteen original
B2 PostgreSQL proofs including literal missing-`listed` rejection. Additional API/Worker process
and broker/local route proofs cover actual product adoption. See the
[adoption report](../../../../docs/reports/b2-directory-product-adoption.md).
