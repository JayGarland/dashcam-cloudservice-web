import type { CaptureSession, FrameHashRecord } from "../models";

export interface SupabaseApi {
  insertSession(session: CaptureSession): Promise<void>;
  insertFrameHashes(records: FrameHashRecord[]): Promise<void>;
}

export interface SupabaseConfig {
  url?: string;
  anonKey?: string;
  accessToken?: string;
  getAccessToken?: () => Promise<string | undefined>;
}

export class FetchSupabaseApi implements SupabaseApi {
  private readonly url: string | undefined;
  private readonly anonKey: string | undefined;
  private readonly accessToken: string | undefined;
  private readonly getAccessToken: (() => Promise<string | undefined>) | undefined;

  constructor(config: SupabaseConfig) {
    this.url = config.url?.replace(/\/$/, "");
    this.anonKey = config.anonKey;
    this.accessToken = config.accessToken;
    this.getAccessToken = config.getAccessToken;
  }

  async insertSession(session: CaptureSession): Promise<void> {
    this.ensureConfigured();
    await this.post("capture_sessions", {
      session_id: session.sessionId,
      device_clock_start_epoch_ms: session.deviceClockStartEpochMs,
      sampling_interval_ms: session.samplingIntervalMs,
      algo_version: session.algoVersion,
      client_version: session.clientVersion ?? null,
    });
  }

  async insertFrameHashes(records: FrameHashRecord[]): Promise<void> {
    this.ensureConfigured();
    if (records.length === 0) {
      return;
    }
    await this.post(
      "frame_hashes",
      records.map((record) => ({
        session_id: record.sessionId,
        sample_index: record.sampleIndex,
        elapsed_ms: record.elapsedMs,
        sample_timestamp_epoch_ms: record.sampleTimestampEpochMs,
        hash_hex: record.hashHex,
        interval_ms: record.intervalMs,
        algo_version: record.algoVersion,
        created_at: new Date(record.createdAtEpochMs).toISOString(),
      }))
    );
  }

  private ensureConfigured(): void {
    if (!this.url || !this.anonKey) {
      throw new Error(
        "FetchSupabaseApi is not configured. Provide url and anonKey."
      );
    }
  }

  private async post(path: string, payload: unknown): Promise<void> {
    if (!this.url || !this.anonKey) {
      return;
    }
    const token =
      (await this.getAccessToken?.()) ?? this.accessToken ?? this.anonKey;

    const response = await fetch(`${this.url}/rest/v1/${path}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        apikey: this.anonKey,
        Authorization: `Bearer ${token}`,
        Prefer: "return=minimal",
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const message = await response.text();
      throw new Error(
        `Supabase insert failed (${response.status}): ${message || response.statusText}`
      );
    }
  }
}
