import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import {
  chmodSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  readlinkSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import {
  delimiter,
  dirname,
  isAbsolute,
  join,
  relative,
  resolve,
  sep,
} from "node:path";
import { XMLParser, XMLValidator } from "fast-xml-parser";
import {
  exists,
  libraries,
  librarySources,
  root,
  sha256,
  type Configuration,
} from "./create.ts";

const forbidden =
  /Wholesale|archive[/\\]proof-sample|FoundryApplication|ConsumerRoot|EventSourcing|(?:Gloaming|ModulithFoundry)\.Events|Rebus|RabbitMQ|ServiceBus|Marten|Outbox|Inbox|AddHostedService|BackgroundService/i;
const creator = join(import.meta.dirname, "create.ts");

function run(
  command: string,
  args: string[],
  cwd: string,
  succeeds = true,
  env: NodeJS.ProcessEnv = process.env,
): string {
  const result = spawnSync(command, args, {
    cwd,
    env,
    encoding: "utf8",
    maxBuffer: 50 * 1024 * 1024,
  });
  if (result.error) throw result.error;
  const log = result.stdout + result.stderr;
  assert.equal(
    result.status === 0,
    succeeds,
    `Unexpected exit ${result.status}: ${command} ${args.join(" ")}\n${log}`,
  );
  process.stdout.write(log);
  return log;
}

function invoke(
  config: string,
  output: string,
  succeeds = true,
  env: NodeJS.ProcessEnv = process.env,
): string {
  return run(
    process.execPath,
    [creator, "--config", config, "--output", output],
    dirname(config),
    succeeds,
    env,
  );
}

function files(directory: string): Map<string, Buffer> {
  const result = new Map<string, Buffer>();
  function walk(current: string): void {
    for (const entry of readdirSync(current, { withFileTypes: true }).sort(
      (a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0),
    )) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) walk(path);
      else {
        assert(
          entry.isFile(),
          `Unexpected non-regular generated file: ${path}`,
        );
        result.set(relative(directory, path), readFileSync(path));
      }
    }
  }
  walk(directory);
  return result;
}

function inside(parent: string, child: string): boolean {
  const path = relative(parent, child);
  return path !== ".." && !path.startsWith(`..${sep}`) && !isAbsolute(path);
}

function object(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

// Inspect native XML through a parser, not an implementation-specific text pattern.
function elements(value: unknown, name: string): Record<string, unknown>[] {
  if (Array.isArray(value))
    return value.flatMap((child) => elements(child, name));
  if (!object(value)) return [];
  return Object.entries(value).flatMap(([key, child]) => {
    if (key !== name) return elements(child, name);
    return (Array.isArray(child) ? child : [child]).filter(object);
  });
}

function xml(bytes: Buffer): unknown {
  const text = bytes.toString("utf8");
  assert.equal(XMLValidator.validate(text), true);
  return new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: "",
  }).parse(text);
}

function text(content: Map<string, Buffer>, path: string): string {
  const bytes = content.get(path);
  assert(bytes, `Missing generated file: ${path}`);
  return bytes.toString("utf8");
}

function inspect(output: string, config: Configuration): void {
  const content = files(output);
  assert(content.has(`${config.applicationName}.slnx`));
  for (const [path, bytes] of content) {
    const source = bytes.toString("utf8");
    assert(!forbidden.test(`${path}\n${source}`), path);
    assert(!source.includes(root), path);
    assert(!isAbsolute(path));
    if (path.endsWith(".csproj")) {
      for (const reference of elements(xml(bytes), "ProjectReference")) {
        assert.equal(typeof reference.Include, "string");
        assert(typeof reference.Include === "string");
        const target = realpathSync(
          resolve(output, dirname(path), reference.Include),
        );
        assert(inside(output, target), target);
      }
    }
    if (!path.startsWith("libraries/")) {
      if (
        path.endsWith(".csproj") ||
        (path.endsWith(".cs") && source.includes("namespace "))
      ) {
        assert(source.includes(config.rootNamespace), path);
      }
    }
  }
  const projects = elements(
    xml(Buffer.from(text(content, `${config.applicationName}.slnx`))),
    "Project",
  );
  assert.equal(projects.length, 7);
  for (const project of projects) {
    assert(typeof project.Path === "string");
    assert(content.has(project.Path), project.Path);
  }
  const manifest: unknown = JSON.parse(text(content, "library-sources.json"));
  assert(object(manifest));
  assert.deepEqual(
    Object.keys(manifest).sort(),
    [...content.keys()].filter((path) => path.startsWith("libraries/")).sort(),
  );
  assert.deepEqual(
    new Set(Object.keys(manifest).map((path) => path.split("/")[1])),
    new Set(libraries),
  );
  for (const [path, digest] of Object.entries(manifest)) {
    const bytes = content.get(path);
    assert(bytes);
    assert.equal(sha256(bytes), digest);
    const name = libraries.find((name) =>
      path.startsWith(`libraries/${name}/`),
    );
    assert(name);
    assert.deepEqual(
      bytes,
      readFileSync(
        join(librarySources[name], path.slice(`libraries/${name}/`.length)),
      ),
    );
  }
  assert.deepEqual(JSON.parse(text(content, "foundry.json")), config);
}

