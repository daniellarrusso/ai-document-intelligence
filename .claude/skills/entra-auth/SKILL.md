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

## Per-claim access (who sees and acts on what)
The roles above are the coarse gate; `Domain/ClaimAccess.cs` is the single definition of per-claim access. The user's **highest** role wins (Admin > Handler > Reader):

| Role | Sees | Acts on (upload, delete document) |
|---|---|---|
| Admin | every claim and document | everything |
| Handler | only claims assigned to them, and unmatched documents they uploaded | only those |
| Reader | every claim that is not `IsPrivate`, and standalone documents | nothing |

- A document is visible if **any** claim it is linked to is visible (an unmatched one follows the standalone rule). Changing a link needs sight of the document and the right to act on the claim. Deleting a document needs the right to act on **every** claim it is linked to (`ClaimAccess.CanActOnDocument`). Unlinking can make a Handler lose sight of a document they didn't upload.
- A claim or document the caller may not view is a **404**, not 403, so private claims don't reveal their existence. 403 is only for "can view, may not act".
- Filtering happens in the repository query (`ClaimAccess.CanView` expression, `DocumentRepository.VisibleTo`), so paging and search are correct. Keep the expression and the in-memory checks in step (tests assert it).
- `IDocumentRepository.GetDocumentByIdAsync` is **unfiltered** and only for the processing worker; request handling uses `GetDocumentForUserAsync`.
- Controllers get the caller from `User.GetCurrentUser()` (needs the `oid` claim) and return 403 if it is missing, so a token without an object id fails closed.
- `AppUser.Id` is the Entra object id; users are recorded on `/api/me` and again when they create a claim or upload.

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
Auto-matching uploaded documents to claims. Handler membership is a snapshot taken at sign-in (`AppUser.IsHandler`), so a user removed from the Handler role stays assignable until they next sign in.
