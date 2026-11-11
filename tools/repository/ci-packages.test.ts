import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";
import { load } from "js-yaml";
import {
  generatedPackageInputs,
  matches,
  packageLanes,
  selectPackages,
} from "./ci-packages.ts";
import type { Filters } from "./ci-packages.ts";

const root = resolve(import.meta.dirname, "../..");
const read = (path: string) => readFileSync(resolve(root, path), "utf8");
const filters = load(read(".github/ci-paths.yml")) as Filters;
const current = read("Directory.Packages.props");
const projects = execFileSync(
  "git",
  ["ls-files", "-z", "--", "*.csproj", "*.props", "*.targets"],
  { cwd: root, encoding: "utf8" },
)
  .split("\0")
  .filter(Boolean)
  .map((path) => ({ path, source: read(path) }));

function bump(id: string) {
  const after = current.replace(
    new RegExp(`(Include="${id.replaceAll(".", "\\.")}"\\s+Version=")([^"]+)"`),
    '$1$2-ci-probe"',
  );
  assert.notEqual(after, current, `Missing central package: ${id}`);
  return selectPackages(filters, current, after, projects);
}

test("central version changes bypass broad path fallbacks and select actual package owners", () => {
  assert(matches(filters, "package_versions", "Directory.Packages.props"));
  for (const lane of ["shared", "unmapped"])
    assert.equal(matches(filters, lane, "Directory.Packages.props"), false);
  const workflows = bump("Microsoft.Agents.AI.Workflows");
  assert.equal(workflows.full, false, workflows.reason);
  assert.deepEqual(workflows.lanes, [
    "Agents",
    "Evaluations",
    "packages",
    "repository_dotnet",
  ]);
  assert.deepEqual(bump("Microsoft.Extensions.AI.Evaluation.Reporting").lanes, [
    "Evaluations",
    "packages",
    "repository_dotnet",
  ]);
  assert.deepEqual(bump("Microsoft.Playwright").lanes, [
    "repository_dotnet",
    "runtime",
  ]);
  assert.deepEqual(bump("Aspire.Hosting.Testing").lanes, [
    "repository_dotnet",
    "runtime",
  ]);
  assert(
    bump("Microsoft.EntityFrameworkCore.Relational").lanes.includes("Agents"),
  );
  assert(bump("Npgsql").lanes.includes("wholesale"));
  assert(bump("Npgsql.EntityFrameworkCore.PostgreSQL").lanes.includes("kafka"));
  assert(
    bump("Microsoft.Extensions.DependencyInjection").lanes.includes("kafka"),
  );
});

test("every current central package has an owned version-update selection", () => {
  for (const [, id] of current.matchAll(
    /<PackageVersion\s+Include="([^"]+)"/g,
  )) {
    const result = bump(id!);
    assert.equal(result.full, false, `${id}: ${result.reason}`);
    assert(result.lanes.includes("repository_dotnet"), id!);
  }
});

test("multiple updates union consumers and preserve package-name case insensitivity", () => {
  const after = current.replace(
    'Include="Microsoft.Playwright"',
    'Include="microsoft.playwright"',
  );
  // Changing the declaration itself is structural and deliberately requests all lanes.
  assert.equal(selectPackages(filters, current, after, projects).full, true);
  const inputs = projects.map((project) => ({
    ...project,
    source: project.source.replaceAll(
      'Include="Microsoft.Playwright"',
      'Include="MICROSOFT.PLAYWRIGHT"',
    ),
  }));
  const bumped = current.replace(
    /(Include="Microsoft.Playwright" Version=")([^"]+)/,
    "$1$2-ci-probe",
  );
  assert.deepEqual(selectPackages(filters, current, bumped, inputs).lanes, [
    "repository_dotnet",
    "runtime",
  ]);
  const combined = bumped.replace(
    /(Include="Microsoft.Agents.AI.Workflows" Version=")([^"]+)/,
    "$1$2-ci-probe",
  );
  assert.deepEqual(selectPackages(filters, current, combined, inputs).lanes, [
    "Agents",
    "Evaluations",
    "packages",
    "repository_dotnet",
    "runtime",
  ]);
});

