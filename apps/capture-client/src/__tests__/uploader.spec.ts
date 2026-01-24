import { describe, expect, it } from "vitest";
import { InMemoryHashQueue } from "../storage/hashQueue";
import type { CaptureSession, FrameHashRecord } from "../models";
import type { SupabaseApi } from "../supabase/supabaseApi";
import { Uploader } from "../supabase/uploader";

class MockSupabaseApi implements SupabaseApi {
  public readonly batches: FrameHashRecord[][] = [];
  public shouldThrow = false;

  async insertSession(_session: CaptureSession): Promise<void> {
    return;
  }

  async insertFrameHashes(records: FrameHashRecord[]): Promise<void> {
    if (this.shouldThrow) {
      throw new Error("upload failed");
    }
    this.batches.push(records);
  }
}

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

describe("Uploader", () => {
  it("uploads in batches and marks uploaded on success", async () => {
    const queue = new InMemoryHashQueue();
    for (let i = 0; i < 5; i += 1) {
      await queue.enqueue(
        makeRecord({
          sampleIndex: i,
          createdAtEpochMs: i + 1,
        })
      );
    }

    const api = new MockSupabaseApi();
    const uploader = new Uploader(queue, api, { batchSize: 2 });

    await uploader.uploadPending();

    expect(api.batches).toHaveLength(3);
    expect(api.batches.map((batch) => batch.length)).toEqual([2, 2, 1]);
    const flattened = api.batches.flat().map((record) => record.sampleIndex);
    expect(flattened).toEqual([0, 1, 2, 3, 4]);

    const pendingCount = await queue.countPending?.();
    expect(pendingCount).toBe(0);
  });

  it("stops and preserves pending on failure", async () => {
    const queue = new InMemoryHashQueue();
    for (let i = 0; i < 3; i += 1) {
      await queue.enqueue(
        makeRecord({
          sampleIndex: i,
          createdAtEpochMs: i + 1,
        })
      );
    }

    const api = new MockSupabaseApi();
    api.shouldThrow = true;
    const uploader = new Uploader(queue, api, { batchSize: 2 });

    await uploader.uploadPending();

    const pending = await queue.getOldestPending(10);
    expect(pending).toHaveLength(3);
    expect(api.batches).toHaveLength(0);
  });
});
