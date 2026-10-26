import assert from "node:assert/strict";
import {
  cpSync,
  existsSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { isAbsolute, join, relative, resolve, sep } from "node:path";
import { XMLParser, XMLValidator } from "fast-xml-parser";
import { libraryPackages } from "./packages.ts";

type Run = (
  command: string,
  args: string[],
  cwd: string,
  env: NodeJS.ProcessEnv,
) => string;

interface Assets {
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
}

const xml = (value: string): string =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
const array = <T>(value: T | T[] | undefined): T[] =>
  value === undefined ? [] : Array.isArray(value) ? value : [value];

export function nativePackage(root: string, id: string) {
  const source = readFileSync(join(root, "Directory.Packages.props"), "utf8");
  assert.equal(
    XMLValidator.validate(source),
    true,
    "Invalid central package XML.",
  );
  const central = new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: "",
    parseTagValue: false,
  }).parse(source) as {
    Project: {
      ItemGroup?:
        | { PackageVersion?: { Include: string; Version: string }[] }
        | { PackageVersion?: { Include: string; Version: string }[] }[];
    };
  };
  const entries = array(central.Project.ItemGroup)
    .flatMap((group) => array(group.PackageVersion))
    .filter((entry) => entry.Include === id);
  assert.equal(entries.length, 1, `Expected one central version for ${id}.`);
  const version = entries[0]!.Version;
  assert(typeof version === "string" && version.length > 0);
  return { id, version };
}

// Shared only by the package proofs. Native profile assertions and cleanup stay with callers.
export function preparePackageProof(root: string, scratch: string, run: Run) {
  const feed = join(scratch, "feed");
  const cache = join(scratch, "packages");
  assert(!existsSync(feed), "Package proof requires a fresh feed.");
  assert(!existsSync(cache), "Package proof requires a fresh isolated cache.");
  const packages = libraryPackages(root);
  run(
    process.execPath,
    [join(root, "tools", "release", "pack.ts"), "--output", feed],
    root,
    process.env,
  );
  const snapshots = readdirSync(feed).filter((file) =>
    /^gloaming\..+\.json$/.test(file),
  );
  assert.equal(snapshots.length, 1, "Expected one completed local snapshot.");
  const snapshot = JSON.parse(
    readFileSync(join(feed, snapshots[0]!), "utf8"),
  ) as { version: string; feed: string; packages: string[] };
  const version = snapshot.version;
  assert.match(version, /^\d+\.\d+\.\d+-local\.\d+\.g[0-9a-f]{8}$/);
  assert.equal(snapshots[0], `gloaming.${version}.json`);
  assert.equal(snapshot.feed, feed);
  assert.deepEqual(
    snapshot.packages,
    packages.map((item) => item.id),
  );
  assert.deepEqual(
    readdirSync(feed)
      .filter((file) => file.endsWith(".nupkg"))
      .sort(),
    packages.map((item) => `${item.id}.${version}.nupkg`).sort(),
    "Archives must match the completed snapshot.",
  );

  cpSync(join(root, "global.json"), join(scratch, "global.json"));
  const config = join(scratch, "NuGet.Config");
  writeFileSync(
    config,
    `<configuration><packageSources><clear/><add key="local" value="${xml(feed)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="local"><package pattern="Gloaming.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`,
  );
  const env = { ...process.env, NUGET_PACKAGES: cache };

  return {
    version,
    feed,
    cache,
    packages,
    env,
    restore(directory: string, expectedClosure: readonly string[]) {
      run(
        process.env.DOTNET_HOST_PATH ?? "dotnet",
        [
          "restore",
          "Consumer.csproj",
          "--configfile",
          config,
          "--no-http-cache",
          "--nologo",
        ],
        directory,
        env,
      );
      const assets = JSON.parse(
        readFileSync(join(directory, "obj", "project.assets.json"), "utf8"),
      ) as Assets;
      assert.deepEqual(
        Object.keys(assets.packageFolders).map((path) => resolve(path)),
        [cache],
        "Restore must use only the isolated package cache.",
      );
      for (const [name, library] of Object.entries(assets.libraries))
        assert.equal(
          library.type,
          "package",
          `Project/source dependency: ${name}`,
        );
      for (const framework of Object.values(assets.project.restore.frameworks))
        assert.deepEqual(
          framework.projectReferences,
          {},
          "ProjectReference in package consumer.",
        );
      const gloaming = Object.keys(assets.libraries).filter((name) =>
        name.startsWith("Gloaming."),
      );
      assert.deepEqual(
        gloaming.map((name) => name.split("/")[0]!).sort(),
        [...expectedClosure].sort(),
        "Unexpected Gloaming dependency closure.",
      );
      for (const name of gloaming) {
        assert.equal(
          name.split("/")[1],
          version,
          `Wrong snapshot version: ${name}`,
        );
        const packagePath = resolve(cache, assets.libraries[name]!.path);
        const relativePath = relative(cache, packagePath);
        assert(
          relativePath !== ".." &&
            !relativePath.startsWith(`..${sep}`) &&
            !isAbsolute(relativePath),
          `Package path escapes isolated cache: ${name}`,
        );
        const metadata = JSON.parse(
          readFileSync(join(packagePath, ".nupkg.metadata"), "utf8"),
        ) as { source: string };
        assert.equal(metadata.source, feed, `Wrong package source: ${name}`);
      }
      return { packages: Object.keys(assets.libraries), gloaming };
    },
  };
}
