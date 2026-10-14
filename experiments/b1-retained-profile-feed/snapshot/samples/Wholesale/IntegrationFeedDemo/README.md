# Retained Sales profile feed

Status, 2026-10-14: protocol experiment retained as evidence; adoption is deferred.
The [retention and contract assessment](../../../docs/reports/b1-retention-and-contract-assessment.md)
closes B1 by owner direction without further implementation. Revisit adoption only when a
later slice establishes a concrete receiver need. The guarantees below describe this experiment;
they do not establish a requirement for its writer gate or approve committing or merging it.

B1 demonstrates one producer-owned integration feed over ordinary state-stored customer
profiles. Sales publishes accepted `ICustomerProfiles.ChangeAsync` edits after explicit
activation. A finite reader records diagnostic receipts in a separate PostgreSQL database
and checkpoints each page transactionally. This is retained change history, not an initial
customer snapshot or a serving customer view. Seeded/pre-existing profiles have no synthetic
feed entry. Snapshot/bootstrap/cutover remains B2.

## Run the finite consumer

Use .NET from the repository's `global.json`, two disposable PostgreSQL databases and the
native Npgsql connection format. Set `ConnectionStrings__Sales` and
`ConnectionStrings__Journal` in your environment; do not commit credentials. Setup explicitly
applies Sales migrations and creates the diagnostic journal in its own empty database.
It admits only the sample's `wholesale-alpha` and `wholesale-beta` Organizations. Setup
is repeatable without resetting feed generations/progress or re-seeding existing profiles.
Normal reads/writes never apply migrations or bootstrap data.

```bash
dotnet build samples/Wholesale/IntegrationFeedDemo/IntegrationFeedDemo.csproj
dotnet run --project samples/Wholesale/IntegrationFeedDemo --no-build -- setup
dotnet run --project samples/Wholesale/IntegrationFeedDemo --no-build -- subscribe wholesale-alpha 11111111-1111-1111-1111-111111111111
dotnet run --project samples/Wholesale/IntegrationFeedDemo --no-build -- write wholesale-alpha
dotnet run --project samples/Wholesale/IntegrationFeedDemo --no-build -- read wholesale-alpha 11111111-1111-1111-1111-111111111111
```

Each `read` handles one page of at most two changes. Repeat it to drain retained work; exit
and restart it to resume from the same committed journal. `write` changes the existing
demonstration profile with its current expected version. `prune organization position`
explicitly discards a committed prefix; it changes neither customer state nor audit/outbox.
The genesis-only `subscribe` command refuses a pruned source rather than quietly starting
with missing history. Subscriber IDs are journal-owned; generation and Organization bindings
cannot be changed through repeated initialization.

The executable has a 30-second operation deadline. Three explicit diagnostic read modes,
`--pause-before-save`, `--pause-before-commit` and `--pause-after-commit`, report their boundary
and wait for that deadline. Process proofs kill the child at these boundaries; normal commands
do not enable the pauses. No daemon, personal database or broker is required by the proofs.

## Producer contract and setup

Public [Sales.Contracts](../modules/Sales/Sales.Contracts/ICustomerProfileFeed.cs) exposes
`DescribeAsync` and `ReadAsync(after, pageSize, token)`. DTOs contain only Sales-owned public
profile values, immutable change identity/position and typed page/cursor outcomes. Contracts
reference no EF, Npgsql or Rootbolt types. Fields include customer name/address; trusted
consumer admission and permitted disclosure belong to the composing host. No public feed
HTTP endpoint or blanket export permission is installed.

