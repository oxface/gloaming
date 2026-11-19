import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { createHash, randomBytes } from "node:crypto";
import {
  appendFileSync,
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join, relative, resolve } from "node:path";
import { nativePackage, preparePackageProof } from "./package-proof.ts";

// A separate native transport profile: ordinary verify remains PostgreSQL-only.
const root = resolve(import.meta.dirname, "../..");
assert.equal(
  process.argv.length,
  2,
  "Usage: npm --prefix eng/release run verify:kafka",
);
const requestedRuntime = process.env.GLOAMING_CONTAINER_RUNTIME;
assert(
  requestedRuntime === undefined ||
    ["docker", "podman"].includes(requestedRuntime),
  "GLOAMING_CONTAINER_RUNTIME must be docker or podman.",
);
if (requestedRuntime === "podman" && !process.env.DOCKER_HOST) {
  const socket = join(
    process.env.XDG_RUNTIME_DIR ?? `/run/user/${process.getuid?.()}`,
    "podman",
    "podman.sock",
  );
  assert(existsSync(socket), "Start the Podman API socket or set DOCKER_HOST.");
  process.env.DOCKER_HOST = `unix://${socket}`;
}
const dotnet = process.env.DOTNET_HOST_PATH ?? "dotnet";
const scratch = mkdtempSync(join(tmpdir(), "gloaming-kafka-package-proof-"));
const directory = join(scratch, "consumer");
const source = join(import.meta.dirname, "consumers", "Kafka");
const id = randomBytes(6).toString("hex");
const password = randomBytes(24).toString("hex");
const images = {
  kafka:
    "docker.io/apache/kafka:4.3.1@sha256:ccd1314e47ec76909e01f86308b4dcf2064f19f7c89759234322314b0e319e26",
  postgres:
    "docker.io/library/postgres:18.6@sha256:7341002d2b8c7c5bdd7542a671a95b36196c0b5b888daf454ae4fc33ba5346d7",
};
const log = join(scratch, "execution.log");
const safe = (text: string): string => text.replaceAll(password, "[redacted]");
const hash = (file: string): string =>
  createHash("sha256").update(readFileSync(file)).digest("hex");
const xml = (text: string): string =>
  text
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;");
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
    `${command} failed (${result.signal ?? result.status}); log: ${log}\n${output}`,
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

let manifest: Record<string, unknown>;
try {
  console.log("Packing current Gloaming libraries.");
  const proof = preparePackageProof(root, scratch, run);
  const { version, feed, cache, env: packageEnv } = proof;
  const archives = proof.packages.map(({ id }) => ({
    id,
    sha256: hash(join(feed, `${id}.${version}.nupkg`)),
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
  const { version: dependencyInjection } = nativePackage(
    root,
    "Microsoft.Extensions.DependencyInjection",
  );
  const { version: testcontainers } = nativePackage(
    root,
    "Testcontainers.PostgreSql",
  );
  writeFileSync(
    join(directory, "Consumer.csproj"),
    `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Gloaming.Messaging.EntityFrameworkCore.Postgres" Version="[${xml(version)}]"/>
    <PackageReference Include="Confluent.Kafka" Version="[2.16.0]"/>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="[${xml(dependencyInjection)}]"/>
    <PackageReference Include="Testcontainers.PostgreSql" Version="[${xml(testcontainers)}]"/>
  </ItemGroup>
</Project>
`,
  );
  const { gloaming: selected } = proof.restore(directory, [
    "Gloaming.EntityFrameworkCore",
    "Gloaming.Messaging",
    "Gloaming.Messaging.EntityFrameworkCore",
    "Gloaming.Messaging.EntityFrameworkCore.Postgres",
  ]);
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

  const cleanupFile = join(scratch, "cleanup.json");
  process.stdout.write(
    safe(
      run(
        dotnet,
        [join(directory, "bin", "Release", "net10.0", "Consumer.dll")],
        directory,
        {
          ...packageEnv,
          PROBE_POSTGRES_IMAGE: images.postgres,
          PROBE_KAFKA_IMAGE: images.kafka,
          PROBE_FIXTURE_PASSWORD: password,
          PROBE_RESULT: join(scratch, "results.json"),
          PROBE_CLEANUP: cleanupFile,
          GLOAMING_PROOF_REVISION: revision,
          GLOAMING_PROOF_SOURCE: `fixture-${id}`,
          GLOAMING_PROOF_VERSION: version,
        },
        480_000,
      ),
    ),
  );
  const cleanup = JSON.parse(readFileSync(cleanupFile, "utf8")) as {
    name: string;
    id: string;
    outcome: string;
  }[];
  assert.deepEqual(
    cleanup.map(({ name, outcome }) => ({ name, outcome })),
    [
      { name: "broker", outcome: "removed" },
      { name: "pg", outcome: "removed" },
    ],
  );
  assert(cleanup.every(({ id }) => id));
  console.log(`Task-owned cleanup: ${JSON.stringify(cleanup)}`);
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
    brokerRestarted: true,
    consumerSources,
    cases: result.cases,
    verifiedAt: new Date().toISOString(),
  };
} finally {
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
}
writeFileSync(
  join(scratch, "manifest.json"),
  JSON.stringify(manifest, null, 2) + "\n",
);
console.log(
  `PASS Kafka package adoption; manifest: ${join(scratch, "manifest.json")}`,
);
