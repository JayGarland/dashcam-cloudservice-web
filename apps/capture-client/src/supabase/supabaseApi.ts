import type { CaptureSession, FrameHashRecord } from "../models";

export interface SupabaseApi {
  insertSession(session: CaptureSession): Promise<void>;
  insertFrameHashes(records: FrameHashRecord[]): Promise<void>;
}

export interface SupabaseConfig {
  url?: string;
  anonKey?: string;
}

export class FetchSupabaseApi implements SupabaseApi {
  private readonly url: string | undefined;
  private readonly anonKey: string | undefined;

  constructor(config: SupabaseConfig) {
    this.url = config.url;
    this.anonKey = config.anonKey;
  }

  async insertSession(_session: CaptureSession): Promise<void> {
    this.ensureConfigured();
    throw new Error("FetchSupabaseApi.insertSession is not implemented.");
  }

  async insertFrameHashes(_records: FrameHashRecord[]): Promise<void> {
    this.ensureConfigured();
    throw new Error("FetchSupabaseApi.insertFrameHashes is not implemented.");
  }

  private ensureConfigured(): void {
    if (!this.url || !this.anonKey) {
      throw new Error(
        "FetchSupabaseApi is not configured. Provide url and anonKey."
      );
    }
  }
}
