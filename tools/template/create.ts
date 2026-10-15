import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  cpSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { constants } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { getSystemErrorMessage, parseArgs } from "node:util";
import { parseTree } from "jsonc-parser";
import koffi from "koffi";

export const root = resolve(import.meta.dirname, "../..");
export const libraries = [
  "Gloaming.ActorIdentity",
  "Gloaming.Tenancy",
  "Gloaming.EntityFrameworkCore",
  "Gloaming.Persistence.EntityFrameworkCore",
] as const;

export const librarySources: Record<(typeof libraries)[number], string> = {
  "Gloaming.ActorIdentity": join(
    root,
    "src/Gloaming.ActorIdentity/Gloaming.ActorIdentity",
  ),
  "Gloaming.Tenancy": join(root, "src/Gloaming.Tenancy/Gloaming.Tenancy"),
  "Gloaming.EntityFrameworkCore": join(
    root,
    "src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore",
  ),
  "Gloaming.Persistence.EntityFrameworkCore": join(
    root,
    "src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore",
  ),
};

export type Configuration = { applicationName: string; rootNamespace: string };

export function snapshotBytes(
  name: (typeof libraries)[number],
  file: string,
): Buffer {
  const upstream = librarySources[name];
  const bytes = readFileSync(join(upstream, file));
  if (!file.endsWith(".csproj")) return bytes;
  // Source snapshots live in a flat libraries directory. Only native project
  // references need relocation; C# source remains byte-for-byte upstream.
  return Buffer.from(
    bytes
      .toString("utf8")
      .replace(
        /(<ProjectReference\s+Include=")([^"]+)(")/g,
        (_, before: string, include: string, after: string) => {
          const target = libraries.find(
            (candidate) =>
              resolve(librarySources[candidate], `${candidate}.csproj`) ===
              resolve(upstream, include),
          );
          if (!target)
            throw new Error(`Unselected snapshot dependency: ${include}`);
          return `${before}../${target}/${target}.csproj${after}`;
        },
      ),
  );
}

const keywords = new Set(
  `
abstract as base bool break byte case catch char checked class const continue decimal
default delegate do double else enum event explicit extern false finally fixed float
for foreach goto if implicit in int interface internal is lock long namespace new null
object operator out override params private protected public readonly ref return sbyte
sealed short sizeof stackalloc static string struct switch this throw true try typeof
uint ulong unchecked unsafe ushort using virtual void volatile while
__arglist __makeref __reftype __refvalue
`
    .trim()
    .split(/\s+/),
);

function identifier(value: string): boolean {
  return /^[A-Za-z_][A-Za-z0-9_]{0,63}$/.test(value) && !keywords.has(value);
}

export function exists(path: string): boolean {
  try {
    lstatSync(path); // Includes dangling symlinks; existsSync would follow them.
    return true;
  } catch (error) {
    if (error instanceof Error && "code" in error && error.code === "ENOENT")
      return false;
    throw error;
  }
}

export function sha256(bytes: Buffer): string {
  return createHash("sha256").update(bytes).digest("hex");
}

function json(value: Record<string, unknown>): string {
  return (
    JSON.stringify(
      Object.fromEntries(
        Object.keys(value)
          .sort()
          .map((key) => [key, value[key]]),
      ),
      null,
      2,
    ) + "\n"
  );
}

function configuration(path: string): Configuration {
  const text = readFileSync(path, "utf8");
  const value: unknown = JSON.parse(text); // Strict JSON: no comments/trailing commas.
  // JSON.parse keeps only the last duplicate. Inspect parsed property nodes as well,
  // including escaped spellings of the same key, without writing a JSON parser.
  const tree = parseTree(text);
  const keys =
    tree?.children?.map((property) => String(property.children?.[0]?.value)) ??
    [];
  if (tree?.type === "object" && new Set(keys).size !== keys.length) {
    throw new Error("Duplicate configuration key.");
  }
  if (
    typeof value !== "object" ||
    value === null ||
    Array.isArray(value) ||
    Object.keys(value).length !== 2 ||
    !("applicationName" in value) ||
    !("rootNamespace" in value)
  ) {
    throw new Error(
      "Configuration requires exactly applicationName and rootNamespace.",
    );
  }
  const { applicationName: name, rootNamespace: namespace } = value;
  if (typeof name !== "string" || !identifier(name)) {
    throw new Error(
      "applicationName must be a non-keyword ASCII C# identifier (1–64 characters).",
    );
  }
  if (/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/i.test(name)) {
    throw new Error("applicationName cannot be a reserved device name.");
  }
  if (
    typeof namespace !== "string" ||
    namespace.length > 200 ||
    !namespace.split(".").every(identifier)
  ) {
    throw new Error(
      "rootNamespace must be dot-separated non-keyword ASCII C# identifiers (at most 200 characters).",
    );
  }
  if (
    ["FoundryApplication", "ConsumerRoot"].some(
      (token) => name.includes(token) || namespace.includes(token),
    )
  ) {
    throw new Error("Configuration cannot contain template source tokens.");
  }
  return { applicationName: name, rootNamespace: namespace };
}

