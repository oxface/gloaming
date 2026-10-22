# WF2 deployment continuity proof

Date: 2026-10-22. Base: `790a8b7`. Scope:
[bounded deployment continuity](../plans/wf2-deployment-continuity.md).

The [standalone consumer](../../src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/README.md)
now executes a real earlier/current process journey on PostgreSQL. Its Earlier and Current
projects compile separate assemblies; they share finite host wiring, while a compile-time
branch preserves the earlier DTO/continuation behavior and Current adds an optional state field.
Each command starts and exits a distinct process. Earlier persists workflow progress and admits
literal delayed replies before Current reads or continues that storage. The database is not
recreated during replacement. No library runtime, public interface or schema changed.

## Fresh outcomes

The new [process suite](../../src/Gloaming.Messaging/samples/WorkflowDeploymentDemo.Tests/DeploymentTests.cs)
passed **7/7** against real PostgreSQL 18.6 through local Podman. Tests use a new disposable
database per case. Build: .NET SDK 10.0.401, zero warnings/errors. CSharpier checked all five
new C#/project files successfully. `git diff --check` passed.

| New executable proof | Evidence |
| --- | --- |
| Compatible deployment with delayed reply | Earlier persists v1 input/state equal to checked-in literal fixtures and admits literal v1 reply JSON. Current equal-start returns retained progress without invoking Start; processing invokes only `prepared`, adds its optional annotation, and atomically records state/outcome/inbox completion. Original initial input, outgoing command and admitted envelope remain unchanged. |
| Retained human/operator pause | Earlier processes its reply into `AwaitingInput`. Current detached read makes no writes and its stored `approve` signal completes the same instance without replaying earlier methods or producing another request. |
| Incompatible definition | Current version 2 rejects version-1 reads and reply processing. Workflow row, outbox and original inbox envelope remain unchanged; native inbox failure accounting commits one failure. Reinstalling compatible Current processes the original reply. |
| Same version, renamed continuation | Read succeeds but reply processing rejects before invoking the method. Compatible Current recovers the retained reply unchanged. Matching version numbers and read probes do not certify compatibility. |
| Same version, changed reply schema | Read succeeds but processing rejects the retained schema-1 envelope. Reinstalling compatible Current recovers it without upcasting/replacing that admitted envelope. |
| Actual death after database save | Current pauses after native `SaveChangesAsync` issued its database writes, before inbox completion/commit. Another connection still sees the original state. Killing the process rolls back; a new Current process commits version 2 and inbox completion with no committed failure observation or duplicate outgoing request. |
| Missing required historical reply field | Literal malformed v1 JSON fails with `JsonException`; state, original envelope and outbox remain intact. Even Earlier cannot recover missing original business data. Neither build invents an outcome/default. |

The compatible case also proves Earlier can read Current's additive state shape, local drain
rejects `AwaitingInput`, and completed version-1 rows still reject version-2 typed reads after
local active/reply counts reach zero. The earlier-reader check does not certify arbitrary
rollback: a write through an older DTO can drop unknown optional fields. Signals retain the
existing read-before-retry obligation after ambiguous commit; no signal receipt was added.

Invocation from the worktree root:

```bash
dotnet build src/Gloaming.Messaging/samples/WorkflowDeploymentDemo.Tests/WorkflowDeploymentDemo.Tests.csproj --nologo -m:1
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test --project src/Gloaming.Messaging/samples/WorkflowDeploymentDemo.Tests/WorkflowDeploymentDemo.Tests.csproj --no-build --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet tool run csharpier check src/Gloaming.Messaging/samples/WorkflowDeploymentDemo src/Gloaming.Messaging/samples/WorkflowDeploymentDemo.Tests
```

The successful deployment log is `/tmp/wf2-deployment-test.log` (7 passed, 45.908 seconds).
The local sandbox needed escalation for the Podman socket/test IPC. An initial unprivileged
test command failed on named-pipe permission. Attempts forwarding runner arguments through `--`
or passing MSBuild `-m:1` to `dotnet test` discovered zero tests; those are **not** successful
verification. Build separately with `-m:1`, then use the exact test command above.

Existing workflow PostgreSQL protocol regression verification passed **38/38** in 9.288 seconds
with the same .NET/PostgreSQL/Podman environment. Its build also had zero warnings/errors; logs
are `/tmp/wf2-protocol-build.log` and `/tmp/wf2-protocol-regression.log`. This is a fresh rerun of
the existing protocol/transaction/inbox tests, distinct from the seven new process cases.
Earlier WF1/WF2 and local-module reports remain historical evidence for external business effects,
transport recovery and consumer compensation; this new slice does not inherit their topologies
as deployment proofs.

## Extraction and ownership

**No new reusable mechanism was proven.** Existing `EfWorkflow` already removes process lookup,
continuation selection, pending/outcome matching and request/state proposal mechanics. The native
inbox processor already owns reply save/commit and durable failure accounting. Current adopts
these mechanisms directly; an additional deployment wrapper would repeat their interfaces without
removing that work. The two binaries share finite sample wiring instead of duplicating it or
extracting a library application host.

Potential repeated mechanics were assessed concretely:

- Definition/continuation inventory is a native query over the provided row. The sample's two
  counts are small and intentionally local; wrapping them would imply readiness without solving
  admission, in-flight transport or business completion. No generic drain/readiness API selected.
- Retaining version-1 methods/JSON and restoring compatible code removes the need for a migration
  in this journey. No repeated state transformations or version dispatch emerged to justify an
  upgrader/registry. Bumping the version or renaming the binding was rejected by executable cases.
- Native transactions, service scopes, signal authorization and retry timing are consumer choices.
  The small finite command switch makes them visible; a commit/recovery facade would obscure the
  existing ambiguous-commit and signal-deduplication limits.
- Process launch and PostgreSQL provisioning use existing neutral test infrastructure. These are
  proof helpers, not a Gloaming runtime feature or consumer deployment dependency.

Library finding: preserve the existing bounded runner and document deployment obligations beside
it. Template finding: no new default template policy is justified; use representative persisted
fixtures/process tests when a template actually composes workflows. Sample finding: keeping a
compatible persisted definition works across stopped-old/started-new process boundaries, including
operator pauses, and a failed candidate can recover valid original replies by restoring compatibility.

## Review-worthy files and remaining gaps

Review the [shared executable host](../../src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/Application.cs),
the two project definitions/compile-time state difference, literal fixtures and process assertions
together. Review the [family deployment guide](../../src/Gloaming.Messaging/docs/workflow-deployments.md)
for the chosen compatible rollout/recovery procedure and comparison with draining.

`drain` is only a local workflow/reply observation: it does not quiesce starts/signals/producers,
dispatch the sample's retained outbox, observe a broker/renderer or fence new work. Global drain
remains a concrete consumer deployment procedure requiring its own transport proof. Retained
completed rows, late outcomes and duplicate starts still need compatible inspection/processing or
a separately designed history/retention policy. No data is deleted or version-stamped to bypass this.

The renderer is represented by trusted finite intake of a literal reply; this slice makes no
external rendering/broker guarantee. Simultaneous mixed-release workers, production schema
migrations, automatic state/input/ledger upgrades, definition routing, historical retention,
compatibility certification and production rollout remain unimplemented. The controlled crash
proves the pre-commit cut; it does not resolve an unavailable commit response. Root integration
must register the three new projects and add the process suite to the messaging CI lane.
