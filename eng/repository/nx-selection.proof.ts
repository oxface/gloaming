import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import test from "node:test";

const root = resolve(import.meta.dirname, "../..");
const nxPackage = JSON.parse(
  readFileSync(resolve(root, "node_modules/nx/package.json"), "utf8"),
) as { bin: { nx: string } };

function nx<T>(...args: string[]): T {
  return JSON.parse(
    execFileSync(
      process.execPath,
      [resolve(root, "node_modules/nx", nxPackage.bin.nx), ...args],
      {
        cwd: root,
        encoding: "utf8",
        env: {
          ...process.env,
          NX_DAEMON: "false",
          NX_ISOLATE_PLUGINS: "false",
        },
        timeout: 120_000,
        maxBuffer: 16 * 1024 * 1024,
      },
    ),
  ) as T;
}

function affected(path: string, target = "test-dotnet"): string[] {
  return nx<string[]>(
    "show",
    "projects",
    "--affected",
    "-t",
    target,
    `--files=${path}`,
    "--json",
  );
}

test("Nx discovers every declared test project and the solution includes it", () => {
  const paths = execFileSync("git", ["ls-files", "--", "*.csproj"], {
    cwd: root,
    encoding: "utf8",
  })
    .trim()
    .split("\n");
  const expected = paths.filter((path) =>
    /<IsTestProject>true<\/IsTestProject>/.test(
      readFileSync(resolve(root, path), "utf8"),
    ),
  );
  const solutionProjects = new Set(
    execFileSync("dotnet", ["sln", "Gloaming.slnx", "list"], {
      cwd: root,
      encoding: "utf8",
    })
      .split(/\r?\n/)
      .map((line) => line.replaceAll("\\", "/"))
      .filter((line) => line.endsWith(".csproj")),
  );
  for (const path of expected)
    assert(
      solutionProjects.has(path),
      `${path} must be built by Gloaming.slnx`,
    );

  const { graph } = nx<{
    graph: {
      nodes: Record<
        string,
        { data: { root: string; targets?: Record<string, unknown> } }
      >;
    };
  }>("graph", "--print");
  const actualRoots = Object.values(graph.nodes)
    .filter(({ data }) => Object.hasOwn(data.targets ?? {}, "test-dotnet"))
    .map(({ data }) => data.root)
    .sort();
  assert.deepEqual(actualRoots, expected.map((path) => dirname(path)).sort());
});

test("a leaf implementation selects its consumer without unrelated tests", () => {
  assert.deepEqual(
    affected(
      "src/Gloaming.Evaluations/Gloaming.Evaluations/EvaluationRunner.cs",
    ),
    ["EvaluationTests"],
  );
  const selected = affected(
    "src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/ActorId.cs",
  );
  for (const name of [
    "ActorIdentityTests",
    "AuditPostgresTests",
    "ContextCompositionTests",
    "Wholesale.IntegrationTests",
    "RuntimeComposition.Tests",
  ])
    assert(selected.includes(name), name);
  assert(!selected.includes("EvaluationTests"));
});

test("linked C# inputs reach their provider consumers", () => {
  const selected = affected("tests/Support/PostgreSqlFixture.cs");
  for (const name of [
    "AuditPostgresTests",
    "WorkflowDeploymentTests",
    "Wholesale.IntegrationTests",
  ])
    assert(selected.includes(name), name);
  assert(!selected.includes("EvaluationTests"));
});

test("Kafka package adoption selects its package closure and proof inputs", () => {
  for (const path of [
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs",
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/ScopeProbe.cs",
    "src/Gloaming.Messaging/Gloaming.Messaging/ScopeProbe.cs",
    "src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/ScopeProbe.cs",
    "eng/release/verify-kafka.ts",
    "modular-monolith-patterns/kafka.md",
  ])
    assert(affected(path, "verify:kafka").includes("release-proofs"), path);

  for (const path of [
    "src/Gloaming.Evaluations/Gloaming.Evaluations/EvaluationRunner.cs",
    "src/Gloaming.Persistence/tests/Fixtures/JsonOwnership/previous.json",
    "README.md",
  ])
    assert(!affected(path, "verify:kafka").includes("release-proofs"), path);
});

test("Wholesale runtime selection covers native references and runtime-only inputs", () => {
  const pending = [
    "samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj",
  ];
  const seen = new Set<string>();
  while (pending.length) {
    const path = pending.pop()!;
    if (seen.has(path)) continue;
    seen.add(path);
    const source = readFileSync(resolve(root, path), "utf8");
    const references = [
      ...source.matchAll(/<ProjectReference\b[^>]*Include="([^"]+)"/g),
    ];
    assert.equal(
      references.length,
      source.match(/<ProjectReference\b/g)?.length ?? 0,
      path,
    );
    for (const match of references) {
      assert(!/[$*;]/.test(match[1]!), path);
      pending.push(
        relative(root, resolve(root, dirname(path), match[1]!)).replaceAll(
          "\\",
          "/",
        ),
      );
    }
    assert(
      affected(`${dirname(path)}/ScopeProbe.cs`).includes(
        "RuntimeComposition.Tests",
      ),
      path,
    );
  }
  for (const path of [
    "samples/Wholesale/Worker/Program.cs",
    "samples/Wholesale/AppHost/Keycloak/wholesale-realm.json",
    "samples/Wholesale/AppHost/Keycloak/deleted-realm.json",
    "aspire.config.json",
  ])
    assert(affected(path).includes("RuntimeComposition.Tests"), path);
});

test("retained None fixtures select deployment and codec consumers conservatively", () => {
  for (const path of [
    "src/Gloaming.Messaging/tests/Fixtures/WorkflowDeployment/fixtures/state-v1.json",
    "src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/opened.v1.json",
    "src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/deleted-input.json",
  ]) {
    const selected = affected(path);
    for (const name of [
      "WorkflowDeploymentTests",
      "EventCodecDemo.Tests",
      "Wholesale.IntegrationTests",
    ])
      assert(selected.includes(name), `${path}: ${name}`);
  }
});

test("central package and shared build changes select all native tests", () => {
  const all = nx<string[]>(
    "show",
    "projects",
    "--with-target=test-dotnet",
    "--json",
  ).sort();
  for (const path of ["Directory.Packages.props", "Directory.Build.props"])
    assert.deepEqual(affected(path).sort(), all, path);
});

test("provider tests run freshly and ordinary commands leave special environments to CI", () => {
  const project = nx<{
    targets: { "test-dotnet": { cache: boolean; dependsOn: string[] } };
  }>("show", "project", "AuditPostgresTests", "--json");
  assert.equal(project.targets["test-dotnet"].cache, false);
  assert(project.targets["test-dotnet"].dependsOn.includes("build"));
  const ordinary = nx<string[]>(
    "show",
    "projects",
    "--with-target=test-dotnet",
    "--exclude=RuntimeComposition.Tests,ArchitectureTests",
    "--json",
  );
  assert(ordinary.includes("Wholesale.IntegrationTests"));
  assert(!ordinary.includes("RuntimeComposition.Tests"));
  assert(!ordinary.includes("ArchitectureTests"));
});
