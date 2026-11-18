import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import {
  dirname,
  extname,
  isAbsolute,
  relative,
  resolve,
  sep,
} from "node:path";

type Reference = { target: string; line: number; markdown: boolean };
export type ReferenceIssue = {
  file: string;
  line: number;
  target: string;
  reason: string;
};

// Preserve line numbers while removing examples that do not declare live links.
function markdownProse(source: string): string {
  let fence: { marker: string; length: number } | undefined;
  return source
    .split("\n")
    .map((line) => {
      const marker = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line);
      if (fence) {
        if (
          marker &&
          marker[1]![0] === fence.marker &&
          marker[1]!.length >= fence.length &&
          marker[2]!.trim() === ""
        ) {
          fence = undefined;
        }
        return "";
      }
      if (marker) {
        fence = { marker: marker[1]![0]!, length: marker[1]!.length };
        return "";
      }
      return line.replace(/(`+)([\s\S]*?)\1(?!`)/g, (match: string) =>
        " ".repeat(match.length),
      );
    })
    .join("\n")
    .replace(/<!--[\s\S]*?-->/g, (match) => match.replace(/[^\n]/g, " "));
}

function markdownReferences(source: string): Reference[] {
  const prose = markdownProse(source);
  const references: Reference[] = [];
  for (const match of prose.matchAll(/\]\(\s*|^ {0,3}\[[^\]\n]+\]:[ \t]*/gm)) {
    let cursor = match.index + match[0].length;
    let target = "";
    if (prose[cursor] === "<") {
      const end = prose.indexOf(">", cursor + 1);
      if (end < 0) continue;
      target = prose.slice(cursor + 1, end);
    } else {
      let depth = 0;
      for (; cursor < prose.length; cursor++) {
        const char = prose[cursor]!;
        if (char === "\\" && cursor + 1 < prose.length) {
          target += char + prose[++cursor]!;
          continue;
        }
        if (/\s/.test(char) || (char === ")" && depth === 0)) break;
        if (char === "(") depth++;
        if (char === ")") depth--;
        target += char;
      }
    }
    references.push({
      target,
      line: prose.slice(0, match.index).split("\n").length,
      markdown: true,
    });
  }
  return references;
}

function projectReferences(source: string): Reference[] {
  const xml = source.replace(/<!--[\s\S]*?-->/g, (match) =>
    match.replace(/[^\n]/g, " "),
  );
  const references: Reference[] = [];
  for (const tag of xml.matchAll(
    /<(ProjectReference|Compile|None|Content|EmbeddedResource|AdditionalFiles|Project|Import)\b([^>]+)>/g,
  )) {
    const attribute =
      tag[1] === "Project"
        ? "Path"
        : tag[1] === "Import"
          ? "Project"
          : "Include";
    const value = new RegExp(`\\b${attribute}\\s*=\\s*["']([^"']+)["']`).exec(
      tag[2]!,
    );
    if (!value) continue;
    const decoded = value[1]!
      .replace(/&quot;/g, '"')
      .replace(/&apos;/g, "'")
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&amp;/g, "&");
    for (const target of decoded.split(";")) {
      references.push({
        target,
        line: xml.slice(0, tag.index).split("\n").length,
        markdown: false,
      });
    }
  }
  return references;
}

export function checkRepository(root: string) {
  const paths = execFileSync(
    "git",
    ["ls-files", "--cached", "--others", "--exclude-standard", "-z"],
    {
      cwd: root,
      encoding: "utf8",
    },
  )
    .split("\0")
    .filter(Boolean);
  const owned = new Set(
    paths.filter((path) => existsSync(resolve(root, path))),
  );
  const directories = new Set<string>(["."]);
  for (const path of owned) {
    let parent = dirname(path);
    while (parent !== ".") {
      directories.add(parent);
      parent = dirname(parent);
    }
  }
  const issues: ReferenceIssue[] = [];
  let markdownTargets = 0;
  let projectTargets = 0;
  let skippedProjectExpressions = 0;
  for (const file of [...owned].sort()) {
    const extension = extname(file).toLowerCase();
    if (![".md", ".csproj", ".slnx", ".props", ".targets"].includes(extension))
      continue;
    const source = readFileSync(resolve(root, file), "utf8");
    const references =
      extension === ".md"
        ? markdownReferences(source)
        : projectReferences(source);
    for (const reference of references) {
      let target = reference.target;
      if (reference.markdown) {
        if (
          !target ||
          target.startsWith("#") ||
          /^(?:[a-z][\w+.-]*:|\/\/)/i.test(target)
        )
          continue;
        target = target.split(/[?#]/)[0]!;
        try {
          target = decodeURIComponent(target).replace(
            /\\([!"#$%&'()*+,\-./:;<=>?@[\]^_`{|}~\\])/g,
            "$1",
          );
        } catch {
          issues.push({
            file,
            line: reference.line,
            target,
            reason: "invalid URI encoding",
          });
          continue;
        }
        markdownTargets++;
      } else {
        if (/[$@%]\(|[*?]/.test(target)) {
          skippedProjectExpressions++;
          continue;
        }
        target = target.replaceAll("\\", "/");
        projectTargets++;
      }
      const absolute = resolve(root, dirname(file), target);
      const path = relative(root, absolute).split(sep).join("/");
      let reason: string | undefined;
      if (
        isAbsolute(target) ||
        /^[a-z]:/i.test(target) ||
        path === ".." ||
        path.startsWith("../")
      ) {
        reason = "target must be relative and inside the repository";
      } else if (!existsSync(absolute)) {
        reason = "target does not exist";
      } else if (
        !(statSync(absolute).isDirectory()
          ? directories.has(path || ".")
          : owned.has(path))
      ) {
        reason = "target has no tracked or non-ignored source";
      }
      if (reason)
        issues.push({
          file,
          line: reference.line,
          target: reference.target,
          reason,
        });
    }
  }
  return { markdownTargets, projectTargets, skippedProjectExpressions, issues };
}
