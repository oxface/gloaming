import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { load } from "js-yaml";
import picomatch from "picomatch";

const root = resolve(import.meta.dirname, "../..");
const read = (path: string) => readFileSync(resolve(root, path), "utf8");
const filters = load(read(".github/ci-paths.yml")) as Record<string, string[]>;
const { jobs } = load(read(".github/workflows/ci.yml")) as {
  jobs: Record<
    string,
    {
      name: string;
      needs?: string | string[];
      if?: string;
      outputs?: Record<string, string>;
      steps: { id?: string; env?: Record<string, string>; run?: string }[];
    }
  >;
};
function matches(lane: string, path: string): boolean {
  const patterns = filters[lane]!;
  return (
    picomatch(
      patterns.filter((p) => !p.startsWith("!")),
      { dot: true },
    )(path) &&
    !picomatch(
      patterns.filter((p) => p.startsWith("!")).map((p) => p.slice(1)),
      { dot: true },
    )(path)
  );
}

test("only environment-specific scopes remain and uncertain inputs retain full coverage", () => {
  assert.deepEqual(Object.keys(filters).sort(), [
    "kafka",
    "packages",
    "repository_dotnet",
    "shared",
    "unmapped",
  ]);
  for (const path of [
    "nx.json",
    "package-lock.json",
    "Directory.Packages.props",
    "future/build-input.json",
    "tests/future/input.json",
  ])
    assert(matches("shared", path) || matches("unmapped", path), path);
  assert(!matches("shared", "README.md"));
  assert(!matches("unmapped", "README.md"));
});

test("package artifacts and native Kafka inputs keep their separate proofs", () => {
  for (const path of [
    "src/Gloaming.Messaging/README.md",
    "src/Gloaming.Persistence/tests/Fixtures/JsonOwnership/previous.json",
    "src/Gloaming.Persistence/tests/Fixtures/RelationalOwnership/RelationalOwnershipJourney.cs",
    "eng/release/verify.ts",
  ])
    assert(matches("packages", path), path);
  for (const path of [
    "modular-monolith-patterns/kafka.md",
    "eng/release/verify-kafka.ts",
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs",
  ])
    assert(matches("kafka", path), path);
  assert(
    !matches(
      "kafka",
      "src/Gloaming.Evaluations/Gloaming.Evaluations/EvaluationRunner.cs",
    ),
  );
  const proxy = "eng/release/consumers/Kafka/BrokerProxy.cs";
  const proxyTest = "eng/release/tests/BrokerProxyTests.cs";
  assert(matches("repository_dotnet", proxy));
  assert(matches("kafka", proxy));
  assert(matches("repository_dotnet", proxyTest));
  assert(!matches("shared", proxyTest));
  assert(!matches("unmapped", proxyTest));
  assert(!matches("packages", proxyTest));
  assert(!matches("kafka", proxyTest));
});

function assertClosure(scope: string, start: string): void {
  const pending = [start];
  const seen = new Set<string>();
  while (pending.length) {
    const path = pending.pop()!;
    if (seen.has(path)) continue;
    seen.add(path);
    assert(matches(scope, path), path);
    assert(matches(scope, `${dirname(path)}/SelectionProbe.cs`), path);
    const source = read(path);
    const references = [
      ...source.matchAll(/<ProjectReference\b[^>]*Include="([^"]+)"/g),
    ];
    assert.equal(
      references.length,
      source.match(/<ProjectReference\b/g)?.length ?? 0,
    );
    for (const match of references) {
      assert(!/[$*;]/.test(match[1]!), `Unsupported reference: ${path}`);
      const dependency = resolve(
        root,
        dirname(path),
        match[1]!.replaceAll("\\", "/"),
      );
      pending.push(dependency.slice(root.length + 1));
    }
  }
}

test("Kafka scope covers the native libraries consumed through packages", () => {
  assertClosure(
    "kafka",
    "src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/Gloaming.Messaging.EntityFrameworkCore.Postgres.csproj",
  );
});

