import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import test from "node:test";
import { load } from "js-yaml";
import { matches as matchFilters } from "./ci-packages.ts";

const root = resolve(import.meta.dirname, "../..");
const filters = load(
  readFileSync(resolve(root, ".github/ci-paths.yml"), "utf8"),
) as Record<string, string[]>;

function matches(lane: string, path: string): boolean {
  return matchFilters(filters, lane, path);
}

function runtime(path: string): boolean {
  return ["shared", "unmapped", "runtime"].some((lane) => matches(lane, path));
}

function projectClosure(start: string): string[] {
  const pending = [start];
  const visited = new Set<string>();
  while (pending.length > 0) {
    const project = pending.pop()!;
    if (visited.has(project)) continue;
    visited.add(project);
    const source = readFileSync(resolve(root, project), "utf8");
    const references = [
      ...source.matchAll(/<ProjectReference\b[^>]*\bInclude="([^"]+)"/g),
    ];
    assert.equal(
      references.length,
      source.match(/<ProjectReference\b/g)?.length ?? 0,
      `Unsupported project reference in ${project}`,
    );
    for (const match of references) {
      assert(
        !/[$*;]/.test(match[1]!),
        `Non-literal project reference in ${project}`,
      );
      pending.push(
        relative(
          root,
          resolve(root, dirname(project), match[1]!.replaceAll("\\", "/")),
        ).replaceAll("\\", "/"),
      );
    }
  }
  return [...visited];
}

test("implementation and package owners in every consumer dependency select its lane", () => {
  for (const file of ["ci.yml", "sample-runtime.yml"]) {
    const workflow = load(
      readFileSync(resolve(root, ".github/workflows", file), "utf8"),
    ) as {
      jobs: Record<
        string,
        {
          if?: string;
          env?: { FAMILY_TEST_COMMANDS?: string };
          steps?: { run?: string }[];
        }
      >;
    };
    for (const job of Object.values(workflow.jobs)) {
      const lane = job.if?.match(/needs\.changes\.outputs\.(\w+)/)?.[1];
      const commands = [
        job.env?.FAMILY_TEST_COMMANDS,
        ...(job.steps ?? []).map((step) => step.run),
      ].join("\n");
      const projects = [...commands.matchAll(/--project\s+(\S+\.csproj)/g)].map(
        (match) => match[1]!,
      );
      if (!lane || lane === "repository_dotnet" || projects.length === 0)
        continue;
      for (const project of projects)
        for (const dependency of projectClosure(project)) {
          assert(
            matches(lane, dependency),
            `${dependency} must select ${lane} for package updates`,
          );
          assert(
            matches(lane, `${dirname(dependency)}/SelectionProbe.cs`),
            `${dependency} must select ${lane} for source changes`,
          );
        }
    }
  }
});

test("runtime selection keeps documentation and unrelated library suites focused", () => {
  const cases: [string, boolean][] = [
    [
      "src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorContextPolicyEvaluator.cs",
      true,
    ],
    [
      "src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/TenantRlsInterceptor.cs",
      true,
    ],
    ["samples/Wholesale/Api/Program.cs", true],
    [".github/workflows/sample-runtime.yml", true],
    ["future-tool/input.json", true],
    [
      "src/Gloaming.ActorIdentity/tests/ActorIdentityTests/AccessorTests.cs",
      false,
    ],
    [
      "src/Gloaming.Messaging/Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres/RecurrenceScope.cs",
      false,
    ],
    [
      "src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore.Postgres/PostgresEventFeed.cs",
      false,
    ],
    [
      "src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md",
      false,
    ],
    ["samples/Wholesale/README.md", false],
    ["docs/development.md", false],
  ];
  for (const [path, expected] of cases)
    assert.equal(runtime(path), expected, path);
});

test("evaluation runner selects its native and package proofs without application runtime", () => {
  for (const path of [
    "src/Gloaming.Evaluations/Gloaming.Evaluations/EvaluationRunner.cs",
    "src/Gloaming.Evaluations/samples/WorkflowEvaluationDemo/DocumentEvaluations.cs",
  ]) {
    assert(matches("Evaluations", path), path);
    assert(matches("packages", path), path);
    assert.equal(matches("Agents", path), false, path);
    assert.equal(runtime(path), false, path);
  }
});

