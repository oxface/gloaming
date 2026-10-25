import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import test from "node:test";
import { load } from "js-yaml";
import picomatch from "picomatch";

const root = resolve(import.meta.dirname, "../..");
const filters = load(
  readFileSync(resolve(root, ".github/ci-paths.yml"), "utf8"),
) as Record<string, string[]>;

// The pinned action uses picomatch with dot=true and some-with-excludes.
// These policy cases cover our string-only filters, not arbitrary action inputs.
function matches(lane: string, path: string): boolean {
  const patterns = filters[lane];
  assert(patterns && patterns.every((pattern) => typeof pattern === "string"));
  const included = patterns.filter((pattern) => !pattern.startsWith("!"));
  const excluded = patterns
    .filter((pattern) => pattern.startsWith("!"))
    .map((pattern) => pattern.slice(1));
  return (
    picomatch(included, { dot: true })(path) &&
    !picomatch(excluded, { dot: true })(path)
  );
}

function runtime(path: string): boolean {
  return ["shared", "unmapped", "runtime"].some((lane) => matches(lane, path));
}

test("implementation changes in every runtime library dependency select the real consumer", () => {
  const pending = [
    "samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj",
  ];
  const visited = new Set<string>();
  while (pending.length > 0) {
    const project = pending.pop()!;
    if (visited.has(project)) continue;
    visited.add(project);
    const source = readFileSync(resolve(root, project), "utf8");
    for (const match of source.matchAll(
      /<ProjectReference\s+Include="([^"]+)"/g,
    )) {
      pending.push(
        relative(
          root,
          resolve(root, dirname(project), match[1]!.replaceAll("\\", "/")),
        ).replaceAll("\\", "/"),
      );
    }
  }
  const libraries = [...visited].filter((project) =>
    project.startsWith("src/Gloaming."),
  );
  assert(libraries.length > 0);
  for (const project of libraries) {
    const sourcePath = `${dirname(project)}/SelectionProbe.cs`;
    assert(runtime(sourcePath), `${sourcePath} must select runtime`);
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