function entries(directory: string): string[] {
  return readdirSync(directory).sort();
}

function createAsync(config: string, output: string): Promise<number | null> {
  return new Promise((resolve, reject) => {
    const child = spawn(
      process.execPath,
      [creator, "--config", config, "--output", output],
      { cwd: dirname(config), stdio: "ignore" },
    );
    child.on("error", reject);
    child.on("close", (code) => resolve(code));
  });
}

async function creationFailures(
  scratch: string,
  good: string,
  created: string,
): Promise<void> {
  const rejected = [
    "{",
    "[]",
    "{}",
    '{"applicationName":"OnlyName"}',
    '{"applicationName":"A","rootNamespace":"B","eventSourcing":true}',
    '{"applicationName":"A","applicationName":"B","rootNamespace":"C"}',
    '{"applicationName":true,"rootNamespace":"B"}',
    '{"applicationName":"../escape","rootNamespace":"B"}',
    '{"applicationName":"class","rootNamespace":"B"}',
    '{"applicationName":"CON","rootNamespace":"B"}',
    '{"applicationName":"A","rootNamespace":"B.class"}',
    '{"applicationName":"A","rootNamespace":"B..C"}',
    '{"applicationName":"A","rootNamespace":null}',
    '{"applicationName":"ConsumerRoot","rootNamespace":"B"}',
    '{"applicationName":"A","\\u0061pplicationName":"B","rootNamespace":"C"}',
    '{"applicationName":"A","rootNamespace":"B",}',
    '{"applicationName":"A",/* comment */"rootNamespace":"B"}',
    ...["__arglist", "__makeref", "__reftype", "__refvalue"].flatMap(
      (token) => [
        JSON.stringify({ applicationName: token, rootNamespace: "Acme" }),
        JSON.stringify({
          applicationName: "A",
          rootNamespace: `Acme.${token}`,
        }),
      ],
    ),
  ];
  const invalid = join(scratch, "invalid.json");
  for (const [index, value] of rejected.entries()) {
    writeFileSync(invalid, value);
    const destination = join(scratch, `invalid-${index}`);
    const before = entries(scratch);
    invoke(invalid, destination, false);
    assert(!exists(destination));
    assert.deepEqual(entries(scratch), before);
  }
  for (const kind of ["directory", "file", "nonempty", "symlink"]) {
    const destination = join(scratch, `occupied-${kind}`);
    if (kind === "file") writeFileSync(destination, "owner content");
    else if (kind === "symlink")
      symlinkSync(join(scratch, "missing-target"), destination);
    else {
      mkdirSync(destination);
      if (kind === "nonempty")
        writeFileSync(join(destination, "owner.txt"), "owner content");
    }
    function snapshot(): Buffer | string | Map<string, Buffer> {
      return kind === "file"
        ? readFileSync(destination)
        : kind === "symlink"
          ? readlinkSync(destination)
          : files(destination);
    }
    const before = snapshot();
    invoke(good, destination, false);
    assert.deepEqual(snapshot(), before);
  }
  const beforeRepeat = files(created);
  invoke(good, created, false);
  assert.deepEqual(files(created), beforeRepeat);
  const absent = join(scratch, "absent-parent");
  invoke(good, join(absent, "consumer"), false);
  assert(!exists(absent));

  const fake = join(scratch, "fake-tools");
  mkdirSync(fake);
  const fakeDotnet = join(fake, "dotnet");
  const fakeEnv = {
    ...process.env,
    PATH: `${fake}${delimiter}${process.env.PATH}`,
  };
  writeFileSync(fakeDotnet, "#!/bin/sh\nexit 9\n");
  chmodSync(fakeDotnet, 0o755);
  const beforeFailure = entries(scratch);
  invoke(good, join(scratch, "engine-failed"), false, fakeEnv);
  assert.deepEqual(entries(scratch), beforeFailure);
  // Native creation can fail after writing part of its staging output, too.
  writeFileSync(
    fakeDotnet,
    `#!${process.execPath}\nconst fs = require('node:fs');\nconst args = process.argv.slice(2);\nif (args[1] === 'install') process.exit(0);\nconst output = args[args.indexOf('--output') + 1];\nfs.mkdirSync(output);\nfs.writeFileSync(output + '/partial.txt', 'partial staging');\nprocess.exit(9);\n`,
  );
  invoke(good, join(scratch, "engine-partial"), false, fakeEnv);
  assert.deepEqual(entries(scratch), beforeFailure);

  const raced = join(scratch, "raced");
  const outcomes = await Promise.all([
    createAsync(good, raced),
    createAsync(good, raced),
  ]);
  assert.deepEqual(outcomes.sort(), [0, 2]);
  assert.deepEqual(files(raced), files(created));

  // Force an external empty destination to appear after preflight but before publish.
  // This exercises the no-replace syscall; a plain Node rename would overwrite it.
  const realDotnet = process.env.PATH?.split(delimiter)
    .map((directory) => join(directory, "dotnet"))
    .find(exists);
  assert(realDotnet);
  const occupiedDuringCreation = join(scratch, "occupied-during-creation");
  writeFileSync(
    fakeDotnet,
    '#!/bin/sh\nif [ "$2" = "foundry-state-stored" ]; then mkdir "$T1_RACE_OUTPUT"; fi\nexec "$T1_REAL_DOTNET" "$@"\n',
  );
  invoke(good, occupiedDuringCreation, false, {
    ...fakeEnv,
    T1_REAL_DOTNET: realDotnet,
    T1_RACE_OUTPUT: occupiedDuringCreation,
  });
  assert(exists(occupiedDuringCreation));
  assert.deepEqual(files(occupiedDuringCreation), new Map());
  assert(!entries(scratch).some((path) => path.startsWith(".foundry-create-")));
  console.log(
    "Creation rejection, repeat, early/partial native failure and publication race proofs passed.",
  );
}

