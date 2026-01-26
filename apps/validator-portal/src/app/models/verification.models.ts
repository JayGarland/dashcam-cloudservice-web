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
}