test("CI accepts an older successful ancestor and falls back for an unusable baseline", () => {
  const script = jobs.changes!.steps.find(({ id }) => id === "range")?.run;
  assert(script);
  const [head, previous] = execFileSync(
    "git",
    ["rev-list", "--first-parent", "-n", "2", "HEAD"],
    { cwd: root, encoding: "utf8" },
  )
    .trim()
    .split("\n");
  assert(head);
  const cases: [base: string, commit: string, output: string][] = [
    ["", head, ""],
    [head, head, ""],
    ["0".repeat(40), head, ""],
  ];
  if (previous)
    cases.push([previous, head, `base=${previous}\n`], [head, previous, ""]);

  const directory = mkdtempSync(resolve(tmpdir(), "gloaming-ci-base-"));
  try {
    const output = resolve(directory, "output");
    for (const [base, commit, expected] of cases) {
      rmSync(output, { force: true });
      const message = execFileSync("bash", ["-e", "-c", script], {
        cwd: root,
        encoding: "utf8",
        env: {
          ...process.env,
          CI_BASE: base,
          GITHUB_SHA: commit,
          GITHUB_OUTPUT: output,
        },
      });
      assert.equal(
        existsSync(output) ? readFileSync(output, "utf8") : "",
        expected,
      );
      if (!expected)
        assert.match(message, /Running all checks eligible for this event/);
    }
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("the scope summary and outputs agree on affected coverage and full fallbacks", () => {
  const script = jobs.changes!.steps.find(({ id }) => id === "plan")?.run;
  assert(script);
  const base = "b".repeat(40);
  const head = "a".repeat(40);
  const empty = {
    full: "false",
    repository_dotnet: "false",
    packages: "false",
    kafka: "false",
    runtime: "false",
  };
  const full = {
    full: "true",
    repository_dotnet: "true",
    packages: "true",
    kafka: "true",
    runtime: "true",
  };
  const cases: [NodeJS.ProcessEnv, typeof empty, string][] = [
    [{}, empty, "Changed paths and affected projects"],
    [
      { CI_REPOSITORY_DOTNET: "true", CI_RUNTIME: "true" },
      { ...empty, repository_dotnet: "true", runtime: "true" },
      "Changed paths and affected projects",
    ],
    [{ CI_BASE: "" }, full, "No usable comparison base"],
    [
      { CI_SHARED: "true" },
      full,
      "Shared build, project, or CI inputs changed",
    ],
    [{ CI_UNMAPPED: "true" }, full, "Unmapped paths changed"],
    [
      { GITHUB_EVENT_NAME: "workflow_dispatch", CI_BASE: "" },
      full,
      "Manual dispatch",
    ],
    [
      { GITHUB_EVENT_NAME: "push", CI_BASE: "" },
      { ...full, runtime: "false" },
      "No usable comparison base",
    ],
  ];

  const directory = mkdtempSync(resolve(tmpdir(), "gloaming-ci-scope-"));
  try {
    const output = resolve(directory, "output");
    const summary = resolve(directory, "summary");
    for (const [overrides, expected, reason] of cases) {
      rmSync(output, { force: true });
      rmSync(summary, { force: true });
      execFileSync("bash", ["-e", "-c", script], {
        cwd: root,
        env: {
          ...process.env,
          CI_BASE: base,
          CI_SHARED: "false",
          CI_UNMAPPED: "false",
          CI_REPOSITORY_DOTNET: "false",
          CI_PACKAGES: "false",
          CI_KAFKA: "false",
          CI_RUNTIME: "false",
          GITHUB_EVENT_NAME: "pull_request",
          ...overrides,
          GITHUB_SHA: head,
          GITHUB_OUTPUT: output,
          GITHUB_STEP_SUMMARY: summary,
        },
      });
      assert.deepEqual(
        Object.fromEntries(
          readFileSync(output, "utf8")
            .trim()
            .split("\n")
            .map((line) => line.split("=")),
        ),
        expected,
      );
      const report = readFileSync(summary, "utf8");
      assert(report.includes(reason), report);
      assert(
        report.includes(overrides.CI_BASE === "" ? "Base `unavailable`" : base),
        report,
      );
      assert(report.includes(head), report);
      assert(
        report.includes(`| Wholesale runtime | ${expected.runtime} |`),
        report,
      );
    }
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("the four CI checks share scope and Repository integrity depends on every proof", () => {
  assert.deepEqual(Object.keys(jobs).sort(), [
    "changes",
    "kafka",
    "packages",
    "repository",
    "runtime",
  ]);
  for (const name of ["kafka", "packages", "runtime"]) {
    assert.equal(jobs[name]?.needs, "changes");
    assert.equal(jobs[name]?.if, `needs.changes.outputs.${name} == 'true'`);
    assert.equal(
      jobs.changes?.outputs?.[name],
      `\${{ steps.plan.outputs.${name} }}`,
    );
  }

  const repository = jobs.repository!;
  assert.equal(repository.name, "Repository integrity");
  assert.equal(repository.if, "${{ !cancelled() }}");
  assert.deepEqual(repository.needs, [
    "changes",
    "packages",
    "kafka",
    "runtime",
  ]);
  assert.equal(repository.steps[0]?.id, "gate");
  assert.deepEqual(repository.steps[0]?.env, {
    SCOPE_RESULT: "${{ needs.changes.result }}",
    PACKAGES_SELECTED: "${{ needs.changes.outputs.packages }}",
    PACKAGES_RESULT: "${{ needs.packages.result }}",
    KAFKA_SELECTED: "${{ needs.changes.outputs.kafka }}",
    KAFKA_RESULT: "${{ needs.kafka.result }}",
    RUNTIME_SELECTED: "${{ needs.changes.outputs.runtime }}",
    RUNTIME_RESULT: "${{ needs.runtime.result }}",
  });
});

test("Repository integrity fails on scope errors and unexpected proof statuses", () => {
  const script = jobs.repository!.steps[0]?.run;
  assert(script);
  const skipped = {
    SCOPE_RESULT: "success",
    PACKAGES_SELECTED: "false",
    PACKAGES_RESULT: "skipped",
    KAFKA_SELECTED: "false",
    KAFKA_RESULT: "skipped",
    RUNTIME_SELECTED: "false",
    RUNTIME_RESULT: "skipped",
  };
  const verify = (overrides: NodeJS.ProcessEnv, message?: string) => {
    const result = spawnSync("bash", ["-e", "-c", script], {
      cwd: root,
      encoding: "utf8",
      env: { ...process.env, ...skipped, ...overrides },
    });
    assert.ifError(result.error);
    assert.equal(result.status, message ? 1 : 0, result.stdout);
    if (message) assert(result.stdout.includes(message), result.stdout);
  };

  verify({});
  for (const result of ["failure", "cancelled", "skipped"])
    verify({ SCOPE_RESULT: result }, `CI scope was ${result}`);

  for (const proof of ["PACKAGES", "KAFKA", "RUNTIME"]) {
    const selected = `${proof}_SELECTED`;
    const status = `${proof}_RESULT`;
    verify({ [selected]: "true", [status]: "success" });
    for (const result of ["failure", "cancelled", "skipped"])
      verify(
        { [selected]: "true", [status]: result },
        `was ${result} (expected success)`,
      );
    verify({ [status]: "success" }, "was success (expected skipped)");
    verify({ [selected]: "" }, "Missing or invalid selection");
  }
});
