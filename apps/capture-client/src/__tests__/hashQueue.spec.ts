import { describe, expect, it } from "vitest";
import { InMemoryHashQueue } from "../storage/hashQueue";
import type { FrameHashRecord } from "../models";

function makeRecord(
  overrides: Partial<FrameHashRecord> = {}
): FrameHashRecord {
  return {
    sessionId: "session-a",
    sampleIndex: 0,
    elapsedMs: 0,
    sampleTimestampEpochMs: 0,
    hashHex: "0000000000000000",
    intervalMs: 500,
    algoVersion: "dhash64_v1",
    createdAtEpochMs: 0,
    uploadState: "pending",
    ...overrides,
  };
}

describe("InMemoryHashQueue", () => {
  it("enqueue then getOldestPending returns pending in deterministic order", async () => {
    const queue = new InMemoryHashQueue();
    await queue.enqueue(
      makeRecord({ sessionId: "b", sampleIndex: 0, createdAtEpochMs: 100 })
    );
    await queue.enqueue(
      makeRecord({ sessionId: "a", sampleIndex: 2, createdAtEpochMs: 100 })
    );
    await queue.enqueue(
      makeRecord({ sessionId: "a", sampleIndex: 1, createdAtEpochMs: 100 })
    );
    await queue.enqueue(
      makeRecord({ sessionId: "c", sampleIndex: 0, createdAtEpochMs: 50 })
    );

    const result = await queue.getOldestPending(10);
    const keys = result.map((record) =>
      `${record.sessionId}:${record.sampleIndex}`
    );

    expect(keys).toEqual(["c:0", "a:1", "a:2", "b:0"]);
  });

  it("markUploaded removes from pending list", async () => {
    const queue = new InMemoryHashQueue();
    await queue.enqueue(makeRecord({ sessionId: "a", sampleIndex: 0 }));
    await queue.enqueue(makeRecord({ sessionId: "a", sampleIndex: 1 }));

    await queue.markUploaded("a", 0);

    const pending = await queue.getOldestPending(10);
    expect(pending).toHaveLength(1);
    expect(pending[0].sampleIndex).toBe(1);
  });
});