```csharp
services.AddCustomerProfiles();
services.AddCustomerProfileFeed();
// Register SalesDbContext/provider and establish immutable admitted tenant/actor separately.

await using var transaction = await database.Database.BeginTransactionAsync(token);
await CustomerProfileFeedSetup.ActivateAsync(database, token);
// Optional ordinary business seeding in this finite setup; never invent feed history.
await database.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

Activation is idempotent and never resets an existing generation, head or floor. Apply the
new forward migration first and drain old profile writers before activating a deployment.
Mixed old/new writers or direct SQL business edits cannot inherit feed completeness. The
existing HTTP sample's finite setup and proof setup explicitly activate both Organizations;
API startup still performs no migrations/seeding. Earlier migrations/fixtures remain unchanged.

## Committed progress and paging

- The admitted Organization owns one persistent feed generation, committed head H and
  retention floor F. Position 0 is genesis. Generation binds a cursor to that source
  incarnation; it grants no authority. A cursor from another Organization/generation is invalid.
- A profile operation begins its own native ReadCommitted transaction and locks this feed's
  state row before loading/changing business state. Only after that lock is acquired may it
  allocate a PostgreSQL sequence position. Retained change, H, profile/address and audit
  share the existing native transaction. Lock ownership lasts through commit/rollback.
- That gate prevents a higher position in this feed from committing ahead of an unresolved
  lower one. A transaction that began earlier but acquires the gate later receives a later
  position. Different Organizations have separate gates; no global feed order is promised.
- Sequence gaps from rollback or another Organization are valid. Positions are ordered,
  not contiguous stream versions. Timestamps and outbox dispatch order are never cursors.
  Arbitrary deletion above F is corruption; B1 does not detect or repair such bypasses.
- Each read requires a context without an existing/ambient/enlisted transaction or pending
  writes. One short read-only RepeatableRead transaction captures generation, F, H and
  entries. It returns `C < position <= H`, ascending, bounded to 1–100 changes, with one
  look-ahead row. Detached results retain no open connection, lock or snapshot.
- If the page is truncated, `Next` is its last returned position. Otherwise `Next=H`,
  including empty pages. Checkpoint Next; checkpointing H from a truncated page loses work.
  `HasMore=false` means caught up through this page's H, and later calls may see newer writes.
- Negative positions, empty generations and invalid page sizes throw argument errors.
  Wrong generation/future position returns `InvalidCursor`; `C<F` returns `CursorExpired`.
  Missing activation, cancellation, integrity and database faults are explicit failures.
  The operation does not retry automatically; dispose/reload after failure.

## Retention and receiver restart

`CustomerProfileFeedSetup.PruneThroughAsync` owns a native transaction on a fresh admitted
Sales context. It takes the same feed gate, deletes through K and sets F=max(F,K) atomically.
Wrong generation/future cutoff fails. Repeated/older cutoffs never regress F. A cutoff within
a sequence gap is valid. C=F works; C<F expires even if the receiver once read that history.
A page already reading its old snapshot can finish consistently during pruning. The next
call observes the new floor. Retention does not pin subscribers or automatically recover them.

`FeedJournal` binds a subscription to an Organization/generation. The receiver keeps one
receipt per change identity, checks duplicate position/content, and commits receipts with
Next in its own local transaction. Native checkpoint Version predicates fence competing
readers. Stale pages cannot skip unprocessed suffixes or regress checkpoints. Equal already
applied pages have no second diagnostic effect; altered identities/content fail. Restart
reopens the journal, re-reads the committed cursor and repeats any rolled-back page.
Expired/invalid cursors stop without changing the journal. B1 supplies no initial population,
live-view availability rule, snapshot recovery or external exactly-once effect guarantee.

## Verification and extraction boundary

```bash
dotnet test --project samples/Wholesale/IntegrationFeedDemo.Tests/IntegrationFeedDemo.Tests.csproj
```

The suite uses the existing Testcontainers PostgreSQL 18.6 fixture with per-case databases
and native connections. Rootless Podman setup is in [development](../../../docs/development.md).
Proofs include the unsafe sequence/MAX counterexample, blocked/early-start late commits,
gaps, bounded pages, tenant isolation, participant/commit/cancellation rollback, retention
floor/races/rollback, duplicates, competing readers, and actual reader-process death/restart.
Forward migration adoption preserves existing profiles, audit, WF2 process and outbox.
CI runs this suite in Wholesale consumer composition.

The source mechanism and receipt policy remain consumer code. No Rootbolt feed package,
interface, template preset or second source is added. Gate/committed-head and snapshot page
checks are future extraction candidates; one sample's proof does not establish independently
adoptable library guarantees. [The B1 report](../../../docs/reports/b1-replayable-integration-feed.md)
records fresh results and limits; [the reviewed scope](../../../docs/plans/b1-replayable-integration-feed.md)
retains the contract/file/proof review.
