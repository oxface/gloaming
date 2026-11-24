import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { basename, join } from "node:path";

const suites = {
  events: "event-codec",
  messaging: "messaging-postgres",
  wholesale: "wholesale",
} as const;

function requireReport(path: string) {
  const file = statSync(path, { throwIfNoEntry: false });
  assert(file?.isFile() && file.size > 0, `Missing or empty report: ${path}`);
}

function reportsWithSuffix(directory: string, suffix: string) {
  const reports = readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(suffix))
    .map((entry) => entry.name)
    .sort();
  assert(reports.length > 0, `Expected a ${suffix} report in ${directory}`);
  return reports;
}

export function verifyPerformanceReports(
  directory: string,
  selection: string,
  revision = process.env.GITHUB_SHA,
) {
  assert(
    selection === "all" || Object.hasOwn(suites, selection),
    `Unknown performance selection: ${selection}`,
  );
  const selected =
    selection === "all"
      ? Object.values(suites)
      : [suites[selection as keyof typeof suites]];
  const reports = selected.map((suite) => {
    if (suite === "wholesale") {
      const files = [
        "measured.html",
        "summary.json",
        ...reportsWithSuffix(join(directory, suite), ".trx"),
      ].map((file) => join(suite, file));
      for (const file of files) requireReport(join(directory, file));

      const summary = JSON.parse(
        readFileSync(join(directory, suite, "summary.json"), "utf8"),
      ) as { metrics?: { http_req_duration?: unknown } };
      assert(
        summary?.metrics?.http_req_duration,
        "Wholesale summary lacks HTTP metrics",
      );
      return { suite, files };
    }

    const results = join(suite, "results");
    const files = reportsWithSuffix(
      join(directory, results),
      "-report-full.json",
    ).flatMap((name) => {
      const json = join(results, name);
      const html = join(results, name.replace(/-full\.json$/, ".html"));
      requireReport(join(directory, json));
      requireReport(join(directory, html));
      const report = JSON.parse(
        readFileSync(join(directory, json), "utf8"),
      ) as { Benchmarks?: { Statistics?: { N?: number } }[] };
      assert(
        Array.isArray(report?.Benchmarks) &&
          report.Benchmarks.length > 0 &&
          report.Benchmarks.every((entry) => {
            const samples = entry?.Statistics?.N;
            return (
              typeof samples === "number" &&
              Number.isInteger(samples) &&
              samples > 0
            );
          }),
        `Benchmark report lacks measurements: ${json}`,
      );
      return [html, json];
    });
    return { suite, files };
  });

  const manifest = join(directory, "manifest.json");
  writeFileSync(
    manifest,
    JSON.stringify(
      { runId: basename(directory), revision: revision ?? null, reports },
      null,
      2,
    ) + "\n",
  );
  return manifest;
}

if (import.meta.main) {
  const [directory, selection] = process.argv.slice(2);
  assert(
    directory && selection,
    "Usage: verify-performance.ts <run directory> <selection>",
  );
  const manifest = verifyPerformanceReports(directory, selection);
  console.log(`Verified performance reports in ${manifest}`);
}
