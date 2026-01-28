const MIME_CANDIDATES = [
  "video/webm;codecs=vp9",
  "video/webm;codecs=vp8",
  "video/webm",
];

export function pickSupportedMimeType(): string | undefined {
  if (typeof MediaRecorder === "undefined") {
    return undefined;
  }
  if (typeof MediaRecorder.isTypeSupported !== "function") {
    return undefined;
  }
  for (const candidate of MIME_CANDIDATES) {
    if (MediaRecorder.isTypeSupported(candidate)) {
      return candidate;
    }
  }
  return undefined;
}

export class LocalRecorder {
  private readonly stream: MediaStream;
  private recorder: MediaRecorder | null = null;
  private chunks: BlobPart[] = [];
  private stopPromise: Promise<Blob> | null = null;
  private resolveStop: ((blob: Blob) => void) | null = null;
  private rejectStop: ((error: Error) => void) | null = null;
  private mimeType: string | undefined;

  constructor(stream: MediaStream) {
    this.stream = stream;
  }

  get isRecording(): boolean {
    return this.recorder?.state === "recording";
  }

  get activeMimeType(): string | undefined {
    return this.mimeType;
  }

  start(): void {
    if (typeof MediaRecorder === "undefined") {
      throw new Error("MediaRecorder is not supported in this browser.");
    }
    if (this.isRecording) {
      return;
    }

    this.chunks = [];
    this.stopPromise = null;
    this.resolveStop = null;
    this.rejectStop = null;

    const preferredMimeType = pickSupportedMimeType();
    const recorder = preferredMimeType
      ? new MediaRecorder(this.stream, { mimeType: preferredMimeType })
      : new MediaRecorder(this.stream);

    this.recorder = recorder;
    this.mimeType = recorder.mimeType || preferredMimeType;

    recorder.addEventListener("dataavailable", (event) => {
      if (event.data && event.data.size > 0) {
        this.chunks.push(event.data);
      }
    });

    recorder.addEventListener("stop", () => {
      const blobType = this.mimeType ?? recorder.mimeType;
      const blob = new Blob(this.chunks, { type: blobType });
      this.resolveStop?.(blob);
    });

    recorder.addEventListener("error", (event) => {
      const error =
        event instanceof ErrorEvent
          ? event.error ?? new Error(event.message)
          : new Error("MediaRecorder error.");
      this.rejectStop?.(error);
    });

    recorder.start();
  }

  stop(): Promise<Blob> {
    if (!this.recorder) {
      return Promise.reject(new Error("Recorder is not initialized."));
    }
    if (!this.stopPromise) {
      this.stopPromise = new Promise<Blob>((resolve, reject) => {
        this.resolveStop = resolve;
        this.rejectStop = reject;
      });
    }

    if (this.recorder.state === "recording") {
      this.recorder.stop();
    } else {
      const blobType = this.mimeType ?? this.recorder.mimeType;
      const blob = new Blob(this.chunks, { type: blobType });
      this.resolveStop?.(blob);
    }

    return this.stopPromise;
  }
}
