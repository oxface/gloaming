import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync } from "node:fs";
import { join, resolve } from "node:path";

const root = resolve(import.meta.dirname, "../..");

export function createPerformanceRun(): string {
  const directory = join(root, ".artifacts", "performance");
  mkdirSync(directory, { recursive: true });
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
  return mkdtempSync(join(directory, `${timestamp}-`));
}

if (import.meta.main) {
  const [suite, reportOption, ...args] = process.argv.slice(2);
  if (
    !suite ||
    (reportOption !== "--artifacts" && reportOption !== "--results-directory")
  ) {
    throw new Error(
      "Expected a suite name and native report directory option.",
    );
  }

  const reports = resolve(
    process.env.GLOAMING_PERFORMANCE_RESULTS ?? createPerformanceRun(),
    suite,
  );
  mkdirSync(reports, { recursive: true });

  const result = spawnSync("dotnet", [...args, reportOption, reports], {
    cwd: root,
    stdio: "inherit",
    env: { ...process.env, GLOAMING_PERFORMANCE_RESULTS: reports },
  });
  console.log(`${suite} reports: ${reports}`);
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
}
