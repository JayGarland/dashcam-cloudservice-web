import type { CaptureSession, FrameHashRecord } from "../models";
import type { FrameSource } from "./frameSource";
import type { HashQueue } from "../storage/hashQueue";
import { dhash64HexFromRgba } from "../hash/dhash64";
import { DEFAULT_INTERVAL_MS } from "../constants";

export interface SamplerConfig {
  samplingIntervalMs: number;
}

export interface SamplerDeps {
  frameSource: FrameSource;
  queue: HashQueue;
  now: () => number;
}

export class Sampler {
  private readonly session: CaptureSession;
  private readonly intervalMs: number;
  private readonly frameSource: FrameSource;
  private readonly queue: HashQueue;
  private readonly now: () => number;
  private intervalId: ReturnType<typeof setInterval> | undefined;
  private sampleIndex = 0;
  private inFlight = false;
  private baseMediaTimeSec: number | undefined;

  constructor(session: CaptureSession, config: SamplerConfig, deps: SamplerDeps) {
    this.session = session;
    const configInterval = config.samplingIntervalMs ?? DEFAULT_INTERVAL_MS;
    const sessionInterval = session.samplingIntervalMs;
    if (configInterval !== sessionInterval) {
      throw new Error(
        `Sampler interval mismatch: config=${configInterval} session=${sessionInterval}`
      );
    }
    this.intervalMs = sessionInterval;
    this.frameSource = deps.frameSource;
    this.queue = deps.queue;
    this.now = deps.now ?? Date.now;
  }

  start(): void {
    if (this.intervalId) {
      return;
    }
    this.intervalId = setInterval(() => {
      void this.handleTick();
    }, this.intervalMs);
  }

  stop(): void {
    if (!this.intervalId) {
      return;
    }
    clearInterval(this.intervalId);
    this.intervalId = undefined;
  }

  private async handleTick(): Promise<void> {
    if (this.inFlight) {
      return;
    }
    this.inFlight = true;
    try {
      const frame = await this.frameSource.readFrame();
      const hashHex = dhash64HexFromRgba(
        frame.rgba,
        frame.width,
        frame.height
      );
      if (!Number.isFinite(frame.mediaTimeSec)) {
        return;
      }
      if (this.baseMediaTimeSec === undefined) {
        this.baseMediaTimeSec = frame.mediaTimeSec;
      }
      const elapsedMs = Math.max(
        0,
        Math.round((frame.mediaTimeSec - this.baseMediaTimeSec) * 1000)
      );
      const sampleTimestampEpochMs =
        this.session.deviceClockStartEpochMs + elapsedMs;

      const record: FrameHashRecord = {
        sessionId: this.session.sessionId,
        sampleIndex: this.sampleIndex,
        elapsedMs,
        sampleTimestampEpochMs,
        hashHex,
        intervalMs: this.session.samplingIntervalMs,
        algoVersion: this.session.algoVersion,
        createdAtEpochMs: this.now(),
        uploadState: "pending",
        mediaTimeSec: frame.mediaTimeSec,
        elapsedSource: frame.elapsedSource,
      };

      await this.queue.enqueue(record);
      this.sampleIndex += 1;
    } catch {
      // Skip this tick on frame or hashing errors.
    } finally {
      this.inFlight = false;
    }
  }
}