test("agent persistence selects native proofs without requiring Wholesale runtime", () => {
  for (const path of [
    "src/Gloaming.Agents/Gloaming.Agents.EntityFrameworkCore/EfChatHistoryProvider.cs",
    "src/Gloaming.Agents/Gloaming.Agents.Workflows/NativeWorkflowExecution.cs",
    "src/Gloaming.Agents/samples/AgentPersistenceDemo/AdoptionJourney.cs",
    "src/Gloaming.Conversations/Gloaming.Conversations.EntityFrameworkCore/EfConversationTranscript.cs",
  ]) {
    assert(matches("Agents", path), path);
    assert(matches("packages", path), path);
    assert.equal(runtime(path), false, path);
  }
  assert.equal(matches("Agents", "src/Gloaming.Agents/README.md"), false);
});

test("runtime selection retains manual and conservative fallbacks", () => {
  const workflow = load(
    readFileSync(resolve(root, ".github/workflows/changes.yml"), "utf8"),
  ) as {
    jobs: {
      changes: {
        outputs: { runtime: string };
        steps: { with?: Record<string, string> }[];
      };
    };
  };
  const { outputs, steps } = workflow.jobs.changes;
  assert(outputs.runtime.includes("github.event_name == 'workflow_dispatch'"));
  for (const lane of ["shared", "unmapped", "runtime"])
    assert(outputs.runtime.includes(`steps.filter.outputs.${lane} == 'true'`));
  assert(
    steps.some(
      (step) => step.with?.["predicate-quantifier"] === "some-with-excludes",
    ),
  );
});

test("shared process and provider fixture changes select the persistence adopter", () => {
  for (const path of [
    "tests/Support/ChildHost.cs",
    "tests/Support/PostgreSqlFixture.cs",
  ])
    assert(matches("Persistence", path), `${path} must select Persistence`);
});

test("native JSON ownership consumer changes select provider and package proofs", () => {
  for (const path of [
    "src/Gloaming.Persistence/tests/Fixtures/JsonOwnership/JsonDocumentsContext.cs",
    "src/Gloaming.Persistence/tests/Fixtures/JsonOwnership/JsonOwnershipJourney.cs",
  ]) {
    assert(matches("Persistence", path), path);
    assert(matches("packages", path), path);
  }
});

test("Kafka adapter, wire fixtures and recipe select native package adoption", () => {
  for (const path of [
    "tools/release/verify-kafka.ts",
    "tools/release/consumers/Kafka/KafkaAdapter.cs",
    "tools/release/consumers/Kafka/Fixtures/changed.v1.json",
    "modular-monolith-patterns/kafka.md",
    "src/Gloaming.Messaging/Gloaming.Messaging/IMessagePublisher.cs",
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs",
  ])
    assert(matches("kafka", path), `${path} must select native package proofs`);

  const workflow = load(
    readFileSync(resolve(root, ".github/workflows/packages.yml"), "utf8"),
  ) as {
    jobs: {
      kafka: {
        needs: string;
        if: string;
        env: { GLOAMING_CONTAINER_RUNTIME: string };
        steps: { run?: string }[];
      };
    };
  };
  assert.equal(workflow.jobs.kafka.needs, "changes");
  assert(
    workflow.jobs.kafka.if.includes("needs.changes.outputs.kafka == 'true'"),
  );
  assert(workflow.jobs.kafka.if.includes("needs.changes.result != 'success'"));
  assert.equal(workflow.jobs.kafka.env.GLOAMING_CONTAINER_RUNTIME, "docker");
  assert(
    workflow.jobs.kafka.steps.some(
      (step) => step.run === "npm --prefix tools/release run verify:kafka",
    ),
  );
});

test("Kafka library closure selects its package proof without unrelated suites", () => {
  for (const path of projectClosure(
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/Gloaming.Messaging.EntityFrameworkCore.Postgres.csproj",
  ))
    assert(matches("kafka", path), path);
  for (const path of [
    "src/Gloaming.Agents/Gloaming.Agents.Workflows/NativeWorkflowExecution.cs",
    "src/Gloaming.Evaluations/Gloaming.Evaluations/EvaluationRunner.cs",
    "src/Gloaming.Messaging/Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres/RecurrenceScope.cs",
  ]) {
    assert.equal(matches("kafka", path), false, path);
    assert.equal(matches("wholesale", path), false, path);
  }
});
