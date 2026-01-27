import { createClient, type Session, type SupabaseClient } from "@supabase/supabase-js";
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

const AUTH_SESSION_KEY = "capture-client.supabase.session";
let authClient: SupabaseClient | null = null;
let authClientKey: string | null = null;
let cachedSession: Session | null = null;

export function configureSupabaseAuth(config: SupabaseConfig): void {
  if (!config.url || !config.anonKey) {
    authClient = null;
    authClientKey = null;
    cachedSession = null;
    return;
  }
  const clientKey = `${config.url}|${config.anonKey}`;
  if (authClient && authClientKey === clientKey) {
    return;
  }
  authClient = createClient(config.url, config.anonKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
  authClientKey = clientKey;
  cachedSession = loadSession();
}

export async function signIn(
  email: string,
  password: string
): Promise<Session> {
  const client = ensureAuthClient();
  const { data, error } = await client.auth.signInWithPassword({
    email,
    password,
  });
  if (error) {
    throw error;
  }
  if (!data.session) {
    throw new Error("Supabase did not return a session.");
  }
  saveSession(data.session);
  return data.session;
}

export async function signOut(): Promise<void> {
  const client = ensureAuthClient();
  await client.auth.signOut();
  saveSession(null);
}

export function getSession(): Session | null {
  if (!cachedSession) {
    cachedSession = loadSession();
  }
  if (cachedSession?.expires_at && cachedSession.expires_at * 1000 <= Date.now()) {
    saveSession(null);
    return null;
  }
  return cachedSession;
}

export async function getAccessToken(): Promise<string | undefined> {
  return getSession()?.access_token;
}

function ensureAuthClient(): SupabaseClient {
  if (!authClient) {
    throw new Error("Supabase auth is not configured.");
  }
  return authClient;
}

function saveSession(session: Session | null): void {
  cachedSession = session;
  try {
    if (!session) {
      localStorage.removeItem(AUTH_SESSION_KEY);
      return;
    }
    localStorage.setItem(AUTH_SESSION_KEY, JSON.stringify(session));
  } catch {
    // Ignore storage failures in demo mode.
  }
}

function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(AUTH_SESSION_KEY);
    if (!raw) {
      return null;
    }
    return JSON.parse(raw) as Session;
  } catch {
    return null;
  }
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
    const token = (await this.getAccessToken?.()) ?? this.accessToken;
    if (!token) {
      throw new Error("Supabase user session is required before uploads.");
    }

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
