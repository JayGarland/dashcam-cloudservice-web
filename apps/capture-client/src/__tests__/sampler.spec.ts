import { describe, expect, it, vi } from "vitest";
import type { CaptureSession } from "../models";
import { Sampler } from "../capture/sampler";
import { InMemoryHashQueue } from "../storage/hashQueue";
import type { FrameData, FrameSource } from "../capture/frameSource";
import { makeSolidColorFrameSource } from "../capture/frameSource";

function makeSession(): CaptureSession {
  return {
    sessionId: "session-1",
    deviceClockStartEpochMs: 1_000_000,
    samplingIntervalMs: 500,
    algoVersion: "dhash64_v1",
  };
}

describe("Sampler", () => {
  it("generates FrameHashRecord with correct timing fields", async () => {
    vi.useFakeTimers();
    const session = makeSession();
    const queue = new InMemoryHashQueue();
    const frameSource = makeSolidColorFrameSource(4, 4, [10, 20, 30, 255]);
    let nowValue = 2_000_000;
    const sampler = new Sampler(
      session,
      { samplingIntervalMs: 500 },
      {
        frameSource,
        queue,
        now: () => {
          nowValue += 10;
          return nowValue;
        },
      }
    );

    sampler.start();
    await vi.advanceTimersByTimeAsync(1500);
    sampler.stop();

    const records = await queue.getOldestPending(10);
    expect(records).toHaveLength(3);

    records.forEach((record, index) => {
      expect(record.sampleIndex).toBe(index);
      expect(record.elapsedMs).toBe(index * 500);
      expect(record.sampleTimestampEpochMs).toBe(
        session.deviceClockStartEpochMs + index * 500
      );
      expect(record.intervalMs).toBe(session.samplingIntervalMs);
      expect(record.algoVersion).toBe("dhash64_v1");
      expect(record.hashHex).toMatch(/^[0-9a-f]{16}$/);
    });

    vi.useRealTimers();
  });

  it("skips tick when FrameSource throws and continues", async () => {
    vi.useFakeTimers();
    const session = makeSession();
    const queue = new InMemoryHashQueue();
    const baseFrameSource = makeSolidColorFrameSource(2, 2, [0, 0, 0, 255]);
    const baseFrame = await baseFrameSource.readFrame();

    class FlakyFrameSource implements FrameSource {
      private calls = 0;

      async readFrame(): Promise<FrameData> {
        this.calls += 1;
        if (this.calls === 1) {
          throw new Error("boom");
        }
        return baseFrame;
      }
    }

    const sampler = new Sampler(
      session,
      { samplingIntervalMs: 500 },
      {
        frameSource: new FlakyFrameSource(),
        queue,
        now: () => 2_000_000,
      }
    );

    sampler.start();
    await vi.advanceTimersByTimeAsync(1500);
    sampler.stop();

    const records = await queue.getOldestPending(10);
    expect(records).toHaveLength(2);
    expect(records[0].sampleIndex).toBe(0);
    expect(records[1].sampleIndex).toBe(1);

    vi.useRealTimers();
  });
});
