import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { test } from "node:test";
import type { TestContext } from "node:test";
import { verifyPerformanceReports } from "./verify-performance.ts";

function performanceRun(t: TestContext, files: Record<string, string>) {
  const directory = mkdtempSync(join(tmpdir(), "gloaming-performance-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  for (const [name, contents] of Object.entries(files)) {
    const path = join(directory, name);
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, contents);
  }
  return directory;
}

const benchmarkExports = {
  "event-codec/results/Codec-report.html": "<html>codec</html>",
  "event-codec/results/Codec-report-full.json":
    '{"Benchmarks":[{"Statistics":{"N":1}}]}',
};

test("a single-suite run records only the selected reports", (t) => {
  const directory = performanceRun(t, benchmarkExports);
  const manifest = verifyPerformanceReports(
    directory,
    "events",
    "test-revision",
  );
  assert.deepEqual(JSON.parse(readFileSync(manifest, "utf8")), {
    runId: basename(directory),
    revision: "test-revision",
    reports: [
      {
        suite: "event-codec",
        files: [
          join("event-codec", "results", "Codec-report.html"),
          join("event-codec", "results", "Codec-report-full.json"),
        ],
      },
    ],
  });
});

test("a nonempty artifact fails verification when a selected suite is absent", (t) => {
  const directory = performanceRun(t, benchmarkExports);
  assert.throws(
    () => verifyPerformanceReports(directory, "all"),
    /messaging-postgres/,
  );
});

test("a benchmark export without measurements fails verification", (t) => {
  const directory = performanceRun(t, {
    ...benchmarkExports,
    "event-codec/results/Codec-report-full.json":
      '{"Benchmarks":[{"Statistics":null}]}',
  });
  assert.throws(
    () => verifyPerformanceReports(directory, "events"),
    /lacks measurements/,
  );
});

test("Wholesale requires a native test report alongside its k6 exports", (t) => {
  const directory = performanceRun(t, {
    "wholesale/measured.html": "<html>k6</html>",
    "wholesale/summary.json": '{"metrics":{"http_req_duration":{}}}',
  });
  assert.throws(
    () => verifyPerformanceReports(directory, "wholesale"),
    /Expected a \.trx report/,
  );
  writeFileSync(join(directory, "wholesale", "load.trx"), "<TestRun/>");
  assert.equal(
    verifyPerformanceReports(directory, "wholesale"),
    join(directory, "manifest.json"),
  );
});
