import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { checkRepository } from "./references.ts";

function repository(
  files: Record<string, string>,
  run: (root: string) => void,
) {
  const root = mkdtempSync(join(tmpdir(), "gloaming-references-"));
  try {
    execFileSync("git", ["init", "--quiet"], { cwd: root });
    for (const [path, contents] of Object.entries(files)) {
      mkdirSync(dirname(join(root, path)), { recursive: true });
      writeFileSync(join(root, path), contents);
    }
    run(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("checks inline links, images and reference definitions with encoded or escaped paths", () => {
  repository(
    {
      "docs/guide.md": [
        "[setup](../src/README.md#setup)",
        '![diagram](<images/a picture.svg> "title")',
        "[Unicode](caf%C3%A9.md?view=1#intro)",
        "[nested](nested(a(b)).md)",
        "[escaped](escaped\\(name\\).md)",
        "[read the details][details]",
        '[details]: ../src/README.md "details"',
        "[section](#missing-anchor) [web](https://example.invalid) [mail](mailto:a@example.invalid)",
      ].join("\n"),
      "src/README.md": "# Setup",
      "docs/images/a picture.svg": "<svg/>",
      "docs/café.md": "",
      "docs/nested(a(b)).md": "",
      "docs/escaped(name).md": "",
    },
    (root) => {
      const result = checkRepository(root);
      assert.deepEqual(result.issues, []);
      assert.equal(result.markdownTargets, 6);
    },
  );
});

test("ignores fenced examples, inline code and comments while preserving failure line numbers", () => {
  repository(
    {
      "README.md": [
        "~~~md",
        "[example](missing.md)",
        "```",
        "~~~",
        "````md",
        "[example](missing.md)",
        "```",
        "````",
        "`[example](missing.md)` <!-- [comment](missing.md) -->",
        "[live](missing.md)",
      ].join("\n"),
    },
    (root) => {
      assert.deepEqual(checkRepository(root).issues, [
        {
          file: "README.md",
          line: 10,
          target: "missing.md",
          reason: "target does not exist",
        },
      ]);
    },
  );
});

test("rejects nonportable paths and directories populated only by ignored build output", () => {
  repository(
    {
      ".gitignore": "obj/\n",
      "obj/generated.cs": "",
      "README.md": [
        "[generated](obj/generated.cs)",
        "[retired](obj/)",
        "[outside](../outside.md)",
        "[absolute](/tmp/local.md)",
        "[bad encoding](bad%zz.md)",
        "[source](src/)",
      ].join("\n"),
      "src/code.cs": "",
    },
    (root) => {
      const result = checkRepository(root);
      assert.equal(result.issues.length, 5);
      assert.match(
        result.issues[0]!.reason,
        /no tracked or non-ignored source/,
      );
      assert.match(
        result.issues[1]!.reason,
        /no tracked or non-ignored source/,
      );
      assert.match(result.issues[2]!.reason, /inside the repository/);
      assert.match(result.issues[3]!.reason, /relative/);
      assert.match(result.issues[4]!.reason, /invalid URI/);
    },
  );
});

test("checks literal solution, project, imported props and linked fixture paths without evaluating MSBuild", () => {
  repository(
    {
      "Example.slnx":
        '<Solution><Project Path="src/Example.csproj" /></Solution>',
      "src/Example.csproj": [
        "<Project><ItemGroup>",
        '<ProjectReference Include="..\\other\\Other.csproj" />',
        "<Compile Include='../Support.cs' />",
        '<None Include="../fixtures/a.json;../fixtures/b.json" />',
        '<Content Include="../fixtures/a&amp;b.json" />',
        '<Compile Include="$(Shared)/dynamic.cs" />',
        '<Compile Include="../**/*.cs" />',
        '<!-- <None Include="missing.json" /> -->',
        '</ItemGroup><Import Project="../Shared.props" /></Project>',
      ].join("\n"),
      "other/Other.csproj": "<Project />",
      "Shared.props":
        '<Project><ItemGroup><AdditionalFiles Include="missing.txt" /></ItemGroup></Project>',
      "Support.cs": "",
      "fixtures/a.json": "{}",
      "fixtures/b.json": "{}",
      "fixtures/a&b.json": "{}",
    },
    (root) => {
      const result = checkRepository(root);
      assert.equal(result.projectTargets, 8);
      assert.equal(result.skippedProjectExpressions, 2);
      assert.deepEqual(result.issues, [
        {
          file: "Shared.props",
          line: 1,
          target: "missing.txt",
          reason: "target does not exist",
        },
      ]);
    },
  );
});

test("CLI rejects a deleted indexed target and includes new untracked documentation", () => {
  repository({ "old.md": "", "README.md": "[old](old.md)" }, (root) => {
    execFileSync("git", ["add", "old.md", "README.md"], { cwd: root });
    unlinkSync(join(root, "old.md"));
    writeFileSync(join(root, "new.md"), "[missing](missing.md)");
    const run = spawnSync(
      process.execPath,
      [fileURLToPath(new URL("check-references.ts", import.meta.url))],
      {
        cwd: root,
        encoding: "utf8",
      },
    );
    assert.equal(run.status, 1);
    assert.match(run.stderr, /README\.md:1: old\.md: target does not exist/);
    assert.match(run.stderr, /new\.md:1: missing\.md: target does not exist/);
    assert.match(run.stdout, /2 broken references/);
  });
});

test("CLI discovers the repository from a subdirectory and passes for portable source links", () => {
  repository(
    { "docs/README.md": "[source](../source.cs)", "source.cs": "" },
    (root) => {
      const run = spawnSync(
        process.execPath,
        [fileURLToPath(new URL("check-references.ts", import.meta.url))],
        {
          cwd: join(root, "docs"),
          encoding: "utf8",
        },
      );
      assert.equal(run.status, 0, run.stderr);
      assert.match(run.stdout, /1 local Markdown targets.*0 broken references/);
    },
  );
});
