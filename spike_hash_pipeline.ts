import { spawnSync } from "child_process";
import fs from "fs";
import os from "os";
import path from "path";

// Hardcoded input path - replace with actual video file
const INPUT_PATH = "d:\\Github\\eSIGELEC_wOrk\\cloud_computing\\dashcam-cloudservice-web\\test_blender.mp4";
const INTERVAL_MS = 500;
const TOLERANCE_MS = 200;
const THRESHOLDS = [5, 8, 10, 12, 15, 18, 22];

const FFPROBE = "ffprobe";
const FFMPEG = "ffmpeg";

type HashEntry = { t_ms: number; hash: string };

type VariantResult = {
  name: string;
  path: string;
  timeline: HashEntry[];
  minDistances: Array<number | null>;
};

function run(cmd: string, args: string[], label: string): Buffer {
  const result = spawnSync(cmd, args, { encoding: "buffer" });
  if (result.status !== 0) {
    const stderr = result.stderr ? result.stderr.toString("utf8") : "";
    throw new Error(`${label} failed: ${cmd} ${args.join(" ")}\n${stderr}`);
  }
  return result.stdout ?? Buffer.alloc(0);
}

function getDurationMs(inputPath: string): number {
  const out = run(
    FFPROBE,
    [
      "-v",
      "error",
      "-show_entries",
      "format=duration",
      "-of",
      "default=noprint_wrappers=1:nokey=1",
      inputPath,
    ],
    "ffprobe"
  )
    .toString("utf8")
    .trim();
  const seconds = Number.parseFloat(out);
  if (!Number.isFinite(seconds)) {
    throw new Error(`ffprobe returned invalid duration: ${out}`);
  }
  return Math.floor(seconds * 1000);
}

function dHashFromGray(data: Buffer, width: number, height: number): string {
  if (data.length !== width * height) {
    throw new Error(`Unexpected frame size: got ${data.length}, expected ${width * height}`);
  }
  let hash = 0n;
  let bit = 0n;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width - 1; x += 1) {
      const left = data[y * width + x];
      const right = data[y * width + x + 1];
      if (left > right) {
        hash |= 1n << bit;
      }
      bit += 1n;
    }
  }
  return hash.toString(16).padStart(16, "0");
}

function sampleHashAt(inputPath: string, tMs: number): string {
  const seconds = (tMs / 1000).toFixed(3);
  const raw = run(
    FFMPEG,
    [
      "-v",
      "error",
      "-ss",
      seconds,
      "-i",
      inputPath,
      "-frames:v",
      "1",
      "-vf",
      "scale=9:8:flags=lanczos,format=gray",
      "-f",
      "rawvideo",
      "-pix_fmt",
      "gray",
      "pipe:1",
    ],
    "ffmpeg sample"
  );
  return dHashFromGray(raw, 9, 8);
}

function buildTimeline(inputPath: string, durationMs: number, intervalMs: number): HashEntry[] {
  const timeline: HashEntry[] = [];
  for (let tMs = 0; tMs <= durationMs; tMs += intervalMs) {
    const hash = sampleHashAt(inputPath, tMs);
    timeline.push({ t_ms: tMs, hash });
  }
  return timeline;
}

function hammingDistanceHex(a: string, b: string): number {
  const x = BigInt(`0x${a}`) ^ BigInt(`0x${b}`);
  let v = x;
  let count = 0;
  while (v > 0n) {
    if (v & 1n) count += 1;
    v >>= 1n;
  }
  return count;
}

function percentile(values: number[], p: number): number | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const idx = Math.floor((p / 100) * (sorted.length - 1));
  return sorted[idx] ?? null;
}

function computeMinDistances(
  reference: HashEntry[],
  candidate: HashEntry[],
  intervalMs: number,
  toleranceMs: number
): Array<number | null> {
  const result: Array<number | null> = [];
  for (const entry of reference) {
    const minIndex = Math.max(0, Math.floor((entry.t_ms - toleranceMs) / intervalMs));
    const maxIndex = Math.min(
      candidate.length - 1,
      Math.ceil((entry.t_ms + toleranceMs) / intervalMs)
    );
    if (candidate.length === 0 || minIndex > maxIndex) {
      result.push(null);
      continue;
    }
    let best: number | null = null;
    for (let i = minIndex; i <= maxIndex; i += 1) {
      const dist = hammingDistanceHex(entry.hash, candidate[i].hash);
      if (best === null || dist < best) best = dist;
    }
    result.push(best);
  }
  return result;
}

function summarizeMatches(
  minDistances: Array<number | null>,
  threshold: number,
  intervalMs: number
): { matchRatio: number; avg: number; max: number; missingSpans: Array<[number, number]> } {
  let matched = 0;
  let total = minDistances.length;
  let sum = 0;
  let max = 0;
  const missingSpans: Array<[number, number]> = [];
  let spanStart: number | null = null;

  for (let i = 0; i < minDistances.length; i += 1) {
    const dist = minDistances[i];
    const isMatch = dist !== null && dist <= threshold;
    if (isMatch && dist !== null) {
      matched += 1;
      sum += dist;
      if (dist > max) max = dist;
      if (spanStart !== null) {
        missingSpans.push([spanStart, (i - 1) * intervalMs]);
        spanStart = null;
      }
    } else {
      if (spanStart === null) spanStart = i * intervalMs;
    }
  }
  if (spanStart !== null) {
    missingSpans.push([spanStart, (minDistances.length - 1) * intervalMs]);
  }

  const matchRatio = total === 0 ? 0 : matched / total;
  const avg = matched === 0 ? 0 : sum / matched;
  return { matchRatio, avg, max, missingSpans };
}

