import assert from "node:assert/strict";
import { execFile, execFileSync, spawn, spawnSync } from "node:child_process";
import { createHash, randomBytes } from "node:crypto";
import {
  appendFileSync,
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, relative, resolve } from "node:path";
import { promisify } from "node:util";
import { removeOwnedContainers } from "./container-cleanup.ts";

// A separate native transport profile: ordinary verify remains PostgreSQL-only.
const root = resolve(import.meta.dirname, "../..");
assert.equal(
  process.argv.length,
  2,
  "Usage: npm --prefix tools/release run verify:kafka",
);
const requestedRuntime = process.env.GLOAMING_CONTAINER_RUNTIME;
assert(
  requestedRuntime === undefined ||
    ["docker", "podman"].includes(requestedRuntime),
  "GLOAMING_CONTAINER_RUNTIME must be docker or podman.",
);
const runtime =
  requestedRuntime ??
  ["docker", "podman"].find(
    (command) =>
      spawnSync(command, ["--version"], { encoding: "utf8" }).status === 0,
  );
assert(
  runtime,
  "Kafka proof requires a local Docker or Podman CLI; no broker tests are skipped.",
);
const dotnet = process.env.DOTNET_HOST_PATH ?? "dotnet";
const scratch = mkdtempSync(join(tmpdir(), "gloaming-kafka-package-proof-"));
const directory = join(scratch, "consumer");
const feed = join(scratch, "feed");
const cache = join(scratch, "packages");
const source = join(import.meta.dirname, "consumers", "Kafka");
const id = randomBytes(6).toString("hex");
const postgres = `gloaming-kafka-proof-pg-${id}`;
const kafka = `gloaming-kafka-proof-broker-${id}`;
const password = randomBytes(24).toString("hex");
const images = {
  kafka:
    "docker.io/apache/kafka:4.3.1@sha256:ccd1314e47ec76909e01f86308b4dcf2064f19f7c89759234322314b0e319e26",
  postgres:
    "docker.io/library/postgres:18.6@sha256:7341002d2b8c7c5bdd7542a671a95b36196c0b5b888daf454ae4fc33ba5346d7",
};
const log = join(scratch, "execution.log");
const packageEnv = { ...process.env, NUGET_PACKAGES: cache };
const ownedContainers: string[] = [];
const failures: unknown[] = [];
let manifest: Record<string, unknown> | undefined;
const safe = (text: string): string => text.replaceAll(password, "[redacted]");
const hash = (file: string): string =>
  createHash("sha256").update(readFileSync(file)).digest("hex");
const xml = (text: string): string =>
  text
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;");
const execute = promisify(execFile);
writeFileSync(log, "Kafka package-only native proof\n");
function run(
  command: string,
  args: string[],
  cwd = root,
  env = process.env,
  timeout = 600_000,
): string {
  const result = spawnSync(command, args, {
    cwd,
    env,
    encoding: "utf8",
    timeout,
    maxBuffer: 50 * 1024 * 1024,
  });
  const output = safe((result.stdout ?? "") + (result.stderr ?? ""));
  appendFileSync(log, output);
  assert(!result.error, `${command}: ${result.error?.message}; log: ${log}`);
  assert.equal(
    result.status,
    0,
    `${command} failed (${result.status}); log: ${log}\n${output}`,
  );
  return result.stdout;
}
const readIndex = () =>
  execFileSync("git", ["ls-files", "--stage", "-z"], {
    cwd: root,
    encoding: "utf8",
  });
const readStaged = () =>
  execFileSync("git", ["diff", "--cached", "--binary"], {
    cwd: root,
    encoding: "utf8",
  });
const indexBefore = readIndex();
const stagedBefore = readStaged();
// Do not put index bytes in the execution log.
writeFileSync(log, "Kafka package-only native proof\n");
const revision = run("git", ["rev-parse", "HEAD"]).trim();
console.log(`Native Kafka package proof; evidence: ${scratch}`);

