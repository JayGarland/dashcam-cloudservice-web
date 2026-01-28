import { DEFAULT_ALGO_VERSION, DEFAULT_INTERVAL_MS } from "../constants";
import { BrowserCameraFrameSource } from "../capture/frameSource";
import { LocalRecorder } from "../capture/localRecorder";
import { Sampler } from "../capture/sampler";
import { InMemoryHashQueue } from "../storage/hashQueue";
import type { HashQueue } from "../storage/hashQueue";
import type { CaptureSession, FrameHashRecord } from "../models";
import {
  FetchSupabaseApi,
  configureSupabaseAuth,
  getAccessToken,
  getSession,
  signIn,
  signOut,
} from "../supabase/supabaseApi";
import type { SupabaseApi } from "../supabase/supabaseApi";
import { Uploader } from "../supabase/uploader";

interface QueueHooks {
  onEnqueue?: (sessionId: string) => void;
  onUploaded?: (sessionId: string, sampleIndex: number) => void;
  triggerUpload?: () => void;
}

class UploadingQueue implements HashQueue {
  private readonly inner: HashQueue;
  private readonly hooks: QueueHooks;

  constructor(inner: HashQueue, hooks: QueueHooks) {
    this.inner = inner;
    this.hooks = hooks;
  }

  async enqueue(record: Parameters<HashQueue["enqueue"]>[0]): Promise<void> {
    await this.inner.enqueue(record);
    this.hooks.onEnqueue?.(record.sessionId);
    this.hooks.triggerUpload?.();
  }

  getOldestPending(limit: number): Promise<FrameHashRecord[]> {
    return this.inner.getOldestPending(limit);
  }

  async markUploaded(sessionId: string, sampleIndex: number): Promise<void> {
    await this.inner.markUploaded(sessionId, sampleIndex);
    this.hooks.onUploaded?.(sessionId, sampleIndex);
  }

  countPending(sessionId?: string): Promise<number> | undefined {
    return this.inner.countPending?.(sessionId);
  }
}

function formatTime(): string {
  const now = new Date();
  return now.toLocaleTimeString();
}

function createSessionId(): string {
  if (crypto.randomUUID) {
    return crypto.randomUUID();
  }
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) =>
    byte.toString(16).padStart(2, "0")
  );
  return [
    hex.slice(0, 4).join(""),
    hex.slice(4, 6).join(""),
    hex.slice(6, 8).join(""),
    hex.slice(8, 10).join(""),
    hex.slice(10, 16).join(""),
  ].join("-");
}

