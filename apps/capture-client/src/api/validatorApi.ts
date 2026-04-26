export interface RecordedHashesResponse {
  sessionId: string;
  storedCount: number;
}

export function resolveValidatorApiBaseUrl(): string {
  const globalConfig = (window as unknown as { __APP_CONFIG__?: { validatorApiBaseUrl?: string } })
    .__APP_CONFIG__;
  const configured = globalConfig?.validatorApiBaseUrl?.trim();
  if (configured) {
    return configured.replace(/\/$/, "");
  }
  return new URL("/api", window.location.origin).toString().replace(/\/$/, "");
}

export async function uploadRecordedHashesViaValidatorApi(
  baseUrl: string,
  sessionId: string,
  video: Blob,
  intervalMs: number,
  accessToken: string
): Promise<RecordedHashesResponse> {
  const endpoint = `${baseUrl}/sessions/${encodeURIComponent(sessionId)}/recorded-hashes`;
  const form = new FormData();
  form.append("video", video, "recorded.webm");
  form.append("intervalMs", String(intervalMs));

  const response = await fetch(endpoint, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
    body: form,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(
      `validator-api recorded-hashes failed (${response.status}): ${message || response.statusText}`
    );
  }

  return (await response.json()) as RecordedHashesResponse;
}
