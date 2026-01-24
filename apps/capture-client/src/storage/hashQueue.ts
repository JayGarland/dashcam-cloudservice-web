import type { FrameHashRecord } from "../models";

export interface HashQueue {
  enqueue(record: FrameHashRecord): Promise<void>;
  getOldestPending(limit: number): Promise<FrameHashRecord[]>;
  markUploaded(sessionId: string, sampleIndex: number): Promise<void>;
  countPending?(sessionId?: string): Promise<number>;
}

export class InMemoryHashQueue implements HashQueue {
  private readonly records = new Map<string, FrameHashRecord>();

  async enqueue(record: FrameHashRecord): Promise<void> {
    const key = this.makeKey(record.sessionId, record.sampleIndex);
    this.records.set(key, { ...record });
  }

  async getOldestPending(limit: number): Promise<FrameHashRecord[]> {
    const pending = Array.from(this.records.values()).filter(
      (record) => record.uploadState === "pending"
    );

    // Deterministic ordering: createdAtEpochMs, then sessionId, then sampleIndex.
    pending.sort((a, b) => {
      if (a.createdAtEpochMs !== b.createdAtEpochMs) {
        return a.createdAtEpochMs - b.createdAtEpochMs;
      }
      if (a.sessionId !== b.sessionId) {
        return a.sessionId.localeCompare(b.sessionId);
      }
      return a.sampleIndex - b.sampleIndex;
    });

    return pending.slice(0, limit);
  }

  async markUploaded(sessionId: string, sampleIndex: number): Promise<void> {
    const key = this.makeKey(sessionId, sampleIndex);
    const record = this.records.get(key);
    if (record) {
      record.uploadState = "uploaded";
    }
  }

  async countPending(sessionId?: string): Promise<number> {
    let count = 0;
    for (const record of this.records.values()) {
      if (record.uploadState !== "pending") {
        continue;
      }
      if (sessionId && record.sessionId !== sessionId) {
        continue;
      }
      count += 1;
    }
    return count;
  }

  private makeKey(sessionId: string, sampleIndex: number): string {
    return `${sessionId}::${sampleIndex}`;
  }
}
