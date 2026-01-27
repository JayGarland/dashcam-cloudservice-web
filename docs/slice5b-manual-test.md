# Slice 5B Manual Test Guide

This guide covers manual verification of auth end-to-end for Slice 5B:
- Capture client requires login to write to Supabase.
- Validator portal requires login for the verify page.
- Validator API enforces JWT auth + validator role.

## Prerequisites
- Supabase project with Auth enabled.
- Two users created in Supabase Auth:
  - validator user (role = validator)
  - non-validator user (no validator role)
- Supabase JWT secret available (Project Settings → API → JWT Secret).
- Supabase URL + anon key for both clients.

## One-time setup
1) Assign validator role in Supabase app_metadata (example SQL):
   ```sql
   update auth.users
   set app_metadata = jsonb_set(coalesce(app_metadata, '{}'::jsonb), '{role}', '"validator"', true)
   where email = 'validator@example.com';
   ```
2) Sign out/in for the validator user so the new role is in the JWT.
3) Set `Supabase:JwtSecret` in `services/validator-api/appsettings.Development.json`.

## Start services
1) Validator API
   ```bash
   cd services/validator-api
   dotnet run
   ```
2) Validator Portal
   ```bash
   cd apps/validator-portal
   npm install
   npm start
   ```
3) Capture Client
   ```bash
   cd apps/capture-client
   npm install
   npm run dev
   ```

## Manual test cases

### A) Capture Client Auth Gate
1) Open `http://localhost:8000`.
2) Enter Supabase URL + anon key.
3) Confirm Start Session is disabled (not signed in).
4) Sign in with a valid email/password.
5) Confirm the UI shows “Signed in as …” and Start Session is enabled.
6) Start Camera, then Start Session.
7) Expected: insertSession succeeds; uploads start (no auth errors).

### B) Validator Portal Login Guard
1) Open `http://localhost:4200/claims/verify` in a fresh session.
2) Expected: redirect to `/login`.
3) Sign in using the validator user.
4) Expected: redirected to `/claims/verify` and Verify UI is visible.

### C) Validator API Auth Enforcement (HTTP)
Use curl or Postman against `https://localhost:5001/api/claims/verify`.

1) No token → 401
   - Send a multipart request without Authorization header.
   - Expected: `401 Unauthorized`.

2) Non-validator token → 403
   - Sign in as non-validator user and grab access token.
   - Send the same multipart request with `Authorization: Bearer <token>`.
   - Expected: `403 Forbidden`.

3) Validator token → 200
   - Sign in as validator user and grab access token.
   - Send the request with `Authorization: Bearer <token>`.
   - Expected: `200 OK` with verification payload.

## Notes
- If the validator role change is not reflected, sign out and sign in again.
- Token expiration is not auto-refreshed in demo mode; re-login if needed.
- The API uses `issuer = supabase` and the JWT secret for validation.
