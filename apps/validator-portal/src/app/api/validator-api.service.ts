import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Inject, Injectable, InjectionToken, Optional } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { VerificationResult } from '../models/verification.models';

export type AuthTokenProvider = () => string | null;

export const AUTH_TOKEN_PROVIDER = new InjectionToken<AuthTokenProvider>(
  'AUTH_TOKEN_PROVIDER'
);

export function buildVerifyClaimFormData(
  videoFile: File,
  sessionId: string,
  metadataFile?: File | null
): FormData {
  const formData = new FormData();
  formData.append('video', videoFile);
  formData.append('sessionId', sessionId);
  if (metadataFile) {
    formData.append('metadata', metadataFile);
  }
  return formData;
}

@Injectable({
  providedIn: 'root'
})
export class ValidatorApiService {
  constructor(
    private http: HttpClient,
    @Optional() @Inject(AUTH_TOKEN_PROVIDER)
    private tokenProvider?: AuthTokenProvider
  ) {}

  verifyClaim(
    videoFile: File,
    sessionId: string,
    metadataFile?: File | null,
    accessToken?: string | null,
    debugEnabled?: boolean
  ): Observable<VerificationResult> {
    const formData = buildVerifyClaimFormData(videoFile, sessionId, metadataFile);
    const headers = this.buildAuthHeaders(accessToken);
    let url = this.buildUrl('/api/claims/verify');
    if (debugEnabled) {
      url += '?debug=1';
    }

    return this.http.post<VerificationResult>(url, formData, headers ? { headers } : {});
  }

  private buildUrl(path: string): string {
    const baseUrl = environment.validatorApiBaseUrl.replace(/\/$/, '');
    return `${baseUrl}${path}`;
  }

  private buildAuthHeaders(overrideToken?: string | null): HttpHeaders | null {
    const token = overrideToken ?? this.tokenProvider?.();
    if (!token) {
      return null;
    }
    return new HttpHeaders({ Authorization: `Bearer ${token}` });
  }
}
