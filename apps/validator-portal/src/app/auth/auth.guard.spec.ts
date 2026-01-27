import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { AuthGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('AuthGuard', () => {
  it('allows navigation when authenticated', async () => {
    TestBed.configureTestingModule({
      imports: [RouterTestingModule],
      providers: [
        AuthGuard,
        {
          provide: AuthService,
          useValue: { isAuthenticated: () => Promise.resolve(true) }
        }
      ]
    });

    const guard = TestBed.inject(AuthGuard);
    const result = await guard.canActivate();

    expect(result).toBeTrue();
  });

  it('redirects to /login when unauthenticated', async () => {
    TestBed.configureTestingModule({
      imports: [RouterTestingModule],
      providers: [
        AuthGuard,
        {
          provide: AuthService,
          useValue: { isAuthenticated: () => Promise.resolve(false) }
        }
      ]
    });

    const guard = TestBed.inject(AuthGuard);
    const router = TestBed.inject(Router);
    const result = await guard.canActivate();

    expect(result instanceof UrlTree).toBeTrue();
    expect(router.serializeUrl(result as UrlTree)).toBe('/login');
  });
});