function run(args: string[], cwd: string): void {
  const result = spawnSync("dotnet", args, {
    cwd,
    stdio: ["ignore", "inherit", "inherit"],
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(
      `dotnet ${args.slice(0, 2).join(" ")} failed (${result.status ?? result.signal}).`,
    );
}

function publish(source: string, destination: string): void {
  // Node's rename can replace an empty directory. Keep Linux's atomic no-replace
  // primitive, so even a destination created after our preflight remains intact.
  const libc = koffi.load("libc.so.6");
  const rename = libc.func(
    "int renameat2(int olddirfd, const char *oldpath, int newdirfd, const char *newpath, unsigned int flags)",
  );
  const AT_FDCWD = -100;
  const RENAME_NOREPLACE = 1;
  if (rename(AT_FDCWD, source, AT_FDCWD, destination, RENAME_NOREPLACE) !== 0) {
    const code = koffi.errno();
    if (code === constants.errno.EEXIST)
      throw new Error(
        "Output was occupied during creation; nothing was replaced.",
      );
    throw new Error(`Cannot publish output: ${getSystemErrorMessage(-code)}.`);
  }
}

export function create(configPath: string, destination: string): void {
  const config = configuration(configPath);
  const output = resolve(destination);
  if (process.platform !== "linux")
    throw new Error(
      "T1 creation supports Linux with glibc only (atomic no-replace directory rename).",
    );
  if (exists(output))
    throw new Error(
      "Output must be a new directory; existing files, directories and links are refused.",
    );
  if (!exists(dirname(output)) || !statSync(dirname(output)).isDirectory())
    throw new Error("Output parent must already exist.");
  const scratch = mkdtempSync(join(dirname(output), ".foundry-create-"));
  try {
    const template = join(scratch, "template");
    cpSync(join(root, "templates/state-stored"), template, {
      recursive: true,
      filter: (path) => !["bin", "obj"].includes(basename(path)),
    });
    // SDK resolution starts at the command's working directory, not the template
    // path. Use the consumer pin for both native commands, overriding parent pins.
    writeFileSync(
      join(scratch, "global.json"),
      readFileSync(join(template, "global.json")),
    );
    const manifest: Record<string, string> = {};
    for (const name of libraries) {
      const upstream = librarySources[name];
      const target = join(template, "libraries", name);
      mkdirSync(target, { recursive: true });
      for (const file of readdirSync(upstream)
        .filter((file) => /\.(cs|csproj)$/.test(file))
        .sort()) {
        const bytes = snapshotBytes(name, file);
        writeFileSync(join(target, file), bytes);
        manifest[`libraries/${name}/${file}`] = sha256(bytes);
      }
    }
    writeFileSync(join(template, "library-sources.json"), json(manifest));
    const hive = join(scratch, "hive");
    run(["new", "install", template, "--debug:custom-hive", hive], scratch);
    const generated = join(scratch, "generated");
    run(
      [
        "new",
        "foundry-state-stored",
        "--name",
        config.applicationName,
        "--RootNamespace",
        config.rootNamespace,
        "--output",
        generated,
        "--debug:custom-hive",
        hive,
      ],
      scratch,
    );
    writeFileSync(join(generated, "foundry.json"), json(config));
    publish(generated, output);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
  console.log(`Created ${output}`);
}

if (resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  try {
    const { values } = parseArgs({
      options: { config: { type: "string" }, output: { type: "string" } },
    });
    if (!values.config || !values.output)
      throw new Error(
        "Usage: npm run create -- --config <json> --output <new-directory>",
      );
    create(values.config, values.output);
  } catch (error) {
    console.error(
      `Creation failed: ${error instanceof Error ? error.message : String(error)}`,
    );
    process.exitCode = 2;
  }
}
