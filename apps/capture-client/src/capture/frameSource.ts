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
  private ready: Promise<void> | undefined;
  private resolveReady: (() => void) | undefined;
  private rejectReady: ((error: unknown) => void) | undefined;
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
    this.ready = new Promise<void>((resolve, reject) => {
      this.resolveReady = resolve;
      this.rejectReady = reject;
    });

    try {
      this.stream = await navigator.mediaDevices.getUserMedia(this.constraints);
      this.video.srcObject = this.stream;
      await this.video.play().catch(() => undefined);

      if (this.video.readyState >= 1 && this.video.videoWidth > 0) {
        this.finalizeDimensions();
      } else {
        this.video.addEventListener(
          "loadedmetadata",
          () => {
            this.finalizeDimensions();
          },
          { once: true }
        );
      }
      await this.ready;
    } catch (error) {
      this.rejectReady?.(error);
      this.ready = undefined;
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
    if (!this.width || !this.height) {
      throw new Error("Camera video dimensions are not available yet.");
    }
    this.context.drawImage(this.video, 0, 0, this.width, this.height);
    const imageData = this.context.getImageData(0, 0, this.width, this.height);
    return {
      rgba: imageData.data,
      width: imageData.width,
      height: imageData.height,
    };
  }

  private finalizeDimensions(): void {
    this.width = this.video.videoWidth;
    this.height = this.video.videoHeight;
    if (!this.width || !this.height) {
      this.width = 640;
      this.height = 480;
    }
    this.canvas.width = this.width;
    this.canvas.height = this.height;
    this.resolveReady?.();
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
