import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash, randomBytes } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  basename,
  dirname,
  isAbsolute,
  join,
  relative,
  resolve,
  sep,
} from "node:path";
import { XMLParser, XMLValidator } from "fast-xml-parser";

const root = resolve(import.meta.dirname, "../..");
const dotnet = process.env.DOTNET_HOST_PATH ?? "dotnet";
const connection = process.env.GLOAMING_PACKAGE_TEST_CONNECTION_STRING;
assert(
  connection,
  "Set GLOAMING_PACKAGE_TEST_CONNECTION_STRING to a PostgreSQL connection with CREATEDB permission; full verification never skips PostgreSQL.",
);
assert.equal(
  process.argv.length,
  2,
  "Usage: node tools/release/verify.ts (no arguments)",
);
const scratch = mkdtempSync(join(tmpdir(), "gloaming-package-proof-"));
const feed = join(scratch, "feed");
const version = `1.0.0-local.${Date.now()}.${randomBytes(4).toString("hex")}`;
const parser = new XMLParser({
  ignoreAttributes: false,
  attributeNamePrefix: "",
  parseTagValue: false,
});
const xml = (value: string): string =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
const array = <T>(value: T | T[] | undefined): T[] =>
  value === undefined ? [] : Array.isArray(value) ? value : [value];
let logNumber = 0;

function run(
  command: string,
  args: string[],
  cwd = root,
  env = process.env,
  succeeds = true,
): string {
  console.log(`> ${command} ${args.join(" ")}`);
  const result = spawnSync(command, args, {
    cwd,
    env,
    encoding: "utf8",
    maxBuffer: 50 * 1024 * 1024,
    timeout: 600_000,
  });
  const output = (result.stdout ?? "") + (result.stderr ?? "");
  // The credential is passed only through the inherited environment, never command arguments.
  const safe = output.replaceAll(connection!, "[redacted connection]");
  const log = join(
    scratch,
    `${String(++logNumber).padStart(3, "0")}-${basename(command)}.log`,
  );
  writeFileSync(log, safe);
  assert(!result.error, `${command}: ${result.error?.message}; log: ${log}`);
  assert.equal(
    result.status === 0,
    succeeds,
    `${command} failed (${result.status}); log: ${log}\n${safe}`,
  );
  return safe;
}

function readXml(file: string): unknown {
  const source = readFileSync(file, "utf8");
  assert.equal(XMLValidator.validate(source), true, `Invalid XML: ${file}`);
  return parser.parse(source) as unknown;
}

interface Project {
  Project: {
    ItemGroup?:
      | { ProjectReference?: { Include: string } | { Include: string }[] }
      | { ProjectReference?: { Include: string } | { Include: string }[] }[];
  };
}
interface Dependency {
  id: string;
  version: string;
}
interface Nuspec {
  package: {
    metadata: {
      id: string;
      version: string;
      authors: string;
      license: { type: string; "#text": string };
      readme: string;
      repository: { type: string; url: string };
      dependencies?: {
        group?:
          | { dependency?: Dependency | Dependency[] }
          | { dependency?: Dependency | Dependency[] }[];
      };
    };
  };
}
interface Assets {
  libraries: Record<string, { type: string; path: string }>;
  packageFolders: Record<string, unknown>;
  project: {
    restore: {
      originalTargetFrameworks: string[];
      frameworks: Record<
        string,
        { projectReferences: Record<string, unknown> }
      >;
    };
  };
}

const packages = readdirSync(join(root, "src"), { withFileTypes: true })
  .filter((entry) => entry.isDirectory() && entry.name.startsWith("Gloaming."))
  .flatMap((family) =>
    readdirSync(join(root, "src", family.name), { withFileTypes: true })
      .filter(
        (entry) => entry.isDirectory() && entry.name.startsWith("Gloaming."),
      )
      .map((entry) => ({
        id: entry.name,
        project: join(
          root,
          "src",
          family.name,
          entry.name,
          `${entry.name}.csproj`,
        ),
      })),
  )
  .sort((left, right) => left.id.localeCompare(right.id));
assert.equal(
  packages.length,
  23,
  "Review the package inventory when adding/removing libraries.",
);
for (const item of packages)
  assert(existsSync(item.project), `Missing library project: ${item.project}`);

