import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { appendFileSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { XMLParser, XMLValidator } from "fast-xml-parser";
import { load } from "js-yaml";
import picomatch from "picomatch";

export type Filters = Record<string, string[]>;

// These adoption projects are generated outside the checkout. Tests compare the
// explicit inputs with nativePackage calls in their owning proof scripts.
export const generatedPackageInputs: Record<string, string[]> = {
  packages: [
    "Microsoft.Extensions.DependencyInjection",
    "Npgsql.EntityFrameworkCore.PostgreSQL",
    "Microsoft.Agents.AI",
    "Microsoft.Agents.AI.Workflows",
    "Microsoft.Extensions.AI.Evaluation.Reporting",
  ],
  kafka: ["Microsoft.Extensions.DependencyInjection"],
};

export function matches(filters: Filters, lane: string, path: string): boolean {
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

function xml(source: string): unknown {
  assert.equal(XMLValidator.validate(source), true, "Invalid MSBuild XML");
  return new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: "@",
    parseTagValue: false,
  }).parse(source) as unknown;
}

function elements(value: unknown, name: string): Record<string, unknown>[] {
  if (!value || typeof value !== "object") return [];
  if (Array.isArray(value))
    return value.flatMap((item) => elements(item, name));
  return Object.entries(value).flatMap(([key, child]) =>
    key === name
      ? (Array.isArray(child) ? child : [child]).map((item) => {
          assert(item && typeof item === "object");
          return item as Record<string, unknown>;
        })
      : elements(child, name),
  );
}

function central(source: string) {
  const document = xml(source);
  const versions = new Map<string, string>();
  for (const entry of elements(document, "PackageVersion")) {
    const id = entry["@Include"];
    const version = entry["@Version"];
    assert(typeof id === "string" && /^[\w.-]+$/.test(id));
    assert(typeof version === "string" && /^[\w.+-]+$/.test(version));
    assert(!versions.has(id.toLowerCase()), `Duplicate central package: ${id}`);
    versions.set(id.toLowerCase(), version);
    entry["@Version"] = "<version>";
  }
  assert(versions.size > 0, "No central package versions");
  return { versions, structure: JSON.stringify(document) };
}

export function packageLanes(filters: Filters): string[] {
  return Object.keys(filters).filter(
    (lane) => !["shared", "unmapped", "package_versions"].includes(lane),
  );
}

// This supplements the folder map for version-only edits, never suppressing its lanes.
// Unknown ownership or unsupported central edits retain full coverage.
export function selectPackages(
  filters: Filters,
  before: string,
  after: string,
  projects: readonly { path: string; source: string }[],
): { full: boolean; lanes: string[]; reason: string } {
  try {
    const old = central(before);
    const current = central(after);
    assert.equal(
      old.structure,
      current.structure,
      "Central package structure changed",
    );
    const changed = new Set(
      [...current.versions.keys()].filter(
        (id) => old.versions.get(id) !== current.versions.get(id),
      ),
    );
    const owned = new Set<string>();
    const selected = new Set(["repository_dotnet"]);
    for (const [lane, ids] of Object.entries(generatedPackageInputs))
      for (const id of ids)
        if (changed.has(id.toLowerCase())) {
          owned.add(id.toLowerCase());
          selected.add(lane);
        }
    for (const { path, source } of projects) {
      const references = elements(xml(source), "PackageReference");
      let affected = false;
      for (const reference of references) {
        const id = reference["@Include"] ?? reference["@Update"];
        assert(typeof id === "string" && /^[\w.-]+$/.test(id));
        if (changed.has(id.toLowerCase())) {
          owned.add(id.toLowerCase());
          affected = true;
        }
      }
      if (!affected) continue;
      assert(
        path.endsWith(".csproj"),
        "A shared import owns a changed package",
      );
      assert(
        !matches(filters, "unmapped", path),
        `Unmapped package owner: ${path}`,
      );
      for (const lane of packageLanes(filters))
        if (matches(filters, lane, path)) selected.add(lane);
    }
    assert(
      [...changed].every((id) => owned.has(id)),
      "Unknown package ownership",
    );
    return {
      full: false,
      lanes: [...selected].sort(),
      reason: `Changed central versions: ${[...changed].join(", ") || "none"}`,
    };
  } catch (error) {
    return {
      full: true,
      lanes: packageLanes(filters),
      reason: error instanceof Error ? error.message : String(error),
    };
  }
}

function main() {
  const root = resolve(import.meta.dirname, "../..");
  const filters = load(
    readFileSync(resolve(root, ".github/ci-paths.yml"), "utf8"),
  ) as Filters;
  let selection;
  try {
    const base = process.env.CI_BASE_SHA;
    assert(
      base && /^[a-f0-9]{40,64}$/.test(base) && !/^0+$/.test(base),
      "Missing comparison commit",
    );
    const before = execFileSync(
      "git",
      ["show", `${base}:Directory.Packages.props`],
      {
        cwd: root,
        encoding: "utf8",
      },
    );
    const paths = execFileSync(
      "git",
      ["ls-files", "-z", "--", "*.csproj", "*.props", "*.targets"],
      { cwd: root, encoding: "utf8" },
    )
      .split("\0")
      .filter(Boolean);
    selection = selectPackages(
      filters,
      before,
      readFileSync(resolve(root, "Directory.Packages.props"), "utf8"),
      paths.map((path) => ({
        path,
        source: readFileSync(resolve(root, path), "utf8"),
      })),
    );
  } catch (error) {
    selection = {
      full: true,
      lanes: packageLanes(filters),
      reason: error instanceof Error ? error.message : String(error),
    };
  }
  console.log(JSON.stringify(selection));
  if (process.env.GITHUB_OUTPUT)
    appendFileSync(
      process.env.GITHUB_OUTPUT,
      [
        `full=${selection.full}`,
        ...packageLanes(filters).map(
          (lane) => `${lane}=${selection.lanes.includes(lane)}`,
        ),
      ].join("\n") + "\n",
    );
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  main();