function formatSpans(spans: Array<[number, number]>): string {
  if (spans.length === 0) return "none";
  const preview = spans.slice(0, 3).map(([s, e]) => `${s}-${e}ms`).join(", ");
  return spans.length > 3 ? `${preview}, ... (${spans.length} total)` : preview;
}

function createVariants(inputPath: string, tmpDir: string): Array<{ name: string; path: string }> {
  const reencodePath = path.join(tmpDir, "variant_reencode.mp4");
  const fpsPath = path.join(tmpDir, "variant_fps25.mp4");

  run(
    FFMPEG,
    [
      "-v",
      "error",
      "-y",
      "-i",
      inputPath,
      "-c:v",
      "libx264",
      "-preset",
      "veryfast",
      "-crf",
      "28",
      "-c:a",
      "aac",
      "-b:a",
      "128k",
      reencodePath,
    ],
    "ffmpeg re-encode"
  );

  run(
    FFMPEG,
    [
      "-v",
      "error",
      "-y",
      "-i",
      inputPath,
      "-vf",
      "fps=25",
      "-c:v",
      "libx264",
      "-preset",
      "veryfast",
      "-crf",
      "28",
      "-c:a",
      "aac",
      "-b:a",
      "128k",
      fpsPath,
    ],
    "ffmpeg fps change"
  );

  return [
    { name: "re-encode", path: reencodePath },
    { name: "fps-25", path: fpsPath },
  ];
}

function main(): void {
  if (!fs.existsSync(INPUT_PATH)) {
    console.error(`Input file not found: ${INPUT_PATH}`);
    process.exit(1);
  }

  const durationMs = getDurationMs(INPUT_PATH);
  console.log("Input:", INPUT_PATH);
  console.log(`Duration: ${durationMs} ms`);
  console.log(`Interval: ${INTERVAL_MS} ms, Tolerance: ${TOLERANCE_MS} ms`);
  console.log("Hash: dHash (9x8 grayscale, 64-bit)");

  console.log("\nGenerating ground truth timeline...");
  const reference = buildTimeline(INPUT_PATH, durationMs, INTERVAL_MS);
  console.log(`Frames hashed: ${reference.length}`);

  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), "hash-spike-"));
  console.log(`\nCreating variants in ${tmpDir}`);
  const variants = createVariants(INPUT_PATH, tmpDir);

  const results: VariantResult[] = [];

  for (const variant of variants) {
    console.log(`\nSampling variant: ${variant.name}`);
    const timeline = buildTimeline(variant.path, durationMs, INTERVAL_MS);
    const minDistances = computeMinDistances(reference, timeline, INTERVAL_MS, TOLERANCE_MS);
    results.push({ name: variant.name, path: variant.path, timeline, minDistances });
  }

  console.log("\nDistance stats (min distance per reference frame):");
  for (const result of results) {
    const distances = result.minDistances.filter((d): d is number => d !== null);
    const p50 = percentile(distances, 50);
    const p90 = percentile(distances, 90);
    const p95 = percentile(distances, 95);
    console.log(
      `${result.name}: p50=${p50 ?? "n/a"}, p90=${p90 ?? "n/a"}, p95=${p95 ?? "n/a"}`
    );
  }

  console.log("\nThreshold sweep: match ratio (%)");
  const header = ["variant", ...THRESHOLDS.map((t) => `<=${t}`)].join("\t");
  console.log(header);
  for (const result of results) {
    const ratios = THRESHOLDS.map((t) => {
      const summary = summarizeMatches(result.minDistances, t, INTERVAL_MS);
      return (summary.matchRatio * 100).toFixed(1);
    });
    console.log([result.name, ...ratios].join("\t"));
  }

  console.log("\nSuggested threshold range (p90-p95) based on min distances:");
  for (const result of results) {
    const distances = result.minDistances.filter((d): d is number => d !== null);
    const p90 = percentile(distances, 90);
    const p95 = percentile(distances, 95);
    console.log(`${result.name}: ${p90 ?? "n/a"} - ${p95 ?? "n/a"}`);
  }

  console.log("\nSummary (using per-variant p90 as threshold):");
  console.log("variant\tthreshold\tmatch%\tavgDist\tmaxDist\tmissingSpans");
  for (const result of results) {
    const distances = result.minDistances.filter((d): d is number => d !== null);
    const threshold = percentile(distances, 90) ?? 10;
    const summary = summarizeMatches(result.minDistances, threshold, INTERVAL_MS);
    const matchPct = (summary.matchRatio * 100).toFixed(1);
    const avg = summary.avg.toFixed(2);
    const max = summary.max.toFixed(0);
    const spans = formatSpans(summary.missingSpans);
    console.log(`${result.name}\t${threshold}\t${matchPct}\t${avg}\t${max}\t${spans}`);
  }

  console.log("\nPass criteria: match ratio >= 80% at small tolerance/threshold.");
}

main();
