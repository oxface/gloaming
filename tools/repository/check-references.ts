import { execFileSync } from "node:child_process";
import { checkRepository } from "./references.ts";

try {
  const root = execFileSync("git", ["rev-parse", "--show-toplevel"], {
    encoding: "utf8",
  }).trim();
  const result = checkRepository(root);
  for (const issue of result.issues) {
    console.error(
      `${issue.file}:${issue.line}: ${issue.target}: ${issue.reason}`,
    );
  }
  console.log(
    `Checked ${result.markdownTargets} local Markdown targets and ${result.projectTargets} literal project/source targets; ` +
      `skipped ${result.skippedProjectExpressions} MSBuild expressions/globs. ${result.issues.length} broken references.`,
  );
  if (result.issues.length) process.exitCode = 1;
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}