async function freePort(): Promise<number> {
  return await new Promise((resolvePort, reject) => {
    const reservation = createServer();
    reservation.on("error", reject);
    reservation.listen(0, "127.0.0.1", () => {
      const address = reservation.address();
      if (!address || typeof address === "string")
        return reject(new Error("No ephemeral port."));
      reservation.close((error) =>
        error ? reject(error) : resolvePort(address.port),
      );
    });
  });
}
async function waitForBroker(): Promise<void> {
  for (let attempt = 0; attempt < 8; attempt++) {
    try {
      await execute(
        runtime!,
        [
          "exec",
          kafka,
          "/opt/kafka/bin/kafka-topics.sh",
          "--bootstrap-server",
          "localhost:19092",
          "--list",
        ],
        { timeout: 10_000, maxBuffer: 1024 * 1024 },
      );
      return;
    } catch {
      await new Promise((resolveDelay) => setTimeout(resolveDelay, 500));
    }
  }
  throw new Error("Owned Kafka container did not become ready.");
}
try {
  console.log("Packing current Gloaming libraries.");
  run(process.execPath, [
    join(import.meta.dirname, "pack.ts"),
    "--output",
    feed,
  ]);
  const snapshots = readdirSync(feed).filter(
    (name) => name.startsWith("gloaming.") && name.endsWith(".json"),
  );
  assert.equal(snapshots.length, 1, "Expected one newly completed snapshot.");
  const snapshot = JSON.parse(
    readFileSync(join(feed, snapshots[0]!), "utf8"),
  ) as { version: string; packages: string[] };
  const version = snapshot.version;
  const archives = snapshot.packages.map((name) => ({
    id: name,
    sha256: hash(join(feed, `${name}.${version}.nupkg`)),
  }));
  mkdirSync(directory);
  cpSync(source, directory, { recursive: true });
  const consumerSources = readdirSync(directory, { recursive: true })
    .filter(
      (path): path is string =>
        typeof path === "string" && /\.(cs|json)$/.test(path),
    )
    .map((path) => ({
      path: relative(root, join(source, path)),
      sha256: hash(join(directory, path)),
    }));
  cpSync(join(root, "global.json"), join(scratch, "global.json"));
  const nativeVersions = readFileSync(
    join(root, "Directory.Packages.props"),
    "utf8",
  );
  const dependencyInjection =
    /Include="Microsoft.Extensions.DependencyInjection"\s+Version="([^"]+)"/.exec(
      nativeVersions,
    )?.[1];
  assert(dependencyInjection, "Expected central native DI version.");
  writeFileSync(
    join(directory, "Consumer.csproj"),
    `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><AnalysisLevel>latest-recommended</AnalysisLevel><EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild></PropertyGroup><ItemGroup><PackageReference Include="Gloaming.Messaging.EntityFrameworkCore.Postgres" Version="[${xml(version)}]"/><PackageReference Include="Confluent.Kafka" Version="[2.16.0]"/><PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="[${xml(dependencyInjection)}]"/></ItemGroup></Project>`,
  );
  const config = join(scratch, "NuGet.Config");
  writeFileSync(
    config,
    `<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="local" value="${xml(feed)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="local"><package pattern="Gloaming.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`,
  );
  run(
    dotnet,
    [
      "restore",
      "Consumer.csproj",
      "--configfile",
      config,
      "--no-http-cache",
      "--nologo",
    ],
    directory,
    packageEnv,
  );
  const assets = JSON.parse(
    readFileSync(join(directory, "obj", "project.assets.json"), "utf8"),
  ) as {
    libraries: Record<string, { type: string; path: string }>;
    packageFolders: Record<string, unknown>;
    project: {
      restore: {
        frameworks: Record<
          string,
          { projectReferences: Record<string, unknown> }
        >;
      };
    };
  };
  for (const library of Object.values(assets.libraries))
    assert.equal(library.type, "package");
  for (const framework of Object.values(assets.project.restore.frameworks))
    assert.deepEqual(framework.projectReferences, {});
  const selected = Object.keys(assets.libraries).filter((name) =>
    name.startsWith("Gloaming."),
  );
  assert.deepEqual(selected.map((name) => name.split("/")[0]).sort(), [
    "Gloaming.EntityFrameworkCore",
    "Gloaming.Messaging",
    "Gloaming.Messaging.EntityFrameworkCore",
    "Gloaming.Messaging.EntityFrameworkCore.Postgres",
  ]);
  assert.deepEqual(
    Object.keys(assets.packageFolders).map((path) => resolve(path)),
    [cache],
  );
  for (const name of selected) {
    assert.equal(name.split("/")[1], version);
    const metadata = JSON.parse(
      readFileSync(
        join(cache, assets.libraries[name]!.path, ".nupkg.metadata"),
        "utf8",
      ),
    ) as { source: string };
    assert.equal(metadata.source, feed);
  }
  run(
    dotnet,
    [
      "build",
      "Consumer.csproj",
      "--configuration",
      "Release",
      "--no-restore",
      "--nologo",
    ],
    directory,
    packageEnv,
  );
  console.log(
    "Package-only consumer built with a fresh cache and the four intended Gloaming packages.",
  );

  const brokerPort = await freePort();
  let proxyPort = await freePort();
  while (proxyPort === brokerPort) proxyPort = await freePort();
  ownedContainers.push(postgres);
  run(
    runtime,
    [
      "run",
      "--detach",
      "--name",
      postgres,
      "--publish",
      "127.0.0.1::5432",
      "--env",
      "POSTGRES_PASSWORD",
      "--env",
      "POSTGRES_DB=kafka_proof",
      images.postgres,
    ],
    root,
    { ...process.env, POSTGRES_PASSWORD: password },
    120_000,
  );
  const postgresPort = run(runtime, ["port", postgres, "5432/tcp"])
    .trim()
    .split(":")
    .at(-1);
  assert(
    postgresPort && /^\d+$/.test(postgresPort),
    "Expected an isolated native PostgreSQL port.",
  );
  ownedContainers.push(kafka);
  run(
    runtime,
    [
      "run",
      "--detach",
      "--name",
      kafka,
      "--publish",
      `127.0.0.1:${brokerPort}:9092`,
      "--env",
      "KAFKA_NODE_ID=1",
      "--env",
      "KAFKA_PROCESS_ROLES=broker,controller",
      "--env",
      "KAFKA_LISTENERS=INTERNAL://:19092,CONTROLLER://:19093,EXTERNAL://:9092",
      "--env",
      `KAFKA_ADVERTISED_LISTENERS=INTERNAL://localhost:19092,EXTERNAL://127.0.0.1:${proxyPort}`,
      "--env",
      "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=INTERNAL:PLAINTEXT,EXTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT",
      "--env",
      "KAFKA_INTER_BROKER_LISTENER_NAME=INTERNAL",
      "--env",
      "KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER",
      "--env",
      "KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:19093",
      "--env",
      "KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1",
      "--env",
      "KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1",
      "--env",
      "KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1",
      "--env",
      "KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0",
      images.kafka,
    ],
    root,
    process.env,
    120_000,
  );
  let pgReady = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    if (
      spawnSync(runtime, ["exec", postgres, "pg_isready", "-U", "postgres"], {
        encoding: "utf8",
        timeout: 5_000,
      }).status === 0
    ) {
      pgReady = true;
      break;
    }
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 500));
  }
  assert(pgReady, "Owned PostgreSQL container did not become ready.");
  await waitForBroker();
  console.log(
    "Native Kafka and PostgreSQL ready; executing provider/process scenarios.",
  );
  let restarted = false;
  await new Promise<void>((resolveChild, reject) => {
    const child = spawn(
      dotnet,
      [join(directory, "bin", "Release", "net10.0", "Consumer.dll")],
      {
        cwd: directory,
        env: {
          ...packageEnv,
          PROBE_POSTGRES: `Host=127.0.0.1;Port=${postgresPort};Database=kafka_proof;Username=postgres;Password=${password};Pooling=false`,
          PROBE_KAFKA: `127.0.0.1:${proxyPort}`,
          PROBE_KAFKA_UPSTREAM: `127.0.0.1:${brokerPort}`,
          PROBE_RESULT: join(scratch, "results.json"),
          GLOAMING_PROOF_REVISION: revision,
          GLOAMING_PROOF_SOURCE: `fixture-${id}`,
          GLOAMING_PROOF_VERSION: version,
        },
        stdio: ["pipe", "pipe", "pipe"],
        timeout: 480_000,
      },
    );
    let pending = "";
    let control: Promise<void> = Promise.resolve();
    child.stdout.on("data", (data: Buffer) => {
      const text = safe(data.toString());
      appendFileSync(log, text);
      process.stdout.write(text);
      pending += text;
      const lines = pending.split("\n");
      pending = lines.pop()!;
      for (const line of lines)
        if (line.trim() === "CONTROL:restart-broker") {
          control = (async () => {
            assert(!restarted, "Only one broker restart is selected.");
            await execute(runtime!, ["restart", kafka], { timeout: 60_000 });
            await waitForBroker();
            restarted = true;
            child.stdin.write("restarted\n");
          })();
          void control.catch((error) => {
            child.kill("SIGKILL");
            reject(error);
          });
        }
    });
    child.stderr.on("data", (data: Buffer) => {
      const text = safe(data.toString());
      appendFileSync(log, text);
      process.stderr.write(text);
    });
    child.on("error", reject);
    child.on("close", (code, signal) => {
      void control
        .then(() =>
          code === 0
            ? resolveChild()
            : reject(
                new Error(
                  `Native proof failed (${code}, ${signal}); log: ${log}`,
                ),
              ),
        )
        .catch(reject);
    });
  });
  assert(restarted, "Native broker restart must actually execute.");
  const result = JSON.parse(
    readFileSync(join(scratch, "results.json"), "utf8"),
  ) as { cases: { name: string; result: string }[] };
  assert.equal(
    result.cases.length,
    13,
    "All selected provider/process scenarios must execute.",
  );
  assert.equal(new Set(result.cases.map((item) => item.name)).size, 13);
  assert(result.cases.every((item) => item.result === "pass"));
  manifest = {
    version,
    revision,
    images,
    packages: archives,
    gloamingPackages: selected,
    projectReferences: 0,
    packageCache: cache,
    brokerRestarted: restarted,
    consumerSources,
    cases: result.cases,
    verifiedAt: new Date().toISOString(),
  };
} catch (failure) {
  failures.push(failure);
  try {
    for (const name of ownedContainers) {
      const result = spawnSync(runtime, ["logs", "--tail", "40", name], {
        encoding: "utf8",
        timeout: 10_000,
      });
      appendFileSync(log, safe((result.stdout ?? "") + (result.stderr ?? "")));
    }
  } catch (diagnosticFailure) {
    failures.push(diagnosticFailure);
  }
} finally {
  try {
    const cleanup = removeOwnedContainers(
      runtime,
      ownedContainers.toReversed(),
    );
    writeFileSync(
      join(scratch, "cleanup.json"),
      JSON.stringify(cleanup, null, 2) + "\n",
    );
    console.log(`Task-owned cleanup: ${JSON.stringify(cleanup)}`);
    if (cleanup.some((result) => result.outcome === "failed"))
      failures.push(
        new Error(`Owned container cleanup failed; evidence: ${scratch}`),
      );
  } catch (failure) {
    failures.push(failure);
  }
  try {
    assert.equal(
      readIndex(),
      indexBefore,
      "The review index changed during verification.",
    );
    assert.equal(
      readStaged(),
      stagedBefore,
      "The staged review snapshot changed.",
    );
  } catch (failure) {
    failures.push(failure);
  }
}
if (failures.length === 1) throw failures[0];
if (failures.length > 1)
  throw new AggregateError(
    failures,
    `Kafka proof or cleanup failed; evidence: ${scratch}`,
  );
assert(manifest, "A successful proof must produce a manifest.");
writeFileSync(
  join(scratch, "manifest.json"),
  JSON.stringify(manifest, null, 2) + "\n",
);
console.log(
  `PASS Kafka package adoption; manifest: ${join(scratch, "manifest.json")}`,
);
