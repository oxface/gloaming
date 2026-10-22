# WF2 deployment continuity: bounded scope

Status: implemented for review on `feat/v1-workflow-continuity`, against `790a8b7`.
The owner authorized implementation and local commits as part of the v1 continuation.

The capability is continuing retained workflow progress after an application deployment. The
selected path keeps a compatible persisted definition in Current, rather than introducing a
migration engine or automatic multiversion runtime. Business semantics, admission, deployment
coordination, native transactions and recovery authority remain consumer-owned.

Deliverables:

- An independent family-local export consumer with separately compiled Earlier and Current
  executable builds. Earlier persists workflows and delayed replies; it exits before Current
  continues them against the same real PostgreSQL storage.
- Literal v1 input, state and reply fixtures, including a malformed required-field reply.
  Current adds an optional state field without rewriting initial input or earlier ledgers.
- Process proofs for compatible continuation, equal-start identity, paused-signal continuation,
  incompatible version rejection, same-version binding/schema failures, malformed original data,
  recovery by reinstalling compatible code, and death after native save before commit.
- A practical compatible rollout/recovery procedure and comparison with draining. Explicitly
  distinguish local active-work observations from globally quiesced transport readiness, and
  include retained completed instances in compatibility obligations.
- A concrete extraction assessment and report of fresh verification.

No scheduler, graph/replay runtime, generic state upgrader, definition registry, deployment
coordinator or production rollout is selected. External renderer/broker behavior and concurrent
mixed-release workers are outside this topology. Production schema migrations are not replaced
by the disposable setup command. Existing protocol proofs remain useful regression coverage but
are not evidence of the new process topology until rerun and reported separately.

Acceptance requires executable earlier/current process boundaries and PostgreSQL evidence; a
documentation-only change does not satisfy the scope. Candidate mechanisms must remove actual
consumer complexity at a net benefit. Repeated finite host setup, transaction ownership and
business/deployment policy alone do not justify new Gloaming abstractions.

See the [consumer](../../src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/README.md),
[family guidance](../../src/Gloaming.Messaging/docs/workflow-deployments.md) and
[slice report](../reports/wf2-deployment-continuity.md).
