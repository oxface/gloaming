import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { XMLParser } from "fast-xml-parser";
import { nativePackage, preparePackageProof } from "./package-proof.ts";

const version = "1.0.0-local.1793000000000.g0123abcd";
const id = "Gloaming.Example";

function fixture() {
  const directory = mkdtempSync(
    join(tmpdir(), "gloaming-package-isolation-test-"),
  );
  const root = join(directory, "root");
  const scratch = join(directory, "proof & snapshot");
  const feed = join(scratch, "feed");
  const cache = join(scratch, "packages");
  const consumer = join(scratch, "consumer");
  const project = join(root, "src", id, id);
  mkdirSync(project, { recursive: true });
  mkdirSync(join(consumer, "obj"), { recursive: true });
  writeFileSync(
    join(project, `${id}.csproj`),
    '<Project Sdk="Microsoft.NET.Sdk"/>',
  );
  const sdk = '{"sdk":{"version":"10.0.402","rollForward":"patch"}}\n';
  writeFileSync(join(root, "global.json"), sdk);
  writeFileSync(
    join(root, "Directory.Packages.props"),
    '<Project><ItemGroup><PackageVersion Include="Native.Dependency" Version="10.0.13"/></ItemGroup></Project>',
  );
  const snapshot = { version, feed, packages: [id] };
  const assets = {
    libraries: {
      [`${id}/${version}`]: {
        type: "package",
        path: `gloaming.example/${version}`,
      },
      "Native.Dependency/10.0.13": {
        type: "package",
        path: "native.dependency/10.0.13",
      },
    } as Record<string, { type: string; path: string }>,
    packageFolders: { [cache]: {} },
    project: {
      restore: { frameworks: { "net10.0": { projectReferences: {} } } },
    },
  };
  const metadata = { source: feed };
  const assetFile = join(consumer, "obj", "project.assets.json");
  const metadataFile = join(
    cache,
    "gloaming.example",
    version,
    ".nupkg.metadata",
  );
  const restores: { args: string[]; cwd: string; env: NodeJS.ProcessEnv }[] =
    [];
  const prepare = (archives = [`${id}.${version}.nupkg`]) =>
    preparePackageProof(root, scratch, (command, args, cwd, env) => {
      if (command === process.execPath) {
        mkdirSync(feed);
        writeFileSync(
          join(feed, `gloaming.${version}.json`),
          JSON.stringify(snapshot),
        );
        for (const archive of archives)
          writeFileSync(join(feed, archive), "fixture archive");
      } else {
        restores.push({ args, cwd, env });
      }
      return "";
    });
  const saveRestore = () => {
    writeFileSync(assetFile, JSON.stringify(assets));
    mkdirSync(join(cache, "gloaming.example", version), { recursive: true });
    writeFileSync(metadataFile, JSON.stringify(metadata));
  };
  return {
    directory,
    root,
    scratch,
    feed,
    cache,
    consumer,
    sdk,
    snapshot,
    assets,
    metadata,
    metadataFile,
    restores,
    prepare,
    saveRestore,
  };
}

test("consumers share the pinned SDK, mapped feed and isolated package provenance", () => {
  const f = fixture();
  try {
    const proof = f.prepare();
    f.saveRestore();
    const consumers = [f.consumer, join(f.scratch, "another-consumer")];
    for (const consumer of consumers) {
      mkdirSync(join(consumer, "obj"), { recursive: true });
      writeFileSync(
        join(consumer, "obj", "project.assets.json"),
        JSON.stringify(f.assets),
      );
      const restored = proof.restore(consumer, [id]);
      assert.deepEqual(restored.gloaming, [`${id}/${version}`]);
      assert.deepEqual(restored.packages, [
        `${id}/${version}`,
        "Native.Dependency/10.0.13",
      ]);
    }
    assert.equal(readFileSync(join(f.scratch, "global.json"), "utf8"), f.sdk);
    const config = new XMLParser({
      ignoreAttributes: false,
      attributeNamePrefix: "",
    }).parse(readFileSync(join(f.scratch, "NuGet.Config"), "utf8"));
    assert.deepEqual(config.configuration.packageSources.add, [
      { key: "local", value: f.feed },
      { key: "nuget.org", value: "https://api.nuget.org/v3/index.json" },
    ]);
    assert.deepEqual(config.configuration.packageSourceMapping.packageSource, [
      { package: { pattern: "Gloaming.*" }, key: "local" },
      { package: { pattern: "*" }, key: "nuget.org" },
    ]);
    assert.equal(f.restores.length, 2);
    for (const restore of f.restores) {
      assert(consumers.includes(restore.cwd));
      assert.equal(restore.env.NUGET_PACKAGES, f.cache);
      assert(restore.args.includes(join(f.scratch, "NuGet.Config")));
    }
  } finally {
    rmSync(f.directory, { recursive: true, force: true });
  }
});

