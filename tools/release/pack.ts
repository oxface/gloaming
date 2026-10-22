import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import {
  constants,
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";
import { libraryPackages } from "./packages.ts";

const root = resolve(import.meta.dirname, "../..");
const { values } = parseArgs({
  options: {
    output: { type: "string", short: "o" },
    help: { type: "boolean", short: "h" },
  },
  allowPositionals: false,
});

if (values.help) {
  console.log(`Usage: npm --prefix tools/release run pack -- [--output DIRECTORY]

Pack all Gloaming libraries at one unique prerelease version without running tests.
Default feed: ${join(root, ".artifacts", "local-feed")}
Relative output paths resolve from the repository root.
DOTNET_HOST_PATH can select the dotnet executable.`);
  process.exit(0);
}

assert(
  values.output === undefined || values.output.trim().length > 0,
  "Output directory must not be blank.",
);
const feed = resolve(root, values.output ?? ".artifacts/local-feed");
const packages = libraryPackages(root);
const prefix = /<VersionPrefix>(\d+\.\d+\.\d+)<\/VersionPrefix>/.exec(
  readFileSync(join(root, "src", "Directory.Build.props"), "utf8"),
)?.[1];
assert(
  prefix,
  "Expected a three-part VersionPrefix in src/Directory.Build.props.",
);
// The letter keeps the random component nonnumeric, including leading zeroes.
const version = `${prefix}-local.${Date.now()}.g${randomBytes(4).toString("hex")}`;
const dotnet = process.env.DOTNET_HOST_PATH ?? "dotnet";
const staging = mkdtempSync(join(tmpdir(), "gloaming-pack-"));

try {
  for (const item of packages) {
    console.log(`Packing ${item.id}`);
    const result = spawnSync(
      dotnet,
      [
        "pack",
        item.project,
        "--configuration",
        "Release",
        "--output",
        staging,
        `-p:Version=${version}`,
        `-p:PackageVersion=${version}`,
        "--nologo",
      ],
      { cwd: root, stdio: "inherit", timeout: 600_000 },
    );
    assert(!result.error, `Packing ${item.id}: ${result.error?.message}`);
    assert.equal(result.status, 0, `Packing ${item.id} failed.`);
  }

  const archives = packages.map((item) => `${item.id}.${version}.nupkg`);
  assert.deepEqual(
    readdirSync(staging)
      .filter((name) => name.endsWith(".nupkg"))
      .sort(),
    [...archives].sort(),
    "The packed archives must match the library inventory.",
  );

  // Build every package before adding this version to the persistent feed.
  mkdirSync(feed, { recursive: true });
  const copied: string[] = [];
  const snapshot = join(feed, `gloaming.${version}.json`);
  try {
    for (const archive of archives) {
      const destination = join(feed, archive);
      copyFileSync(
        join(staging, archive),
        destination,
        constants.COPYFILE_EXCL,
      );
      copied.push(destination);
    }
    writeFileSync(
      snapshot,
      JSON.stringify(
        { version, feed, packages: packages.map((item) => item.id) },
        null,
        2,
      ) + "\n",
      { flag: "wx" },
    );
  } catch (failure) {
    for (const file of copied) rmSync(file, { force: true });
    throw failure;
  }

  console.log(`Packed ${packages.length} Gloaming packages.
Feed: ${feed}
Version: ${version}
Snapshot: ${snapshot}`);
} finally {
  rmSync(staging, { recursive: true, force: true });
}