export function initCapturePage(root: HTMLElement): void {
  root.innerHTML = `
    <div class="page">
      <header>
        <h1>Dashcam Capture Client</h1>
        <p>Slice 5A prototype: live camera sampling + hash upload.</p>
        <div class="badge" id="online-indicator">Checking network...</div>
      </header>

      <div class="layout">
        <section class="panel">
          <h2>Session Setup</h2>
          <div class="field">
            <label for="supabase-url">Supabase URL</label>
            <input id="supabase-url" type="text" placeholder="https://xxxx.supabase.co" />
          </div>
          <div class="field">
            <label for="supabase-key">Supabase anon key</label>
            <input id="supabase-key" type="password" placeholder="anon key" />
          </div>
          <div class="field">
            <label for="auth-email">Login email</label>
            <input id="auth-email" type="email" placeholder="user@example.com" />
          </div>
          <div class="field">
            <label for="auth-password">Login password</label>
            <input id="auth-password" type="password" placeholder="password" />
          </div>
          <div class="controls">
            <button id="auth-signin">Sign In</button>
            <button id="auth-signout" class="secondary">Sign Out</button>
          </div>
          <div class="helper" id="auth-status">Not signed in</div>
          <div class="field">
            <label for="sample-interval">Sampling interval (ms)</label>
            <input id="sample-interval" type="number" min="100" step="50" value="500" />
          </div>
          <div class="field">
            <label>
              <input id="record-local" type="checkbox" />
              Record local video (optional)
            </label>
            <div class="helper" id="recording-warning"></div>
          </div>
          <div class="controls">
            <button id="start-camera">Start Camera</button>
            <button id="start-session">Start Session</button>
            <button id="stop-session" class="secondary">Stop Session</button>
          </div>
        </section>

        <section class="panel">
          <h2>Preview</h2>
          <video id="camera-preview" autoplay muted playsinline></video>
        </section>
      </div>

      <section class="panel">
        <h2>Session Status</h2>
        <div class="status-grid">
          <div class="status-card">
            <span>Session ID</span>
            <strong id="session-id">-</strong>
          </div>
          <div class="status-card">
            <span>Pending hashes</span>
            <strong id="pending-count">0</strong>
          </div>
          <div class="status-card">
            <span>Uploaded hashes</span>
            <strong id="uploaded-count">0</strong>
          </div>
          <div class="status-card">
            <span>Last uploaded index</span>
            <strong id="last-uploaded">-</strong>
          </div>
          <div class="status-card">
            <span>Recording</span>
            <strong id="recording-status">Off</strong>
          </div>
        </div>
        <div class="field">
          <label>Local recording</label>
          <div class="controls">
            <a id="recording-download" class="badge" href="#" download hidden>
              Download video
            </a>
          </div>
        </div>
      </section>

      <section class="panel">
        <h2>Activity Log</h2>
        <div id="log" class="log"></div>
      </section>
    </div>
  `;

  const onlineIndicator = root.querySelector<HTMLDivElement>(
    "#online-indicator"
  );
  const supabaseUrlInput = root.querySelector<HTMLInputElement>(
    "#supabase-url"
  );
  const supabaseKeyInput = root.querySelector<HTMLInputElement>(
    "#supabase-key"
  );
  const authEmailInput = root.querySelector<HTMLInputElement>("#auth-email");
  const authPasswordInput = root.querySelector<HTMLInputElement>(
    "#auth-password"
  );
  const authSignInButton = root.querySelector<HTMLButtonElement>(
    "#auth-signin"
  );
  const authSignOutButton = root.querySelector<HTMLButtonElement>(
    "#auth-signout"
  );
  const authStatusLabel = root.querySelector<HTMLDivElement>("#auth-status");
  const sampleIntervalInput = root.querySelector<HTMLInputElement>(
    "#sample-interval"
  );
  const recordLocalToggle = root.querySelector<HTMLInputElement>(
    "#record-local"
  );
  const recordingWarning = root.querySelector<HTMLDivElement>(
    "#recording-warning"
  );
  const startCameraButton = root.querySelector<HTMLButtonElement>(
    "#start-camera"
  );
  const startSessionButton = root.querySelector<HTMLButtonElement>(
    "#start-session"
  );
  const stopSessionButton = root.querySelector<HTMLButtonElement>(
    "#stop-session"
  );
  const sessionIdLabel = root.querySelector<HTMLElement>("#session-id");
  const pendingCountLabel = root.querySelector<HTMLElement>(
    "#pending-count"
  );
  const uploadedCountLabel = root.querySelector<HTMLElement>(
    "#uploaded-count"
  );
  const lastUploadedLabel = root.querySelector<HTMLElement>(
    "#last-uploaded"
  );
  const recordingStatusLabel = root.querySelector<HTMLElement>(
    "#recording-status"
  );
  const recordingDownloadLink = root.querySelector<HTMLAnchorElement>(
    "#recording-download"
  );
  const logEl = root.querySelector<HTMLDivElement>("#log");
  const preview = root.querySelector<HTMLVideoElement>("#camera-preview");

  if (
    !onlineIndicator ||
    !supabaseUrlInput ||
    !supabaseKeyInput ||
    !authEmailInput ||
    !authPasswordInput ||
    !authSignInButton ||
    !authSignOutButton ||
    !authStatusLabel ||
    !sampleIntervalInput ||
    !recordLocalToggle ||
    !recordingWarning ||
    !startCameraButton ||
    !startSessionButton ||
    !stopSessionButton ||
    !sessionIdLabel ||
    !pendingCountLabel ||
    !uploadedCountLabel ||
    !lastUploadedLabel ||
    !recordingStatusLabel ||
    !recordingDownloadLink ||
    !logEl ||
    !preview
  ) {
    throw new Error("Capture UI is missing required elements.");
  }

  const LOCAL_STORAGE_KEYS = {
    url: "capture-client.supabase.url",
    key: "capture-client.supabase.key",
    authEmail: "capture-client.supabase.auth.email",
    interval: "capture-client.sampling.interval",
  };

  supabaseUrlInput.value =
    localStorage.getItem(LOCAL_STORAGE_KEYS.url) ?? "";
  supabaseKeyInput.value =
    localStorage.getItem(LOCAL_STORAGE_KEYS.key) ?? "";
  authEmailInput.value =
    localStorage.getItem(LOCAL_STORAGE_KEYS.authEmail) ?? "";
  sampleIntervalInput.value =
    localStorage.getItem(LOCAL_STORAGE_KEYS.interval) ??
    String(DEFAULT_INTERVAL_MS);

  configureSupabaseAuth({
    url: supabaseUrlInput.value.trim(),
    anonKey: supabaseKeyInput.value.trim(),
  });

  let camera: BrowserCameraFrameSource | null = null;
  let sampler: Sampler | null = null;
  let activeSession: CaptureSession | null = null;
  let uploadedCount = 0;
  let lastUploadedIndex: number | null = null;
  let pendingCount = 0;
  let isLoggedIn = Boolean(getSession());
  let localRecorder: LocalRecorder | null = null;
  let recordingActive = false;
  let recordingDownloadUrl: string | null = null;

  const baseQueue = new InMemoryHashQueue();
  let uploader: Uploader | null = null;
  let supabaseApi: FetchSupabaseApi | null = null;

  const apiProxy: SupabaseApi = {
    async insertSession(session) {
      if (!supabaseApi) {
        throw new Error("Supabase is not configured.");
      }
      return supabaseApi.insertSession(session);
    },
    async insertFrameHashes(records) {
      if (!supabaseApi) {
        throw new Error("Supabase is not configured.");
      }
      return supabaseApi.insertFrameHashes(records);
    },
  };

  const queue = new UploadingQueue(baseQueue, {
    onEnqueue: () => {
      void refreshPendingCount();
    },
    onUploaded: (sessionId, sampleIndex) => {
      if (activeSession && sessionId === activeSession.sessionId) {
        uploadedCount += 1;
        if (lastUploadedIndex === null || sampleIndex > lastUploadedIndex) {
          lastUploadedIndex = sampleIndex;
        }
        log(`Uploaded sample #${sampleIndex}.`);
        updateStatus();
      }
      void refreshPendingCount();
    },
    triggerUpload: () => {
      void uploader?.uploadPending();
    },
  });

  uploader = new Uploader(queue, apiProxy);
  uploader.attachOnlineListener();

  function log(message: string): void {
    const line = `[${formatTime()}] ${message}`;
    console.log(line);
    logEl.textContent = [line, logEl.textContent].filter(Boolean).join("\n");
  }

  function updateOnlineIndicator(): void {
    const online = navigator.onLine;
    onlineIndicator.textContent = online ? "Online" : "Offline";
    onlineIndicator.classList.toggle("online", online);
    onlineIndicator.classList.toggle("offline", !online);
  }

  function updateStatus(): void {
    sessionIdLabel.textContent = activeSession?.sessionId ?? "-";
    pendingCountLabel.textContent = String(pendingCount);
    uploadedCountLabel.textContent = String(uploadedCount);
    lastUploadedLabel.textContent =
      lastUploadedIndex !== null ? String(lastUploadedIndex) : "-";
    recordingStatusLabel.textContent = recordingActive ? "Active" : "Off";
    startCameraButton.disabled = Boolean(camera);
    startSessionButton.disabled = !camera || Boolean(sampler) || !isLoggedIn;
    stopSessionButton.disabled = !sampler;
    recordLocalToggle.disabled = Boolean(sampler);
  }

  function updateAuthStatus(): void {
    const session = getSession();
    isLoggedIn = Boolean(session);
    const email = session?.user?.email ?? session?.user?.id ?? "user";
    authStatusLabel.textContent = isLoggedIn
      ? `Signed in as ${email}.`
      : "Not signed in.";
    authSignInButton.disabled = isLoggedIn;
    authSignOutButton.disabled = !isLoggedIn;
    updateStatus();
  }

  async function refreshPendingCount(): Promise<void> {
    pendingCount = (await queue.countPending?.()) ?? pendingCount;
    updateStatus();
  }

  function setRecordingWarning(message: string): void {
    recordingWarning.textContent = message;
    recordingWarning.style.display = message ? "block" : "none";
  }

  function clearRecordingDownload(): void {
    if (recordingDownloadUrl) {
      URL.revokeObjectURL(recordingDownloadUrl);
      recordingDownloadUrl = null;
    }
    recordingDownloadLink.hidden = true;
    recordingDownloadLink.removeAttribute("href");
    recordingDownloadLink.removeAttribute("download");
  }

  function extensionFromMimeType(mimeType: string): string {
    if (!mimeType) {
      return "webm";
    }
    const [type] = mimeType.split(";");
    const parts = type.split("/");
    if (parts.length === 2 && parts[1]) {
      return parts[1];
    }
    return "webm";
  }

  function buildSupabaseApi(): FetchSupabaseApi {
    const url = supabaseUrlInput.value.trim();
    const anonKey = supabaseKeyInput.value.trim();

    localStorage.setItem(LOCAL_STORAGE_KEYS.url, url);
    localStorage.setItem(LOCAL_STORAGE_KEYS.key, anonKey);
    localStorage.setItem(LOCAL_STORAGE_KEYS.authEmail, authEmailInput.value.trim());

    if (!url || !anonKey) {
      throw new Error("Supabase URL and anon key are required.");
    }
    configureSupabaseAuth({ url, anonKey });
    return new FetchSupabaseApi({
      url,
      anonKey,
      getAccessToken,
    });
  }

  function syncSupabaseConfig(): void {
    const url = supabaseUrlInput.value.trim();
    const anonKey = supabaseKeyInput.value.trim();
    localStorage.setItem(LOCAL_STORAGE_KEYS.url, url);
    localStorage.setItem(LOCAL_STORAGE_KEYS.key, anonKey);
    configureSupabaseAuth({ url, anonKey });
    updateAuthStatus();
  }

  supabaseUrlInput.addEventListener("change", syncSupabaseConfig);
  supabaseKeyInput.addEventListener("change", syncSupabaseConfig);

  authSignInButton.addEventListener("click", async () => {
    syncSupabaseConfig();
    const email = authEmailInput.value.trim();
    const password = authPasswordInput.value;
    if (!email || !password) {
      log("Email and password are required to sign in.");
      return;
    }
    try {
      await signIn(email, password);
      localStorage.setItem(LOCAL_STORAGE_KEYS.authEmail, email);
      log(`Signed in as ${email}.`);
    } catch (error) {
      log(`Sign-in failed: ${String(error)}`);
    }
    updateAuthStatus();
  });

  authSignOutButton.addEventListener("click", async () => {
    try {
      await signOut();
      log("Signed out.");
    } catch (error) {
      log(`Sign-out failed: ${String(error)}`);
    }
    updateAuthStatus();
  });

  startCameraButton.addEventListener("click", async () => {
    if (camera) {
      return;
    }
    try {
      camera = new BrowserCameraFrameSource({
        videoElement: preview,
        facingMode: "environment",
      });
      await camera.start();
      log("Camera started; preview should be visible.");
      updateStatus();
    } catch (error) {
      log(`Camera start failed: ${String(error)}`);
      camera = null;
      updateStatus();
    }
  });

  startSessionButton.addEventListener("click", async () => {
    if (!camera) {
      log("Start the camera before starting a session.");
      return;
    }
    if (sampler) {
      log("Session already running.");
      return;
    }
    if (!getSession()) {
      log("Sign in before starting a session.");
      updateAuthStatus();
      return;
    }

    recordingActive = false;
    localRecorder = null;
    clearRecordingDownload();
    setRecordingWarning("");

    const intervalMs = Math.max(
      100,
      Number.parseInt(sampleIntervalInput.value, 10) || DEFAULT_INTERVAL_MS
    );
    sampleIntervalInput.value = String(intervalMs);
    localStorage.setItem(LOCAL_STORAGE_KEYS.interval, String(intervalMs));

    activeSession = {
      sessionId: createSessionId(),
      deviceClockStartEpochMs: Date.now(),
      samplingIntervalMs: intervalMs,
      algoVersion: DEFAULT_ALGO_VERSION,
    };
    uploadedCount = 0;
    lastUploadedIndex = null;

    supabaseApi = null;
    try {
      supabaseApi = buildSupabaseApi();
      await apiProxy.insertSession(activeSession);
      log(`insertSession ok: ${activeSession.sessionId}`);
    } catch (error) {
      log(`insertSession failed: ${String(error)}`);
    }

    sampler = new Sampler(
      activeSession,
      { samplingIntervalMs: intervalMs },
      {
        frameSource: camera,
        queue,
        now: Date.now,
      }
    );
    sampler.start();
    log(`Sampler started (interval ${intervalMs}ms).`);

    if (recordLocalToggle.checked) {
      const stream = camera.getVideoElement().srcObject;
      if (!(stream instanceof MediaStream)) {
        log("Recording could not start: camera stream unavailable.");
        setRecordingWarning("Recording unavailable: camera stream missing.");
      } else {
        try {
          localRecorder = new LocalRecorder(stream);
          localRecorder.start();
          recordingActive = true;
          log("Local recording started.");
        } catch (error) {
          log(`Recording start failed: ${String(error)}`);
          setRecordingWarning(
            "Recording failed to start. Session will continue without video."
          );
          localRecorder = null;
          recordingActive = false;
        }
      }
    }

    updateStatus();
    void refreshPendingCount();
  });

  stopSessionButton.addEventListener("click", async () => {
    if (!sampler) {
      return;
    }
    sampler.stop();
    sampler = null;
    log("Sampler stopped; flushing pending uploads.");
    void uploader?.uploadPending();
    if (recordingActive && localRecorder) {
      recordingActive = false;
      updateStatus();
      try {
        const blob = await localRecorder.stop();
        const mimeType = blob.type || localRecorder.activeMimeType || "";
        const ext = extensionFromMimeType(mimeType);
        const filename = `session_${activeSession?.sessionId ?? "unknown"}.${ext}`;
        recordingDownloadUrl = URL.createObjectURL(blob);
        recordingDownloadLink.href = recordingDownloadUrl;
        recordingDownloadLink.download = filename;
        recordingDownloadLink.textContent = `Download video (${ext})`;
        recordingDownloadLink.hidden = false;
        log("Local recording ready for download.");
      } catch (error) {
        log(`Recording stop failed: ${String(error)}`);
      } finally {
        localRecorder = null;
      }
    }
    updateStatus();
  });

  window.addEventListener("online", () => {
    updateOnlineIndicator();
    log("Network online; attempting to flush uploads.");
  });
  window.addEventListener("offline", () => {
    updateOnlineIndicator();
    log("Network offline; uploads will buffer.");
  });

  setRecordingWarning("");
  updateOnlineIndicator();
  updateAuthStatus();
  updateStatus();
  void refreshPendingCount();
}
