import assert from "node:assert/strict";
import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";

export function libraryPackages(root: string) {
  const packages = readdirSync(join(root, "src"), { withFileTypes: true })
    .filter(
      (entry) => entry.isDirectory() && entry.name.startsWith("Gloaming."),
    )
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

  assert(packages.length > 0, "No Gloaming library projects found.");
  assert.equal(new Set(packages.map((item) => item.id)).size, packages.length);
  for (const item of packages)
    assert(
      existsSync(item.project),
      `Missing library project: ${item.project}`,
    );

  return packages;
}
