export interface FrameData {
  rgba: Uint8ClampedArray;
  width: number;
  height: number;
}

export interface FrameSource {
  readFrame(): Promise<FrameData>;
}

export interface BrowserCameraOptions {
  videoElement?: HTMLVideoElement;
  width?: number;
  height?: number;
  facingMode?: "user" | "environment";
  deviceId?: string;
}

export class BrowserCameraFrameSource implements FrameSource {
  private readonly video: HTMLVideoElement;
  private readonly canvas: HTMLCanvasElement;
  private readonly context: CanvasRenderingContext2D;
  private readonly constraints: MediaStreamConstraints;
  private stream: MediaStream | undefined;
  private ready: Promise<void> | null = null;
  private width = 0;
  private height = 0;

  constructor(options: BrowserCameraOptions = {}) {
    if (typeof document === "undefined") {
      throw new Error("BrowserCameraFrameSource requires a browser document.");
    }
    this.video = options.videoElement ?? document.createElement("video");
    this.video.autoplay = true;
    this.video.muted = true;
    this.video.playsInline = true;
    this.video.setAttribute("playsinline", "true");

    this.canvas = document.createElement("canvas");
    const context = this.canvas.getContext("2d", { willReadFrequently: true });
    if (!context) {
      throw new Error("Unable to acquire 2D canvas context.");
    }
    this.context = context;

    const videoConstraints: MediaTrackConstraints = {
      facingMode: options.facingMode ? { ideal: options.facingMode } : undefined,
      width: options.width ? { ideal: options.width } : undefined,
      height: options.height ? { ideal: options.height } : undefined,
      deviceId: options.deviceId ? { exact: options.deviceId } : undefined,
    };

    const hasSpecificConstraints = Object.values(videoConstraints).some(
      (value) => value !== undefined
    );

    this.constraints = {
      video: hasSpecificConstraints ? videoConstraints : true,
      audio: false,
    };
  }

  async start(): Promise<void> {
    if (this.stream) {
      return;
    }
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error("Camera access is not supported in this browser.");
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia(this.constraints);
      this.video.srcObject = this.stream;
      await this.video.play().catch(() => undefined);
      this.ready = this.waitForIntrinsicDimensions();
      await this.ready;
    } catch (error) {
      this.ready = null;
      throw error;
    }
  }

  stop(): void {
    if (!this.stream) {
      return;
    }
    for (const track of this.stream.getTracks()) {
      track.stop();
    }
    this.stream = undefined;
    this.video.srcObject = null;
    this.ready = null;
    this.width = 0;
    this.height = 0;
  }

  getVideoElement(): HTMLVideoElement {
    return this.video;
  }

  async readFrame(): Promise<FrameData> {
    if (!this.stream) {
      await this.start();
    }
    if (this.ready) {
      await this.ready;
    }
    const intrinsicWidth = this.video.videoWidth;
    const intrinsicHeight = this.video.videoHeight;
    if (!intrinsicWidth || !intrinsicHeight) {
      throw new Error("Camera video dimensions are not available yet.");
    }
    this.ensureCanvasSize(intrinsicWidth, intrinsicHeight);
    this.context.drawImage(
      this.video,
      0,
      0,
      intrinsicWidth,
      intrinsicHeight
    );
    const imageData = this.context.getImageData(
      0,
      0,
      intrinsicWidth,
      intrinsicHeight
    );
    return {
      rgba: imageData.data,
      width: imageData.width,
      height: imageData.height,
    };
  }

  getDebugFrameSizes(): {
    intrinsicWidth: number;
    intrinsicHeight: number;
    displayWidth: number;
    displayHeight: number;
    hashWidth: number;
    hashHeight: number;
  } {
    return {
      intrinsicWidth: this.video.videoWidth,
      intrinsicHeight: this.video.videoHeight,
      displayWidth: this.video.clientWidth,
      displayHeight: this.video.clientHeight,
      hashWidth: this.canvas.width,
      hashHeight: this.canvas.height,
    };
  }

  private ensureCanvasSize(width: number, height: number): void {
    if (this.width === width && this.height === height) {
      return;
    }
    this.width = width;
    this.height = height;
    this.canvas.width = width;
    this.canvas.height = height;
  }

  private waitForIntrinsicDimensions(): Promise<void> {
    const initialWidth = this.video.videoWidth;
    const initialHeight = this.video.videoHeight;
    if (initialWidth > 0 && initialHeight > 0) {
      this.ensureCanvasSize(initialWidth, initialHeight);
      return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
      let settled = false;
      let rafId = 0;
      let frameCallbackId: number | null = null;
      const videoWithCallback = this.video as HTMLVideoElement & {
        requestVideoFrameCallback?: (callback: () => void) => number;
        cancelVideoFrameCallback?: (handle: number) => void;
      };

      const cleanup = (): void => {
        this.video.removeEventListener("loadedmetadata", handleReadyCheck);
        this.video.removeEventListener("loadeddata", handleReadyCheck);
        this.video.removeEventListener("resize", handleReadyCheck);
        this.video.removeEventListener("error", handleError);
        if (rafId) {
          cancelAnimationFrame(rafId);
          rafId = 0;
        }
        if (frameCallbackId !== null && videoWithCallback.cancelVideoFrameCallback) {
          videoWithCallback.cancelVideoFrameCallback(frameCallbackId);
        }
      };

      const handleReadyCheck = (): void => {
        if (settled) {
          return;
        }
        const width = this.video.videoWidth;
        const height = this.video.videoHeight;
        if (width > 0 && height > 0) {
          settled = true;
          cleanup();
          this.ensureCanvasSize(width, height);
          resolve();
          return;
        }
        if (!rafId) {
          rafId = requestAnimationFrame(() => {
            rafId = 0;
            handleReadyCheck();
          });
        }
      };

      const handleError = (): void => {
        if (settled) {
          return;
        }
        settled = true;
        cleanup();
        reject(new Error("Camera video dimensions are not available yet."));
      };

      this.video.addEventListener("loadedmetadata", handleReadyCheck);
      this.video.addEventListener("loadeddata", handleReadyCheck);
      this.video.addEventListener("resize", handleReadyCheck);
      this.video.addEventListener("error", handleError, { once: true });

      if (videoWithCallback.requestVideoFrameCallback) {
        frameCallbackId = videoWithCallback.requestVideoFrameCallback(() => {
          handleReadyCheck();
        });
      }

      handleReadyCheck();
    });
  }
}

export function makeSolidColorFrameSource(
  width: number,
  height: number,
  rgba: [number, number, number, number]
): FrameSource {
  const [r, g, b, a] = rgba;
  const data = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < data.length; i += 4) {
    data[i] = r;
    data[i + 1] = g;
    data[i + 2] = b;
    data[i + 3] = a;
  }

  return {
    async readFrame(): Promise<FrameData> {
      return {
        rgba: data,
        width,
        height,
      };
    },
  };
}
