import { Component } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from './auth.service';

@Component({
  selector: 'app-auth',
  templateUrl: './auth.component.html',
  styleUrls: ['./auth.component.css']
})
export class AuthComponent {
  email = '';
  password = '';
  errorMessage = '';
  isLoading = false;
  isLoggedIn = false;

  constructor(private auth: AuthService, private router: Router) {
    void this.refreshStatus();
  }

  async signIn(): Promise<void> {
    this.errorMessage = '';
    this.isLoading = true;

    try {
      await this.auth.signIn(this.email.trim(), this.password);
      await this.refreshStatus();
      await this.router.navigate(['/claims/verify']);
    } catch (error) {
      this.errorMessage =
        error instanceof Error ? error.message : 'Login failed.';
    } finally {
      this.isLoading = false;
    }
  }

  async signOut(): Promise<void> {
    this.errorMessage = '';
    this.isLoading = true;

    try {
      await this.auth.signOut();
      await this.refreshStatus();
    } catch (error) {
      this.errorMessage =
        error instanceof Error ? error.message : 'Logout failed.';
    } finally {
      this.isLoading = false;
    }
  }

  private async refreshStatus(): Promise<void> {
    this.isLoggedIn = await this.auth.isAuthenticated();
  }
}
