export type AlgoVersion = "dhash64_v1";

export interface CaptureSession {
  sessionId: string;
  deviceClockStartEpochMs: number;
  samplingIntervalMs: number;
  algoVersion: AlgoVersion;
  clientVersion?: string;
}

export interface FrameHashRecord {
  sessionId: string;
  sampleIndex: number;
  elapsedMs: number;
  sampleTimestampEpochMs: number;
  hashHex: string;
  intervalMs: number;
  algoVersion: AlgoVersion;
  createdAtEpochMs: number;
  uploadState: "pending" | "uploaded";
  mediaTimeSec?: number;
  elapsedSource?: "rvfc" | "currentTime";
}