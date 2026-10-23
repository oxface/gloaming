/**
 * Manual ES2 design diagnostic: native SQL, separate from the C# library tests.
 * Run with Node 24.21+ and rootless Podman:
 *   node src/Gloaming.EventSourcing/docs/proofs/es2-coordination.ts
 * Uses one disposable PostgreSQL 18.6 container without published host ports.
 */
import assert from "node:assert/strict";
import { execFileSync, spawn, spawnSync } from "node:child_process";
import type { ChildProcessWithoutNullStreams } from "node:child_process";
import { randomUUID } from "node:crypto";
import { createInterface } from "node:readline";
import { setTimeout as delay } from "node:timers/promises";

const container = `gloaming-es2-review-${randomUUID().slice(0, 10)}`;
const sessions: Session[] = [];
const psql = ["psql", "-X", "-qAt", "-v", "ON_ERROR_STOP=1", "-U", "postgres"];

function control(sql: string): string {
  return execFileSync("podman", ["exec", container, ...psql, "-c", sql], {
    encoding: "utf8",
    timeout: 15_000,
  }).trim();
}

class Session {
  readonly process: ChildProcessWithoutNullStreams;
  private readonly lines: string[] = [];
  private wake: (() => void) | undefined;
  private ended = false;
  private failure: Error | undefined;

  constructor() {
    this.process = spawn("podman", ["exec", "-i", container, ...psql]);
    for (const stream of [this.process.stdout, this.process.stderr]) {
      createInterface({ input: stream }).on("line", (line) => {
        this.lines.push(line);
        this.wake?.();
      });
    }
    this.process.stdin.on("error", (error) => {
      this.failure = error;
      this.wake?.();
    });
    this.process.on("error", (error) => {
      this.failure = error;
      this.wake?.();
    });
    this.process.on("close", () => {
      this.ended = true;
      this.wake?.();
    });
  }

  mark(sql: string): string {
    const marker = `ES2_MARK_${randomUUID()}`;
    this.process.stdin.write(`${sql} SELECT '${marker}';\n`);
    return marker;
  }

  async until(marker: string): Promise<string[]> {
    const output: string[] = [];
    const deadline = Date.now() + 15_000;
    while (true) {
      const line = this.lines.shift();
      if (line === marker) return output;
      if (line !== undefined) {
        if (line !== "") output.push(line);
        continue;
      }
      if (this.failure) throw this.failure;
      if (this.ended) throw new Error(`Session ended: ${output.join("\n")}`);
      const remaining = deadline - Date.now();
      if (remaining <= 0) throw new Error(`SQL marker timeout: ${marker}`);
      await new Promise<void>((resolve, reject) => {
        const timer = setTimeout(() => {
          this.wake = undefined;
          reject(new Error(`SQL marker timeout: ${marker}`));
        }, remaining);
        this.wake = () => {
          clearTimeout(timer);
          this.wake = undefined;
          resolve();
        };
      });
    }
  }

  command(sql: string): Promise<string[]> {
    return this.until(this.mark(sql));
  }

  async close(): Promise<void> {
    if (this.ended) return;
    // Close all sessions concurrently so cleanup cannot wait behind another session's lock.
    this.process.stdin.end("ROLLBACK;\n");
    await new Promise<void>((resolve) => {
      const timer = setTimeout(() => {
        this.process.kill();
        resolve();
      }, 5_000);
      this.process.once("close", () => {
        clearTimeout(timer);
        resolve();
      });
    });
  }
}

async function session(role: string): Promise<Session> {
  const current = new Session();
  sessions.push(current);
  await current.command(`SET application_name='${role}';`);
  return current;
}

async function waiting(role: string): Promise<void> {
  const deadline = Date.now() + 10_000;
  while (Date.now() < deadline) {
    if (
      control(
        `SELECT count(*) FROM pg_stat_activity WHERE application_name='${role}' AND wait_event='advisory';`,
      ) === "1"
    )
      return;
    await delay(50);
  }
  throw new Error(`No observed advisory wait for ${role}`);
}

function reset(amount: number): void {
  control(
    `TRUNCATE facts; DELETE FROM projections; DELETE FROM streams; INSERT INTO streams VALUES ('A',1); INSERT INTO facts VALUES ('A',1,10); INSERT INTO projections VALUES ('A',1,${amount});`,
  );
}

function acquire(mode: "write" | "rebuild"): string {
  const operation =
    mode === "write" ? "pg_advisory_xact_lock_shared" : "pg_advisory_xact_lock";
  return `SELECT ${operation}(hashtextextended('A',0));`;
}

async function append(current: Session): Promise<string[]> {
  const result = await current.command(
    "UPDATE streams SET version=2 WHERE id='A' AND version=1 RETURNING version;",
  );
  if (result.length === 1 && result[0] === "2")
    await current.command(
      "INSERT INTO facts VALUES ('A',2,5); UPDATE projections SET amount=amount+5,version=2 WHERE id='A'; COMMIT;",
    );
  return result;
}

