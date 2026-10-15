---
name: entra-auth
description: Microsoft Entra ID authentication and role-based authorisation for the API and Angular UI. Use when changing auth, roles, policies, scopes, MSAL config, token handling, route guards, or when sign-in or 401/403 problems occur.
---

# Authentication and authorisation

Every API endpoint requires a signed-in Microsoft Entra ID user. No Azure subscription is needed, only an Entra tenant (a free one is fine).

## Roles
Entra *app roles* on the API registration; values are case-sensitive and defined in `Domain/AppRoles.cs`:

- `Reader`: view claims and documents, ask questions of a document.
- `Handler`: Reader, plus create claims, upload documents, delete documents.
- `Admin`: everything (nothing is admin-only yet).

## API
`src/Api/Authorization/AuthorizationPolicies.cs`, `Program.cs`:

- `Microsoft.Identity.Web` validates the access token.
- Policies `CanRead` (Reader/Handler/Admin) and `CanWrite` (Handler/Admin) both also require the `access_as_user` scope.
- The fallback policy requires the scope and a known role, so a forgotten `[Authorize]` is not open.
- The API refuses to start without `AzureAd:TenantId` and `AzureAd:ClientId`.
- `GET /api/me` returns the caller's name and roles.
- **`MapInboundClaims = false` is required.** By default the JWT handler renames `roles` to `ClaimTypes.Role`, which silently made every user role-less (Admin included). Do not remove it.
- New endpoints must be added to the `Endpoints` list in `EndpointAuthorizationTests` (see the `backend-testing` skill).

## UI
`src/Ui/src/app/auth/`:

- `@azure/msal-browser` (auth code + PKCE, redirect flow, session-storage cache).
- `AuthService` signs in/out and gets tokens.
- `authInterceptor` adds the bearer token to calls to our API only.
- `authGuard` protects every route and waits for `UserService` to load roles from `/api/me`.
- Controls the user can't use (create/upload/delete) are hidden for Readers; the API enforces the same rules independently.
- Everything is a no-op during SSR/prerender.

## Config (public identifiers, not secrets)
- `AzureAd` in `src/Api/appsettings.json`.
- `auth` (authority, SPA client id, API scope) in `src/Ui/src/environments/environment*.ts`.
- The UI requests `api://<API client id>/access_as_user`.

## Entra setup (portal steps for a new tenant)
1. *API app registration* (single tenant): set the Application ID URI to `api://<client id>` (and click Save), add a delegated scope `access_as_user` (enabled), set `requestedAccessTokenVersion` to `2` in the manifest, and add app roles `Reader`, `Handler`, `Admin` (allowed member types: Users/Groups).
2. *SPA app registration* (single tenant): platform "Single-page application" with redirect URI `http://localhost:4200`; add the API's `access_as_user` delegated permission and grant admin consent.
3. *Enterprise applications -> the API -> Properties*: "Assignment required" = Yes. Then *Users and groups*: assign each user a role. Assign on the **API's** enterprise app, not the UI's. Group assignment needs a paid tier; assign users directly.

## Gotchas we hit
- The **tenant ID** must be the *Directory (tenant) ID* shown on the app registration's Overview page. A tenant ID copied from elsewhere gave `AADSTS500011` ("resource principal ... not found") after sign-in.
- A user created from a personal Microsoft account is an external identity (`...#EXT#@<tenant>.onmicrosoft.com`); sign in with the original email address, not that long name.
- Roles added after a sign-in are not in the existing token: sign out and in again.
- If sign-in fails, the UI shows the error and a "Try again" button instead of redirecting again, so a misconfiguration cannot loop.

## Not built yet
Per-claim ownership (everyone with a role sees all claims), recording who created/uploaded things, mapping `AssignedTo` to Entra identities.
