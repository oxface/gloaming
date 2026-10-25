import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { removeOwnedContainers } from "./container-cleanup.ts";

test("owned cleanup removes volumes and attempts every resource after runtime failures", () => {
  const directory = mkdtempSync(join(tmpdir(), "gloaming-cleanup-test-"));
  try {
    const runtime = join(directory, "fixture-runtime");
    const calls = join(directory, "calls.jsonl");
    // Exercise the subprocess boundary without contacting a real container daemon.
    writeFileSync(
      runtime,
      `#!/usr/bin/env node
const fs = require('node:fs');
const args = process.argv.slice(2);
fs.appendFileSync(${JSON.stringify(calls)}, JSON.stringify(args) + '\\n');
const name = args.at(-1);
const failures = {
  dockerAbsent: 'Error response from daemon: No such container: dockerAbsent',
  podmanAbsent: 'Error: no container with name or ID "podmanAbsent" found: no such container',
  denied: 'permission denied while trying to connect to the Docker daemon socket',
  disconnected: 'Cannot connect to the Docker daemon. Is the docker daemon running?',
  ambiguous: 'Error response from daemon: No such container: ambiguous\\nconnection lost',
};
if (failures[name]) { console.error(failures[name]); process.exit(1); }
`,
      { mode: 0o700 },
    );
    const names = [
      "denied",
      "disconnected",
      "ambiguous",
      "dockerAbsent",
      "podmanAbsent",
      "created",
    ];
    const results = removeOwnedContainers(runtime, names);
    assert.deepEqual(
      results.map(({ name, outcome }) => ({ name, outcome })),
      [
        { name: "denied", outcome: "failed" },
        { name: "disconnected", outcome: "failed" },
        { name: "ambiguous", outcome: "failed" },
        { name: "dockerAbsent", outcome: "absent" },
        { name: "podmanAbsent", outcome: "absent" },
        { name: "created", outcome: "removed" },
      ],
    );
    assert.deepEqual(
      readFileSync(calls, "utf8")
        .trim()
        .split("\n")
        .map((line) => JSON.parse(line) as string[]),
      names.map((name) => ["rm", "--force", "--volumes", name]),
    );
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("an unavailable cleanup executable fails every attempted resource", () => {
  const directory = mkdtempSync(join(tmpdir(), "gloaming-cleanup-test-"));
  try {
    const results = removeOwnedContainers(join(directory, "missing"), [
      "broker",
      "database",
    ]);
    assert(results.every((result) => result.outcome === "failed"));
    assert(results.every((result) => result.exitCode === null));
    assert(results.every((result) => result.diagnostic.includes("ENOENT")));
    assert.equal(results.length, 2);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