try {
  execFileSync(
    "podman",
    [
      "run",
      "--rm",
      "-d",
      "--name",
      container,
      "-e",
      "POSTGRES_PASSWORD=es2-review-local",
      "postgres:18.6",
    ],
    { stdio: "ignore", timeout: 60_000 },
  );
  let ready = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    if (
      spawnSync("podman", ["exec", container, "pg_isready", "-U", "postgres"], {
        stdio: "ignore",
        timeout: 5_000,
      }).status === 0
    ) {
      ready = true;
      break;
    }
    await delay(200);
  }
  assert(ready, "Disposable PostgreSQL not ready");
  console.log("Database:", control("SHOW server_version;"));
  control(
    "CREATE TABLE streams(id text PRIMARY KEY,version bigint); CREATE TABLE facts(id text,version bigint,delta int,PRIMARY KEY(id,version)); CREATE TABLE projections(id text PRIMARY KEY,version bigint,amount int);",
  );

  reset(99);
  let writer = await session("es2-unguarded-writer");
  let repair = await session("es2-row-lock-repair");
  assert.deepEqual(
    await writer.command(
      "BEGIN; SELECT version||'|'||amount FROM projections WHERE id='A';",
    ),
    ["1|99"],
  );
  await repair.command(
    "BEGIN; SELECT id FROM streams WHERE id='A' FOR UPDATE; UPDATE projections SET amount=10 WHERE id='A'; COMMIT;",
  );
  assert.deepEqual(
    await writer.command(
      "UPDATE streams SET version=2 WHERE id='A' AND version=1 RETURNING version;",
    ),
    ["2"],
  );
  await writer.command(
    "INSERT INTO facts VALUES ('A',2,5); UPDATE projections SET amount=104,version=2 WHERE id='A' AND version=1; COMMIT;",
  );
  assert.equal(
    control(
      "SELECT amount||'|'||(SELECT sum(delta) FROM facts)||'|'||version FROM projections;",
    ),
    "104|15|2",
  );
  console.log(
    "PASS: repair-only row locking + unchanged version permits stale writer overwrite (104 persisted; history derives 15).",
  );

  reset(10);
  writer = await session("es2-earlier-writer");
  repair = await session("es2-waiting-repair");
  await writer.command("BEGIN; " + acquire("write"));
  assert.deepEqual(
    await writer.command(
      "SELECT version||'|'||amount FROM projections WHERE id='A';",
    ),
    ["1|10"],
  );
  let marker = repair.mark("BEGIN; " + acquire("rebuild"));
  await waiting("es2-waiting-repair");
  assert.deepEqual(await append(writer), ["2"]);
  await repair.until(marker);
  assert.deepEqual(
    await repair.command("SELECT version FROM streams WHERE id='A';"),
    ["2"],
  );
  await repair.command(
    "UPDATE projections SET amount=(SELECT sum(delta) FROM facts),version=2 WHERE id='A'; COMMIT;",
  );
  assert.equal(control("SELECT amount FROM projections;"), "15");
  console.log(
    "PASS: exclusive rebuild waits for an already-loaded shared writer and captures committed head 2.",
  );

  reset(99);
  repair = await session("es2-active-repair");
  writer = await session("es2-later-writer");
  await repair.command("BEGIN; " + acquire("rebuild"));
  await repair.command("UPDATE projections SET amount=10 WHERE id='A';");
  marker = writer.mark("BEGIN; " + acquire("write"));
  await waiting("es2-later-writer");
  assert.equal(
    control(
      "SELECT pg_try_advisory_xact_lock_shared(hashtextextended('B',0));",
    ),
    "t",
  );
  await repair.command("COMMIT;");
  await writer.until(marker);
  assert.deepEqual(
    await writer.command("SELECT amount FROM projections WHERE id='A';"),
    ["10"],
  );
  await writer.command("ROLLBACK;");
  console.log(
    "PASS: later shared writer loads repaired amount 10 after commit; distinct key can proceed.",
  );

  reset(10);
  const first = await session("es2-shared-first");
  const second = await session("es2-shared-second");
  await first.command("BEGIN; " + acquire("write"));
  await second.command("BEGIN; " + acquire("write"));
  assert.deepEqual(
    await first.command("SELECT version FROM streams WHERE id='A';"),
    ["1"],
  );
  assert.deepEqual(
    await second.command("SELECT version FROM streams WHERE id='A';"),
    ["1"],
  );
  assert.deepEqual(await append(first), ["2"]);
  assert.deepEqual(await append(second), []);
  await second.command("ROLLBACK;");
  console.log(
    "PASS: shared writers overlap; the second native version predicate affects zero rows.",
  );
  console.log(
    "Four PostgreSQL coordination diagnostics passed. These are native SQL design evidence, not library integration proofs.",
  );
} finally {
  await Promise.allSettled(sessions.map((current) => current.close()));
  spawnSync("podman", ["rm", "-f", container], {
    stdio: "ignore",
    timeout: 15_000,
  });
}
