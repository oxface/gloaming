import { execFileSync, spawnSync } from "node:child_process";
import { isAbsolute, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { createPerformanceRun } from "./run-performance.ts";

const root = resolve(import.meta.dirname, "../..");
const nx = fileURLToPath(import.meta.resolve("nx"));
const directory = relative(root, process.env.INIT_CWD ?? root)
  .split(sep)
  .join("/");
const [target, ...args] = process.argv.slice(2);
if (target !== "test-dotnet" && target !== "performance") {
  throw new Error("Expected the test-dotnet or performance target.");
}

const explicitProjects = args.some((arg) =>
  /^(?:--projects|-p)(?:=|$)/.test(arg),
);
if (
  directory &&
  directory !== ".." &&
  !directory.startsWith("../") &&
  !isAbsolute(directory) &&
  !explicitProjects
) {
  // Nx 24.0.6 cannot use project globs with minimatch 11 (nrwl/nx#37727).
  // Use the inferred project roots to pass exact project names to Nx.
  const { graph } = JSON.parse(
    execFileSync(process.execPath, [nx, "graph", "--print"], {
      cwd: root,
      encoding: "utf8",
      maxBuffer: 16 * 1024 * 1024,
    }),
  ) as {
    graph: {
      nodes: Record<
        string,
        {
          name: string;
          data: { root: string; targets?: Record<string, unknown> };
        }
      >;
    };
  };
  const projects = Object.values(graph.nodes)
    .filter(
      ({ data }) =>
        data.targets?.[target] &&
        (data.root === directory || data.root.startsWith(directory + "/")),
    )
    .map(({ name }) => name);
  if (projects.length === 0) {
    throw new Error(`No ${target} projects under ${directory}.`);
  }
  args.push("--projects", projects.join(","));
}

const reports =
  target === "performance" &&
  !args.some((arg) => /^(?:--graph(?:=|$)|--help$)/.test(arg))
    ? resolve(
        root,
        process.env.GLOAMING_PERFORMANCE_RESULTS ?? createPerformanceRun(),
      )
    : undefined;
const result = spawnSync(
  process.execPath,
  [nx, "run-many", "-t", target, ...args],
  {
    cwd: root,
    stdio: "inherit",
    env: reports
      ? { ...process.env, GLOAMING_PERFORMANCE_RESULTS: reports }
      : process.env,
  },
);
if (reports) console.log(`Performance run reports: ${reports}`);
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