test("unsupported edits and unknown or shared ownership retain full coverage", () => {
  const changed = current.replace(
    /(Include="Microsoft.Playwright" Version=")([^"]+)/,
    "$1$2-ci-probe",
  );
  const cases: [string, typeof projects][] = [
    [current.replace("true", "false"), projects],
    [
      current.replace(
        "</ItemGroup>",
        '<PackageVersion Include="Unknown.Package" Version="1.0.0" /></ItemGroup>',
      ),
      projects,
    ],
    [current.replace(/<PackageVersion Include="Cronos"[^>]+\/>/, ""), projects],
    ["<Project>", projects],
    [changed, []],
    [
      changed,
      [
        {
          path: "Directory.Build.props",
          source:
            '<Project><ItemGroup><PackageReference Include="Microsoft.Playwright" /></ItemGroup></Project>',
        },
      ],
    ],
    [
      changed,
      [
        {
          path: "future/Tool.csproj",
          source:
            '<Project><ItemGroup><PackageReference Include="Microsoft.Playwright" /></ItemGroup></Project>',
        },
      ],
    ],
    [
      changed,
      [
        {
          path: "samples/Tool.csproj",
          source:
            '<Project><ItemGroup><PackageReference Include="$(DynamicPackage)" /></ItemGroup></Project>',
        },
      ],
    ],
  ];
  for (const [after, inputs] of cases) {
    const result = selectPackages(filters, current, after, inputs);
    assert.equal(result.full, true, result.reason);
    assert.deepEqual(result.lanes, packageLanes(filters));
  }
});

test("generated package inputs match their owning adoption scripts", () => {
  for (const [lane, file] of [
    ["packages", "verify.ts"],
    ["kafka", "verify-kafka.ts"],
  ] as const) {
    const source = read(`tools/release/${file}`);
    const ids = [
      ...source.matchAll(/nativePackage\(\s*root,\s*"([^"]+)"/g),
    ].map((match) => match[1]!);
    assert.equal(
      ids.length,
      source.match(/\bnativePackage\s*\(/g)?.length ?? 0,
      `${file} has non-literal generated inputs`,
    );
    assert.deepEqual(generatedPackageInputs[lane], [...new Set(ids)]);
  }
});

test("workflow unions dependency selection with every existing path/manual fallback", () => {
  const workflow = load(read(".github/workflows/changes.yml")) as {
    on: { workflow_call: { outputs: Record<string, unknown> } };
    jobs: {
      changes: {
        outputs: Record<string, string>;
        steps: {
          id?: string;
          if?: string;
          env?: Record<string, string>;
          run?: string;
        }[];
      };
    };
  };
  for (const lane of packageLanes(filters)) {
    assert(lane in workflow.on.workflow_call.outputs, lane);
    const expression = workflow.jobs.changes.outputs[lane]!;
    for (const clause of [
      "github.event_name == 'workflow_dispatch'",
      "steps.filter.outputs.shared == 'true'",
      "steps.filter.outputs.unmapped == 'true'",
      `steps.filter.outputs.${lane} == 'true'`,
      "steps.dependencies.outputs.full == 'true'",
      `steps.dependencies.outputs.${lane} == 'true'`,
    ])
      assert(expression.includes(clause), `${lane}: ${clause}`);
  }
  const step = workflow.jobs.changes.steps.find(
    (step) => step.id === "dependencies",
  )!;
  assert.equal(step.if, "steps.filter.outputs.package_versions == 'true'");
  assert.equal(
    step.env?.CI_BASE_SHA,
    "${{ github.event.pull_request.base.sha || github.event.before }}",
  );
  assert.equal(step.run, "node tools/repository/ci-packages.ts");
});

test("CLI writes full selection when the comparison commit is unavailable", () => {
  const scratch = mkdtempSync(join(tmpdir(), "gloaming-ci-packages-"));
  try {
    const output = join(scratch, "output");
    const result = spawnSync(
      process.execPath,
      [resolve(root, "tools/repository/ci-packages.ts")],
      {
        cwd: root,
        encoding: "utf8",
        env: {
          ...process.env,
          CI_BASE_SHA: "0".repeat(40),
          GITHUB_OUTPUT: output,
        },
      },
    );
    assert.equal(result.status, 0, result.stderr);
    const values = readFileSync(output, "utf8").trim().split("\n");
    for (const key of ["full", ...packageLanes(filters)])
      assert(values.includes(`${key}=true`), key);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});