function inspectRestoredGraph(output: string): void {
  for (const [path, bytes] of files(output)) {
    if (!path.endsWith("project.assets.json")) continue;
    const data: unknown = JSON.parse(bytes.toString("utf8"));
    assert(object(data) && object(data.libraries));
    for (const [name, dependency] of Object.entries(data.libraries)) {
      assert(!forbidden.test(name), name);
      assert(object(dependency));
      if (dependency.type === "project") {
        assert(typeof dependency.path === "string");
        const target = realpathSync(
          resolve(output, dirname(path), "..", dependency.path),
        );
        assert(inside(output, target), target);
      }
    }
    assert(!bytes.toString("utf8").includes(root), path);
  }
}

async function main(): Promise<void> {
  assert(
    process.env.CATALOG_TEST_ADMIN_CONNECTION_STRING,
    "Set CATALOG_TEST_ADMIN_CONNECTION_STRING for this complete proof.",
  );
  const scratch = realpathSync(mkdtempSync("/tmp/foundry-t1-ts-"));
  assert(!inside(root, scratch));
  try {
    // Every output lives under a conflicting SDK pin. Without a staging pin,
    // native creation either selects SDK 9 or fails when only SDK 10 is installed.
    const parentPin = JSON.stringify({
      sdk: { version: "9.0.100", rollForward: "disable" },
    });
    writeFileSync(join(scratch, "global.json"), parentPin);
    const parentSdk = spawnSync("dotnet", ["--version"], {
      cwd: scratch,
      encoding: "utf8",
    });
    if (parentSdk.error) throw parentSdk.error;
    assert(parentSdk.status !== 0 || parentSdk.stdout.trim() === "9.0.100");
    const consumers: { config: string; output: string; name: string }[] = [];
    for (const config of [
      { applicationName: "Cedar", rootNamespace: "Acme.Cedar" },
      { applicationName: "HarborDesk", rootNamespace: "Task" },
    ]) {
      const path = join(scratch, `${config.applicationName}.json`);
      writeFileSync(path, JSON.stringify(config));
      const output = join(scratch, config.applicationName);
      invoke(path, output);
      inspect(output, config);
      assert.equal(
        readFileSync(join(scratch, "global.json"), "utf8"),
        parentPin,
      );
      consumers.push({ config: path, output, name: config.applicationName });
    }
    const first = consumers[0];
    assert(first);
    const identical = join(scratch, "identical");
    invoke(first.config, identical);
    assert.deepEqual(files(identical), files(first.output));
    await creationFailures(scratch, first.config, first.output);
    for (const { output, name } of consumers) {
      run("dotnet", ["restore", `${name}.slnx`], output);
      run(
        "dotnet",
        [
          "build",
          `${name}.slnx`,
          "--no-restore",
          "-m:1",
          "--disable-build-servers",
        ],
        output,
      );
      inspectRestoredGraph(output);
      run(
        "dotnet",
        [
          "format",
          "style",
          `${name}.slnx`,
          "--verify-no-changes",
          "--no-restore",
        ],
        output,
      );
      run(
        "dotnet",
        [
          "format",
          "analyzers",
          `${name}.slnx`,
          "--verify-no-changes",
          "--no-restore",
        ],
        output,
      );
      run(
        "dotnet",
        [
          "test",
          "--project",
          `tests/${name}.Adoption.Tests/${name}.Adoption.Tests.csproj`,
          "--no-build",
          "--no-restore",
          "--no-progress",
          "--no-ansi",
          "--output",
          "Normal",
          "--show-stdout",
          "Failed",
          "--show-stderr",
          "Failed",
        ],
        output,
      );
    }
    console.log(
      "T1 TypeScript proof passed: two external consumers (including namespace Task), conflicting parent SDK pin, deterministic creation, omission, local references and fresh PostgreSQL journeys.",
    );
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

await main();