function readmeFor(project: string): string {
  const local = join(dirname(project), "README.md");
  return existsSync(local) ? local : join(dirname(project), "../README.md");
}

function renderReadme(file: string): string {
  return readFileSync(file, "utf8").replace(
    /\]\(([^\s)]+)\)/g,
    (match: string, target: string) => {
      if (/^(?:[a-z][a-z0-9+.-]*:|#|\/\/)/i.test(target)) return match;
      const [path, fragment] = target.split("#", 2);
      assert(
        path && !isAbsolute(path),
        `Unsupported README link: ${file}: ${target}`,
      );
      const destination = resolve(
        dirname(file),
        decodeURIComponent(path).replaceAll("\\", sep),
      );
      assert(
        existsSync(destination),
        `Broken README link: ${relative(root, file)} -> ${target}`,
      );
      const repoPath = relative(root, destination)
        .split(sep)
        .map((segment) =>
          encodeURIComponent(segment).replace(
            /[!'()*]/g,
            (character) =>
              `%${character.charCodeAt(0).toString(16).toUpperCase()}`,
          ),
        )
        .join("/");
      assert(
        !repoPath.startsWith("../"),
        `README link escapes repository: ${target}`,
      );
      return `](https://github.com/oxface/modulith-foundry/blob/main/${repoPath}${fragment ? `#${fragment}` : ""})`;
    },
  );
}

const indexBefore = run("git", ["diff", "--cached", "--binary"]);
mkdirSync(feed);
console.log(`Local package proof ${version}; artifacts and logs: ${scratch}`);

try {
  // Exercise the native SDK task against independent literal expectations, including
  // encoded path segments and both separator spellings, before packing real libraries.
  const rendererRoot = join(scratch, "readme-renderer");
  mkdirSync(join(rendererRoot, "docs"), { recursive: true });
  writeFileSync(
    join(rendererRoot, "docs", "owner's résumé (v1).md"),
    "# Fixture\n",
  );
  writeFileSync(join(scratch, "outside.md"), "# Outside\n");
  const rendererSource = join(rendererRoot, "README.md");
  const rendererOutput = join(scratch, "rendered-README.md");
  const rendererProject = join(scratch, "readme-renderer.proj");
  writeFileSync(
    rendererProject,
    `<Project><PropertyGroup><IsGloamingPackage>true</IsGloamingPackage></PropertyGroup><Import Project="${xml(join(root, "src", "Directory.Build.targets"))}"/><Target Name="RenderProof"><RenderGloamingPackageReadme SourceFile="${xml(rendererSource)}" RepositoryRoot="${xml(rendererRoot)}" RepositoryUrl="https://github.com/example/proof" DestinationFile="${xml(rendererOutput)}"/></Target></Project>`,
  );
  const encodedPath = "owner%27s%20r%C3%A9sum%C3%A9%20%28v1%29.md";
  writeFileSync(
    rendererSource,
    `[posix](docs/${encodedPath}#scope)\n[windows](docs\\${encodedPath})\n[external](https://example.test/help#x)\n[anchor](#local)\n[directory](docs/)\n`,
  );
  const expectedRenderer = `[posix](https://github.com/example/proof/blob/main/docs/${encodedPath}#scope)\n[windows](https://github.com/example/proof/blob/main/docs/${encodedPath})\n[external](https://example.test/help#x)\n[anchor](#local)\n[directory](https://github.com/example/proof/blob/main/docs)\n`;
  run(dotnet, ["msbuild", rendererProject, "-t:RenderProof", "-nologo"]);
  assert.equal(readFileSync(rendererOutput, "utf8"), expectedRenderer);
  for (const [input, message] of [
    ["[missing](docs/absent.md)", "Broken README link"],
    ["[escape](../outside.md)", "README link escapes repository"],
  ]) {
    writeFileSync(rendererSource, input!);
    const failure = run(
      dotnet,
      ["msbuild", rendererProject, "-t:RenderProof", "-nologo"],
      root,
      process.env,
      false,
    );
    assert(failure.includes(message!));
    assert.equal(readFileSync(rendererOutput, "utf8"), expectedRenderer);
  }
  console.log(
    "PASS native README rendering: encoded paths, both separators, external/anchor links, missing targets and repository escape rejection",
  );

  for (const item of packages) {
    const rendered = join(scratch, `${item.id}.README.md`);
    writeFileSync(rendered, renderReadme(readmeFor(item.project)));
    run(dotnet, [
      "pack",
      item.project,
      "--configuration",
      "Release",
      "--output",
      feed,
      `-p:PackageVersion=${version}`,
      `-p:Version=${version}`,
      "--nologo",
    ]);
  }
  const archives = readdirSync(feed).filter((file) => file.endsWith(".nupkg"));
  assert.equal(archives.length, packages.length);
  const unzip = join(scratch, "unzip.proj");
  writeFileSync(
    unzip,
    `<Project><Target Name="Extract"><Unzip SourceFiles="$(Archive)" DestinationFolder="$(Destination)" /></Target></Project>`,
  );
  const manifest: object[] = [];
  for (const item of packages) {
    const archive = join(feed, `${item.id}.${version}.nupkg`);
    assert(existsSync(archive), `Missing package: ${archive}`);
    const extracted = join(scratch, "unpacked", item.id);
    run(dotnet, [
      "msbuild",
      unzip,
      "-t:Extract",
      `-p:Archive=${archive}`,
      `-p:Destination=${extracted}`,
      "-nologo",
    ]);
    for (const directory of ["build", "buildTransitive", "tools"])
      assert(
        !existsSync(join(extracted, directory)),
        `Repository pack preparation leaked into ${item.id}: ${directory}`,
      );
    const metadata = (readXml(join(extracted, `${item.id}.nuspec`)) as Nuspec)
      .package.metadata;
    assert.equal(metadata.id, item.id);
    assert.equal(metadata.version, version);
    assert.equal(metadata.authors, "oxface");
    assert.equal(metadata.license.type, "expression");
    assert.equal(metadata.license["#text"], "Apache-2.0");
    assert.equal(metadata.repository.type, "git");
    assert.equal(
      metadata.repository.url,
      "https://github.com/oxface/modulith-foundry",
    );
    assert.equal(metadata.readme, "README.md");
    assert.equal(
      readFileSync(join(extracted, "README.md"), "utf8"),
      readFileSync(join(scratch, `${item.id}.README.md`), "utf8"),
    );
    assert(
      existsSync(join(extracted, "lib", "net10.0", `${item.id}.dll`)),
      "Missing assembly.",
    );
    const docs = readXml(
      join(extracted, "lib", "net10.0", `${item.id}.xml`),
    ) as { doc: { assembly: { name: string }; members: unknown } };
    assert.equal(docs.doc.assembly.name, item.id);
    assert(docs.doc.members, "Missing XML documentation members.");
    const dependencies = array(metadata.dependencies?.group).flatMap((group) =>
      array(group.dependency),
    );
    const internal = dependencies.filter((dependency) =>
      dependency.id.startsWith("Gloaming."),
    );
    const project = readXml(item.project) as Project;
    const expected = array(project.Project.ItemGroup)
      .flatMap((group) => array(group.ProjectReference))
      .map((ref) => basename(ref.Include, ".csproj"))
      .sort();
    assert.deepEqual(
      internal.map((dependency) => dependency.id).sort(),
      expected,
      `Wrong dependency graph: ${item.id}`,
    );
    for (const dependency of internal)
      assert(
        [version, `[${version}]`].includes(dependency.version),
        `Wrong internal dependency version: ${item.id} -> ${dependency.id} ${dependency.version}`,
      );
    manifest.push({
      id: item.id,
      version,
      sha256: createHash("sha256").update(readFileSync(archive)).digest("hex"),
      dependencies,
    });
  }

  const config = join(scratch, "NuGet.Config");
  writeFileSync(
    config,
    `<configuration><packageSources><clear/><add key="local" value="${xml(feed)}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/><packageSource key="local"><package pattern="Gloaming.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>`,
  );
  const packageCache = join(scratch, "nuget-packages");
  const consumerEnv = { ...process.env, NUGET_PACKAGES: packageCache };
  const profiles = [
    {
      name: "core",
      source: "Core.cs",
      ids: ["Gloaming.ActorIdentity", "Gloaming.Tenancy"],
      external: false,
    },
    {
      name: "events",
      source: "Events.cs",
      ids: ["Gloaming.Events.Serialization", "Gloaming.Events.History"],
      external: false,
    },
    {
      name: "postgres",
      source: "Postgres.cs",
      ids: [
        "Gloaming.Persistence.EntityFrameworkCore",
        "Gloaming.Messaging.EntityFrameworkCore.Postgres",
        "Gloaming.Auditing.EntityFrameworkCore.Postgres",
      ],
      external: true,
    },
    {
      name: "all-packages",
      source: undefined,
      ids: packages.map((item) => item.id),
      external: false,
    },
  ];
  for (const profile of profiles) {
    const directory = join(scratch, "consumers", profile.name);
    mkdirSync(directory, { recursive: true });
    writeFileSync(
      join(directory, "Program.cs"),
      profile.source
        ? readFileSync(
            join(import.meta.dirname, "consumers", profile.source),
            "utf8",
          )
        : 'Console.WriteLine("PASS all 23 package references compile together");',
    );
    writeFileSync(
      join(directory, "Consumer.csproj"),
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>${profile.ids.map((id) => `<PackageReference Include="${id}" Version="[${version}]"/>`).join("")}${profile.external ? '<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12"/>' : ""}</ItemGroup></Project>`,
    );
    run(
      dotnet,
      [
        "restore",
        "Consumer.csproj",
        "--configfile",
        config,
        "--no-cache",
        "--nologo",
      ],
      directory,
      consumerEnv,
    );
    const assets = JSON.parse(
      readFileSync(join(directory, "obj", "project.assets.json"), "utf8"),
    ) as Assets;
    const gloaming = Object.entries(assets.libraries).filter(([name]) =>
      name.startsWith("Gloaming."),
    );
    for (const [name, library] of Object.entries(assets.libraries))
      assert.equal(
        library.type,
        "package",
        `Project/source dependency in ${profile.name}: ${name}`,
      );
    for (const framework of Object.values(assets.project.restore.frameworks))
      assert.equal(
        Object.keys(framework.projectReferences).length,
        0,
        `ProjectReference in ${profile.name}`,
      );
    const expected = new Set(profile.ids);
    // Expand the reviewed project graph only for the selected roots.
    const expand = (id: string): void => {
      const item = packages.find((item) => item.id === id)!;
      const project = readXml(item.project) as Project;
      for (const ref of array(project.Project.ItemGroup).flatMap((group) =>
        array(group.ProjectReference),
      )) {
        const dependency = basename(ref.Include, ".csproj");
        if (!expected.has(dependency)) {
          expected.add(dependency);
          expand(dependency);
        }
      }
    };
    for (const id of profile.ids) expand(id);
    assert.deepEqual(
      gloaming.map(([name]) => name.split("/")[0]!).sort(),
      [...expected].sort(),
      `Unrelated Gloaming family introduced into ${profile.name}`,
    );
    for (const [name, library] of gloaming) {
      assert.equal(name.split("/")[1], version);
      assert(
        existsSync(join(packageCache, library.path, ".nupkg.metadata")),
        `Package not restored into isolated cache: ${name}`,
      );
    }
    run(
      dotnet,
      [
        "run",
        "--project",
        "Consumer.csproj",
        "--configuration",
        "Release",
        "--no-restore",
        "--nologo",
      ],
      directory,
      consumerEnv,
    );
    console.log(
      `PASS ${profile.name}: ${gloaming.length} Gloaming packages; zero ProjectReferences; isolated package cache`,
    );
  }
  writeFileSync(
    join(scratch, "manifest.json"),
    JSON.stringify(
      {
        version,
        packages: manifest,
        profiles: profiles.map((profile) => profile.name),
        verifiedAt: new Date().toISOString(),
      },
      null,
      2,
    ) + "\n",
  );
  console.log(
    `PASS release package/adoption proof. Evidence: ${join(scratch, "manifest.json")}`,
  );
} finally {
  assert.equal(
    run("git", ["diff", "--cached", "--binary"]),
    indexBefore,
    "The staged review snapshot changed during verification.",
  );
  // Keep packages, consumers and logs for owner inspection; never retain credentials in files.
  rmSync(join(scratch, "NuGet.Config"), { force: true });
}