const rejectedRestores = [
  [
    "a different package cache",
    (f) => {
      f.assets.packageFolders = { [join(f.directory, "shared-cache")]: {} };
    },
    /isolated package cache/,
  ],
  [
    "an additional fallback cache",
    (f) => {
      f.assets.packageFolders[join(f.directory, "fallback")] = {};
    },
    /isolated package cache/,
  ],
  [
    "a source project in libraries",
    (f) => {
      f.assets.libraries["Native.Dependency/10.0.13"]!.type = "project";
    },
    /Project\/source dependency/,
  ],
  [
    "a framework ProjectReference",
    (f) => {
      Object.assign(
        f.assets.project.restore.frameworks["net10.0"].projectReferences,
        { "../Library.csproj": {} },
      );
    },
    /ProjectReference/,
  ],
  [
    "an unexpected Gloaming package",
    (f) => {
      f.assets.libraries[`Gloaming.Unrelated/${version}`] = {
        type: "package",
        path: "unrelated",
      };
    },
    /dependency closure/,
  ],
  [
    "a missing selected package",
    (f) => {
      delete f.assets.libraries[`${id}/${version}`];
    },
    /dependency closure/,
  ],
  [
    "another snapshot version",
    (f) => {
      const library = f.assets.libraries[`${id}/${version}`]!;
      delete f.assets.libraries[`${id}/${version}`];
      f.assets.libraries[`${id}/1.0.0-preview.1`] = library;
    },
    /Wrong snapshot version/,
  ],
  [
    "a package from another feed",
    (f) => {
      f.metadata.source = join(f.directory, "another-feed");
    },
    /Wrong package source/,
  ],
  [
    "a package path outside the cache",
    (f) => {
      f.assets.libraries[`${id}/${version}`]!.path = "../outside";
    },
    /escapes isolated cache/,
  ],
] satisfies [string, (f: ReturnType<typeof fixture>) => void, RegExp][];

for (const [name, change, expected] of rejectedRestores) {
  test(`package proof rejects ${name}`, () => {
    const f = fixture();
    try {
      const proof = f.prepare();
      change(f);
      f.saveRestore();
      assert.throws(() => proof.restore(f.consumer, [id]), expected);
    } finally {
      rmSync(f.directory, { recursive: true, force: true });
    }
  });
}

test("package provenance requires retained cache metadata", () => {
  const f = fixture();
  try {
    const proof = f.prepare();
    f.saveRestore();
    rmSync(f.metadataFile);
    assert.throws(() => proof.restore(f.consumer, [id]), /ENOENT/);
  } finally {
    rmSync(f.directory, { recursive: true, force: true });
  }
});

for (const mismatch of ["feed", "inventory", "version", "archives"] as const) {
  test(`package proof rejects a mismatched snapshot ${mismatch}`, () => {
    const f = fixture();
    try {
      if (mismatch === "feed") f.snapshot.feed = join(f.directory, "old-feed");
      if (mismatch === "inventory")
        f.snapshot.packages = ["Gloaming.Unrelated"];
      if (mismatch === "version") f.snapshot.version = "1.0.0-preview.1";
      assert.throws(() => f.prepare(mismatch === "archives" ? [] : undefined));
    } finally {
      rmSync(f.directory, { recursive: true, force: true });
    }
  });
}

test("native dependencies use one explicit central version", () => {
  const f = fixture();
  try {
    assert.deepEqual(nativePackage(f.root, "Native.Dependency"), {
      id: "Native.Dependency",
      version: "10.0.13",
    });
    assert.throws(
      () => nativePackage(f.root, "Missing.Dependency"),
      /one central version/,
    );
    writeFileSync(
      join(f.root, "Directory.Packages.props"),
      '<Project><ItemGroup><PackageVersion Include="Native.Dependency" Version="1.0.0"/><PackageVersion Include="Native.Dependency" Version="2.0.0"/></ItemGroup></Project>',
    );
    assert.throws(
      () => nativePackage(f.root, "Native.Dependency"),
      /one central version/,
    );
  } finally {
    rmSync(f.directory, { recursive: true, force: true });
  }
});
