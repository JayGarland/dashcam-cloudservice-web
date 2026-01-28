import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';

import { ValidatorApiService } from '../../api/validator-api.service';
import { AuthService } from '../../auth/auth.service';
import { VerificationResult } from '../../models/verification.models';

@Component({
  selector: 'app-verify-claim',
  templateUrl: './verify-claim.component.html',
  styleUrls: ['./verify-claim.component.css']
})
export class VerifyClaimComponent {
  videoFile?: File;
  metadataFile?: File;
  sessionId = '';
  result?: VerificationResult;
  errorMessage = '';
  isLoading = false;

  constructor(private api: ValidatorApiService, private auth: AuthService) {}

  get verdictClass(): string {
    switch (this.result?.verdict) {
      case 'Verified':
        return 'verified';
      case 'Suspicious':
        return 'suspicious';
      case 'Inconclusive':
      default:
        return 'inconclusive';
    }
  }

  onVideoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.videoFile = input.files && input.files.length > 0 ? input.files[0] : undefined;
  }

  onMetadataSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.metadataFile =
      input.files && input.files.length > 0 ? input.files[0] : undefined;
  }

  onSessionIdChanged(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.sessionId = input.value;
  }

  async submit(): Promise<void> {
    this.errorMessage = '';

    if (!this.videoFile || !this.sessionId.trim()) {
      this.errorMessage = 'Please select a video file and enter a session ID.';
      return;
    }

    const accessToken = await this.auth.getAccessToken();
    if (!accessToken) {
      this.errorMessage = 'Please sign in before verifying a claim.';
      return;
    }

    this.isLoading = true;
    this.result = undefined;

    this.api
      .verifyClaim(this.videoFile, this.sessionId.trim(), this.metadataFile, accessToken)
      .subscribe({
      next: (result) => {
        this.result = result;
        this.isLoading = false;
      },
      error: (error: HttpErrorResponse) => {
        this.isLoading = false;
        this.handleError(error);
      }
    });
  }

  private handleError(error: HttpErrorResponse): void {
    const status = error.status ? `HTTP ${error.status}` : 'Request failed';
    let detail = '';

    if (typeof error.error === 'string') {
      detail = error.error;
    } else if (error.error?.message) {
      detail = error.error.message;
    } else if (error.message) {
      detail = error.message;
    }

    this.errorMessage = detail ? `${status}: ${detail}` : status;
  }
}
