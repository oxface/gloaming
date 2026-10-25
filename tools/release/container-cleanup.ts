import { spawnSync } from "node:child_process";

export type ContainerRemoval = {
  name: string;
  outcome: "removed" | "absent" | "failed";
  exitCode: number | null;
  diagnostic: string;
};

// Names belong to this invocation. Attempt every removal, including after a failed
// create whose server-side outcome is unknown. Never infer absence from any error.
export function removeOwnedContainers(
  runtime: string,
  names: readonly string[],
): ContainerRemoval[] {
  return names.map((name) => {
    const result = spawnSync(runtime, ["rm", "--force", "--volumes", name], {
      encoding: "utf8",
      timeout: 30_000,
    });
    const diagnostic = [result.error?.message, result.stderr?.trim()]
      .filter(Boolean)
      .join("\n");
    const confirmedAbsent =
      !result.error &&
      result.status === 1 &&
      [
        `Error response from daemon: No such container: ${name}`,
        `Error: no container with name or ID "${name}" found: no such container`,
      ].includes(diagnostic);
    return {
      name,
      outcome:
        !result.error && result.status === 0
          ? "removed"
          : confirmedAbsent
            ? "absent"
            : "failed",
      exitCode: result.status,
      diagnostic,
    };
  });
}
