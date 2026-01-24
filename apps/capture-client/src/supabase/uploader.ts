import type { HashQueue } from "../storage/hashQueue";
import type { SupabaseApi } from "./supabaseApi";
import { DEFAULT_BATCH_SIZE } from "../constants";

export interface UploaderConfig {
  batchSize: number;
}

export class Uploader {
  private readonly queue: HashQueue;
  private readonly api: SupabaseApi;
  private readonly batchSize: number;
  private readonly maxIterations: number;

  constructor(
    queue: HashQueue,
    api: SupabaseApi,
    config?: Partial<UploaderConfig>
  ) {
    this.queue = queue;
    this.api = api;
    this.batchSize = config?.batchSize ?? DEFAULT_BATCH_SIZE;
    this.maxIterations = 100;
  }

  async uploadPending(): Promise<void> {
    for (let iteration = 0; iteration < this.maxIterations; iteration += 1) {
      const batch = await this.queue.getOldestPending(this.batchSize);
      if (batch.length === 0) {
        return;
      }
      try {
        await this.api.insertFrameHashes(batch);
      } catch {
        return;
      }
      for (const record of batch) {
        await this.queue.markUploaded(record.sessionId, record.sampleIndex);
      }
    }
  }

  attachOnlineListener(): void {
    if (typeof window === "undefined") {
      return;
    }
    window.addEventListener("online", () => {
      void this.uploadPending();
    });
  }
}
