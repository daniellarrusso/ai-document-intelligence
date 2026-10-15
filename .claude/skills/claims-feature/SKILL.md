---
name: claims-feature
description: The Claims feature (claim entity, ClaimsController endpoints, claim list/create/details UI). Use when adding or changing claims endpoints, claim fields or validation, claim-document attachment, or the claims screens in the Angular UI.
---

# Claims

## Model
`Claim` (`Reference`, `PolicyNumber`, `ClaimantName`, `IncidentDate`, `AmountClaimed`, `Status`, `AssignedTo`, `CreatedAt`).

- One claim has many documents via the nullable `Document.ClaimId` (null for standalone documents).
- Deleting a claim is `Restrict`ed while it has documents.
- Field length limits are constants on `Claim` and shared by validation and the EF mapping.

## Endpoints (`ClaimsController`, `api/claims`)
- `POST /` creates a claim -> 201. `Reference` is generated (`CLM-yyyyMMdd-XXXXXXXX`), status starts `Open`; invalid input -> 400.
- `GET /?pageNumber=&pageSize=&q=&status=` lists claims newest first (`pageSize` 1-100). `q` is a case-insensitive substring match on reference, policy number or claimant name (max 100 chars; `%`/`_` match literally); `status` filters by `ClaimStatus` (integer). The response is a bare array, so the UI treats a full page as "maybe more".
- `GET /{id}` returns `{ claim, documents[] }` (documents exclude `ExtractedText`); 404 if missing.
- `POST /{id}/documents` uploads a multipart `file` and attaches it to the claim (404 if the claim doesn't exist). Goes through the normal processing pipeline.

Responses use DTOs (`ClaimResponse`, `ClaimDetailResponse`, `ClaimDocumentResponse`); enums serialise as integers.

## UI (`src/Ui/src/app/claims/`, linked from the header)
- `/claims`: a create-claim form and a searchable/filterable, paged claims list (`claim-list/`, `create-claim/`). Each reference links to the claim.
- `/claims/:id` (`claim-details/`, client-rendered): read-only claim details plus its documents, with an upload control that attaches new documents to the claim (`ClaimService.uploadDocument`). The page stays put after an upload and polls every 2s while any document is still processing.
- Documents that belong to a claim link back to it ("Back to claim") and return to it after being deleted; standalone documents behave as before.

## Not built yet
- Claim-wide question answering.
- Editing a claim's status/assignee.
- Endpoints to update a claim's status/assignee or delete a claim.

## Related
Roles required per endpoint: see the `entra-auth` skill. New endpoints must be added to `EndpointAuthorizationTests` (see `backend-testing`).
