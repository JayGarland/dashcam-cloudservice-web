import { Injectable } from '@angular/core';
import { createClient, Session, SupabaseClient } from '@supabase/supabase-js';

import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private static readonly SESSION_STORAGE_KEY =
    'validator-portal.supabase.session';

  private readonly client: SupabaseClient;
  private session: Session | null = null;

  constructor() {
    this.client = createClient(environment.supabaseUrl, environment.supabaseAnonKey, {
      auth: {
        persistSession: false,
        autoRefreshToken: false
      }
    });
    this.session = this.loadSession();
  }

  async signIn(email: string, password: string): Promise<void> {
    const { data, error } = await this.client.auth.signInWithPassword({
      email,
      password
    });

    if (error) {
      throw error;
    }
    if (!data.session) {
      throw new Error('No session returned from Supabase.');
    }

    this.saveSession(data.session);
  }

  async signOut(): Promise<void> {
    await this.client.auth.signOut();
    this.saveSession(null);
  }

  getAccessTokenSync(): string | null {
    const session = this.getSessionSync();
    return session?.access_token ?? null;
  }

  async getAccessToken(): Promise<string | null> {
    return this.getAccessTokenSync();
  }

  async isAuthenticated(): Promise<boolean> {
    return Boolean(this.getSessionSync());
  }

  private getSessionSync(): Session | null {
    if (!this.session) {
      this.session = this.loadSession();
    }

    if (!this.session) {
      return null;
    }

    if (this.session.expires_at && this.session.expires_at * 1000 <= Date.now())
    {
      this.saveSession(null);
      return null;
    }

    return this.session;
  }

  private saveSession(session: Session | null): void {
    this.session = session;
    if (!session) {
      localStorage.removeItem(AuthService.SESSION_STORAGE_KEY);
      return;
    }

    localStorage.setItem(
      AuthService.SESSION_STORAGE_KEY,
      JSON.stringify(session)
    );
  }

  private loadSession(): Session | null {
    const raw = localStorage.getItem(AuthService.SESSION_STORAGE_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as Session;
    } catch (error) {
      console.warn('Failed to parse cached Supabase session.', error);
      return null;
    }
  }
}
