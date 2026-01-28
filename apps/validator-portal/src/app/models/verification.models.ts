export type Verdict = 'Verified' | 'Suspicious' | 'Inconclusive';

export interface MissingSpan {
  startElapsedMs: number;
  endElapsedMs: number;
  reason: string;
}

export interface VerificationResult {
  verdict: Verdict;
  sessionId: string;
  threshold: number;
  toleranceMs: number;
  intervalMs: number;
  expectedSamples: number;
  matchedSamples: number;
  matchRatio: number;
  avgDistance: number;
  maxDistance: number;
  missingSpans: MissingSpan[];
  notes: string[];
  debug?: VerificationDebugMetrics;
}

export interface VerificationDebugMetrics {
  sessionId: string;
  sessionSamplingIntervalMs: number;
  toleranceMs: number;
  threshold: number;
  referenceHashCount: number;
  extractedFrameCount: number;
  extractedElapsedMsRange?: DebugElapsedMsRange;
  matcherWindowStats: MatcherWindowStat[];
}

export interface DebugElapsedMsRange {
  minElapsedMs: number;
  maxElapsedMs: number;
}

export interface MatcherWindowStat {
  refElapsedMs: number;
  candidateCountInWindow: number;
  bestMinDistance: number | null;
}
