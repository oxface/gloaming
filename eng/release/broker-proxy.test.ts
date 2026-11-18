import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import test from "node:test";

test("Kafka proxy drains reset and cancelled sessions and preserves unexpected faults", () => {
  const directory = mkdtempSync(join(tmpdir(), "gloaming-proxy-test-"));
  try {
    copyFileSync(
      resolve(import.meta.dirname, "../../global.json"),
      join(directory, "global.json"),
    );
    copyFileSync(
      join(import.meta.dirname, "consumers/Kafka/BrokerProxy.cs"),
      join(directory, "BrokerProxy.cs"),
    );
    copyFileSync(
      join(import.meta.dirname, "tests/BrokerProxyLifecycle.cs"),
      join(directory, "Program.cs"),
    );
    writeFileSync(
      join(directory, "Proxy.csproj"),
      `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
`,
    );
    const result = spawnSync(
      process.env.DOTNET_HOST_PATH ?? "dotnet",
      ["run", "--project", "Proxy.csproj", "--configuration", "Release"],
      {
        cwd: directory,
        encoding: "utf8",
        timeout: 90_000,
        maxBuffer: 1024 * 1024,
      },
    );
    assert(!result.error, result.error?.message);
    assert.equal(
      result.status,
      0,
      `${result.stdout}${result.stderr}\nSignal: ${result.signal}`,
    );
    assert.match(result.stdout, /PASS reset during relay startup: 32/);
    assert.match(result.stdout, /PASS stop during connection setup: 32/);
    assert.match(
      result.stdout,
      /PASS stop during concurrent connection attempts: 128/,
    );
    assert.match(
      result.stdout,
      /PASS unexpected relay fault remains observable/,
    );
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
