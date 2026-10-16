---
name: claims-feature
description: The Claims feature (claim entity, ClaimsController endpoints, claim list/create/details UI). Use when adding or changing claims endpoints, claim fields or validation, claim-document attachment, or the claims screens in the Angular UI.
---

# Claims

## Model
`Claim` (`Reference`, `PolicyNumber`, `ClaimantName`, `IncidentDate`, `AmountClaimed`, `Status`, `AssignedToUserId`/`AssignedToUser`, `CreatedByUserId`, `IsPrivate`, `CreatedAt`).

- Claims and documents are many-to-many through `ClaimDocument` (`ClaimId`, `DocumentId`, `LinkedAt`, `LinkedByUserId`; composite key). A document with no links is **unmatched**. There is no `Document.ClaimId`.
- Deleting a claim is `Restrict`ed while it has links; deleting a document cascades to its links.
- Field length limits are constants on `Claim` and shared by validation and the EF mapping.

## Endpoints (`ClaimsController`, `api/claims`)
- `POST /` creates a claim -> 201. `Reference` is generated (`CLM-yyyyMMdd-XXXXXXXX`), status starts `Open`; invalid input -> 400. The body may carry `assignedToUserId` (must be a known handler, else 400; omitted = the creator) and `isPrivate`.
- `PUT /{id}/assignee` `{ userId }` reassigns to a known handler -> 204 (400 if not a handler). Admin, or the handler who owns the claim; the previous handler can no longer see it afterwards.
- `PUT /{id}/privacy` `{ isPrivate }` -> 204, same permissions.
- `GET /api/users/handlers` (CanWrite) lists `{ id, displayName }` of users who last signed in as a Handler; it feeds the assignee dropdowns.
- `GET /?pageNumber=&pageSize=&q=&status=` lists claims newest first (`pageSize` 1-100). `q` is a case-insensitive substring match on reference, policy number or claimant name (max 100 chars; `%`/`_` match literally); `status` filters by `ClaimStatus` (integer). The response is a bare array, so the UI treats a full page as "maybe more".
- `GET /{id}` returns `{ claim, documents[] }` (documents exclude `ExtractedText`); 404 if missing.
- `PUT /{id}/documents/{documentId}` links an existing document to the claim -> 204 (idempotent). `DELETE` of the same URL unlinks it -> 204 (the document is kept). Both need sight of the document and the right to *act* on the claim (Admin, or the assigned Handler); 404 if either isn't visible, 403 if visible but not actable.
- `POST /{id}/documents` uploads a multipart `file` and attaches it to the claim (404 if the claim doesn't exist); the document and its link are saved in one transaction. Goes through the normal processing pipeline.

Access is per claim, not just per role (see `entra-auth`, "Per-claim access"): list/detail/upload only see claims the caller may view, a claim that isn't visible is a 404, and creating a claim assigns it to the creator (`AssignedToUserId`, `CreatedByUserId`). Documents record `UploadedByUserId`.

Related document endpoints (`DocumentsController`): `GET /api/documents?pageNumber=&pageSize=&q=&unmatched=` (`q` = file-name substring, `unmatched=true` = no claim links, `pageSize` 1-100) and `GET /api/documents/{id}/claims` (the linked claims the caller can view; empty = unmatched).

Responses use DTOs (`ClaimResponse`, `ClaimDetailResponse`, `ClaimDocumentResponse`); enums serialise as integers.

## UI (`src/Ui/src/app/claims/`, linked from the header)
- `/claims`: a create-claim form and a searchable/filterable, paged claims list (`claim-list/`, `create-claim/`). Each reference links to the claim.
- Create form and `/claims/:id` both have an assignee **dropdown of handlers** (Handlers and Admins only; Readers see the name as text) and a "Private" checkbox. Create defaults to "Me"; details has a Reassign button, and a non-admin is sent back to the list afterwards because they can no longer see the claim.
- `/claims/:id` (`claim-details/`, client-rendered): claim details plus its documents, with an upload control that attaches new documents to the claim (`ClaimService.uploadDocument`). The page stays put after an upload and polls every 2s while any document is still processing.
- Claim page: each document has an **Unlink** button, and a "Link an existing document" panel searches unmatched documents by file name with a **Link** button per result (Handlers/Admins only).
- Document page (`/documents/:id`): a **Claims** section lists the linked claims (Unlink) and a "Link to a claim" panel searches claims (reference/policy/claimant) to link. "Back to claim" and the post-delete redirect use the `?claim=<id>` query param the claim page adds to its document links; otherwise they go to the document list.

## Not built yet
- Claim-wide question answering.
- Editing a claim's status.
- Auto-matching an uploaded document to a claim by its reference (matching is manual for now).
- Handlers appear in the dropdown only after they have signed in once as a Handler (`IsHandler` is a snapshot taken at sign-in), so a role change needs a fresh sign-in to be reflected.
- Endpoints to update a claim's status/assignee or delete a claim.

## Related
Roles required per endpoint: see the `entra-auth` skill. New endpoints must be added to `EndpointAuthorizationTests` (see `backend-testing`).
