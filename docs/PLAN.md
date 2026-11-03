# Production Plan: AI Document Intelligence

## Context

The app today accepts uploads for claims but only really supports PDFs. Any other file is stored, then fails text extraction. There is also no way to view or download a stored file, and nothing exists for production: no CI, no containers, no health checks, no production config, no upload hardening.

Two product needs drive this plan:

1. Upload more file types: `.msg` and `.eml` email, plus images and videos that form part of a claim.
2. Preview those files in the browser.

The plan then covers production readiness and a candidate feature list for you to prioritise.

## Progress

**Done: Foundation slice (sequencing step 1) plus a first cut of preview**

- 1.1 `FileTypePolicy`: allow-list by extension plus magic bytes, per-category size limits, detected content type stored, 400 with a clear reason, sanitised blob names (`documents/{id}/original/{guid}{ext}`), per-endpoint request size limits. Currently allows pdf, txt, jpg/jpeg, png, gif, webp, mp4, mov, webm.
- 1.2 Extractors chosen by content type (`IDocumentTextExtractor.CanHandle`); `PlainTextDocumentTextExtractor` added; images and video complete with no text instead of failing.
- 1.4 Orphan-blob cleanup when the DB write fails after upload.
- 1.5 (partial) File picker `accept` list and server error messages in both upload screens. Not yet: drag and drop, multi-file, progress bar, `GET /api/documents/upload-policy`.
- 2.1 `GET /api/documents/{id}/content` and `/download` (per-claim access, Range support, `nosniff`, `Cache-Control: private, no-store`, inline only for previewable types), added to `EndpointAuthorizationTests`. Fetched via `HttpClient` into `blob:` URLs (option 1).
- 2.2 (partial) PDF, image, video and plain-text viewers in `DocumentPreview`, shown on the details page and in a right-hand preview blade that opens when hovering a document name in the document list and claim documents. Not yet: pdf.js, zoom/rotate, email viewer.

**Not done yet** (everything else in this plan), notably:

- `.eml`/`.msg` (MimeKit, MsgReader, `IEmailParser`), `.docx`, HEIC/TIFF, and the 2.3 email preview with server-side sanitisation.
- 1.3 data model additions (`Category`, `ParentDocumentId`, scan status) and their migration. (Main has since gained `Document.ContentHash`, a SHA-256 of every upload, which covers the `Sha256` field; the backfill of older documents and the index are still to check.) This slice avoided a migration by deriving the category from the stored content type.
- 1.4 malware scanning with quarantine; 1.6 attachment review, duplicate and signature detection.
- 2.4 SAS-based video streaming, direct-to-blob upload and thumbnails.
- Phases 3 and 4.

**Known limitations of what shipped**

- The preview and hover blade download the whole file before showing it, so large video is slow (fixed by 2.4 SAS URLs). Thumbnails would also make hovering cheaper.
- Documents uploaded before this change keep their client-supplied content type and show "no preview" with Download.
- The video limit is 500 MB at the request level; direct-to-blob upload (open question 3) is still undecided.
- The initial UI bundle is over its 500 kB budget (651 kB); not checked whether this predates the slice.
- Hover is mouse and keyboard focus only; there is no touch trigger.

**Suggested next step:** `.eml`/`.msg` extraction and the email viewer (sequencing step 2), which also needs the `ParentDocumentId` migration (`ContentHash` already exists).

## Current state (from code inspection)

*This section describes the code before the foundation slice above.*

| Area | Today |
|---|---|
| Upload | `POST /api/documents` and `POST /api/claims/{id}/documents` take an `IFormFile`. The only checks are non-empty, ≤50 MB and a non-blank content type (`BlobDocumentStorage.cs`). The client-supplied content type is trusted. There is no allow-list or filename sanitisation. |
| Download or preview | **None.** `IDocumentStorage.DownloadAsync` is used only by the background worker. The UI shows only extracted text in a `<pre>`. |
| Text extraction | One extractor, `PdfDocumentTextExtractor` (PdfPig), which throws `NotSupportedException` for anything else. Scanned PDFs get no OCR. |
| Processing | An in-process unbounded `Channel<Guid>` and a `BackgroundService`. It is not durable, so queued items are lost on restart and documents stuck in `Processing` are never recovered. |
| Blob naming | `documents/{id}/original/{FileName}` with an unsanitised `FileName`. |
| Ops | No Dockerfile, CI, health checks, structured logging or rate limiting. Config is dev-only (hard-coded dev password and `localhost` CORS). `BlobServiceClient` uses a connection string only. |

## Phase 1: Multi-type upload

**Goal:** accept `.pdf`, `.eml`, `.msg`, images and video safely, and extract searchable text where it makes sense.

### 1.1 Supported types and a central allow-list
- Add a `FileTypePolicy` (Domain or Infrastructure) that maps extension to allowed MIME types, a category and a per-category max size.

| Category | Types | Max size (proposed) | Text for RAG? |
|---|---|---|---|
| Document | pdf, docx, txt | 50 MB | Yes |
| Email | eml, msg | 50 MB | Yes (headers, body, attachment list) |
| Image | jpg, png, heic, webp, gif, tiff | 25 MB | Optional (OCR, phase 4) |
| Video | mp4, mov, webm | 500 MB to 1 GB | No (metadata only) |

- Validate by extension **and** magic bytes. Do not trust the client `Content-Type`; detect it server-side and store the detected value.
- Reject executables, scripts and macro-enabled Office files (`.exe`, `.js`, `.docm`, etc.).
- Return **400 with a clear message** instead of the current unhandled `ArgumentException`, which probably yields a 500 (verify).
- Sanitise the stored blob name. Use `documents/{id}/original/{guid}{ext}` and keep the original filename only in the DB.
- Add `[RequestSizeLimit]` or `RequestFormLimits` per endpoint so oversized bodies are rejected before buffering.

### 1.2 Extractor strategy
- Turn `IDocumentTextExtractor` into a set of extractors picked by content type (a resolver or `IEnumerable<IDocumentTextExtractor>` with `CanHandle`).
- Add `EmlTextExtractor` and `MsgTextExtractor`.
  - Candidate libraries: **MimeKit** for `.eml` (MIT, well maintained) and **MsgReader** for `.msg`.
  - Output should include From, To, Cc, Subject, Date and body (prefer plain text, falling back to HTML stripped to text).
  - Expose the attachment list (name, size, content type, inline/CID flag, bytes on demand) through a shared `IEmailParser` so the extractor and the attachment review step (1.6) use one parser. Attachments are imported only when the user selects them.
- Add `DocxTextExtractor` and `PlainTextExtractor`. DocumentFormat.OpenXml is already used widely and is small.
- Images and video: **skip extraction** and mark `Completed` with no text. Do not mark them `Failed`. Add a `DocumentStatus` value only if needed (the enum is append-only because it is stored as an int).
- Each extractor must produce an explicit `Failed` reason rather than throwing `NotSupportedException` for known types.

### 1.3 Data model
- Add to `Document`: `Category` (enum), `DetectedContentType`, `OriginalFileName`, `Sha256` (dedupe and integrity), optional `ParentDocumentId` (email attachments), optional `Width`, `Height` and `DurationSeconds`.
- New EF migration (follow the `db-migrations` skill). Backfill existing rows as `Category = Document`.

### 1.4 Hardening on upload
- **Malware scanning** is required before production, since adult users will upload arbitrary files.
  - Options: Microsoft Defender for Storage (blob scan, a good fit on Azure), or ClamAV in a sidecar.
  - Flow: upload to a quarantine state, scan, then release. Add `ScanStatus` (`Pending`, `Clean`, `Infected`).
  - Block preview and download until `Clean`.
- Fix the orphan-blob case in `DocumentService.SaveDocumentAsync`: if the DB write fails after the blob upload, delete the blob.
- Large video: use chunked or direct-to-blob uploads (see 2.4) so large files do not pass through the API.

### 1.5 Frontend upload
- Add an `accept` attribute and client-side type and size checks that mirror the server policy (fed from a `GET /api/documents/upload-policy` endpoint so there is one source of truth).
- Add drag-and-drop, multi-file upload, a progress bar (`HttpEventType.UploadProgress`) and specific error messages instead of "File upload failed!".
- Use the same component in the claim details upload.

### 1.6 Email attachment selection (two-step email upload)

**Requirement:** when an uploaded `.eml`/`.msg` has attachments, the user chooses which attachments are stored with the email. They can preview each attachment before deciding, and likely signature images (logos, social icons) are flagged automatically so they are not stored by accident.

**Flow**
1. User uploads the email. The API stores and scans it as normal, then parses it and **extracts the attachment list only**. Nothing is persisted as a `Document` yet.
2. The UI shows a "Review attachments" step with one row per attachment: name, size, type, thumbnail, a preview action, a checkbox and a badge when flagged.
3. User confirms. `POST /api/documents/{emailId}/attachments` takes the selected attachment ids, and the API creates a child `Document` for each (`ParentDocumentId` = the email) through the normal pipeline (validation, scan, extraction, indexing).
4. Unselected attachments are not stored as documents. They stay inside the original email blob, so nothing is lost. The email preview lists them as "not imported", and the user can import them later from the email's detail page.
5. No attachments: skip step 2 entirely.

**API (all `CanWrite` + per-claim access checks, added to `EndpointAuthorizationTests`)**
- `GET /api/documents/{emailId}/attachments`: the parsed list, with a stable id (index plus content hash), file name, detected type, size, `IsInline`, `LooksLikeSignature`, `SignatureReason`, and `Imported` state.
- `GET /api/documents/{emailId}/attachments/{attachmentId}/content`: streams one attachment for preview, extracted on demand from the email blob. It reuses the same safety rules as 2.1 (detected type, `nosniff`, scan gating). Attachments are attacker-controlled, so they go through the same allow-list and magic-byte check as any upload.
- `POST /api/documents/{emailId}/attachments`: import the selected ids (idempotent, so retries cannot create duplicates; use the hash to detect that).
- Attachments that fail the allow-list (e.g. `.exe`) are shown as blocked and cannot be selected.

**Duplicate detection (reply chains)**

Reply chains routinely re-attach the same files, so an attachment may already be in the claim from an earlier email (or from a direct upload).

- Match on **content hash (SHA-256), not file name**, because names change across replies and forwards. Compare against every document linked to the target claim (`ClaimDocument` join), whether it arrived as an email attachment or a standalone upload.
- `GET …/attachments` returns `AlreadyInClaim` plus the existing document's id and name. In the review step these rows are **disabled and unchecked**, with the badge "Already in this claim" and a link to the existing document. Users cannot tick them.
- **Enforced server-side, not just in the UI.** The import endpoint skips or rejects any attachment whose hash is already on the claim and reports each as `skipped: duplicate` in the response. The checkbox state in the UI is only a convenience.
- Concurrency: two users importing the same attachment at once must not create two copies. Do the hash check and the insert in one transaction (or behind a unique index on claim + hash through the link table) and handle the conflict as "duplicate".
- Also catch duplicates **within the same email** (the same file attached twice) and collapse them into one row.
- **Email with no claim context** (uploaded as an unmatched document): there is nothing to compare against yet, so the check runs against the claim at the time the email or an attachment is linked or imported. Linking an unmatched document to a claim also needs the duplicate check (see the open question on whether to block or warn).
- If the hash exists elsewhere in the system but not in this claim, show a softer "Also exists in claim X" note (only if the user has access to that claim, to avoid leaking other claims' data) and let them proceed. Optionally offer "link the existing document" instead of storing another copy.
- Needs `Document.Sha256` to be populated on every upload path (not only email attachments), an index on it, and a one-off backfill for existing documents (compute from blob in a background job).

**Signature and noise detection**
- **Tier 1, deterministic (ship first):** score each attachment with cheap rules, none of which delete anything.
  - Inline `Content-ID` image that the HTML body references via `cid:`. These are almost always logos and embedded pictures.
  - Small image: under about 200×200 px or under about 30 KB.
  - Typical generated names: `image001.png`, `Outlook-*.png`, `logo*`, `facebook`/`linkedin`/`twitter` icons.
  - Same SHA-256 seen on many other emails (a repeating signature logo). Needs a hash-frequency lookup against existing `Document.Sha256`.
  - A tracking-pixel size (1×1).
- **Tier 2, AI-assisted (after tier 1 is working):** send only the ambiguous images (not flagged by tier 1, but small or inline) to a vision-capable model with a narrow prompt: "Is this a company logo, social icon, email signature banner or decorative image, or is it substantive content (photo of damage, document, screenshot)?". Return a label plus confidence. This depends on the AI hosting decision in 3.3, and claim images would go to that model, so it needs the same privacy sign-off. Run it behind a feature flag and cache the result by hash.
- **Behaviour:** flagged items are shown **unchecked with a "Likely signature" badge and the reason**, and are never discarded silently. The user can tick them and preview them. Everything else is checked by default. Add a "Select all / none" and a "Hide likely signatures" toggle.
- **Feedback loop:** when the user overrides a flag, record it (hash plus decision) so repeated logos get remembered per tenant. This is optional and can wait.

**Data model additions**
- A `DocumentAttachmentOrigin` link or simply `ParentDocumentId` plus `SourceAttachmentId` and `SourceAttachmentHash` on the child `Document`. This lets the email page show which attachments were imported.
- Optionally a small `EmailAttachmentSuggestion` cache table (hash, label, confidence, source) for the tier 2 results.

**Frontend**
- A new `EmailAttachmentReviewComponent`, shown after email upload completes (also reachable later from the email's details page). It reuses the viewers from Phase 2 in a side panel or modal, so the preview work in 2.2 is a dependency. Build the viewers first, or ship the review step with thumbnails and download initially.
- Handles loading, no-attachments, blocked, scan-pending and error states explicitly. Vitest tests cover default selection, flagged items starting unchecked, and the confirm payload.
- The same step is used when uploading an email inside a claim, in which case the imported attachments are linked to that claim automatically.

**Tests**
- Fixture `.eml`/`.msg` files: one with a signature logo (cid inline), one with a real photo attachment, one with a mix, one with a blocked `.exe` attachment, one with the same file attached twice, one with none, and a reply-chain pair where the second email re-attaches a file from the first. Assert the list, the flags and the import result.
- Duplicate handling: attachment with the same bytes but a different name is flagged; the import endpoint refuses a duplicate even if the request includes it; concurrent imports of the same attachment create one document; a user without access to another claim never sees that claim's details in the "also exists" note.
- A confirm request with an id that is not in the email is rejected (no importing arbitrary blobs).

## Phase 2: In-browser preview

**Goal:** a viewer on the document details page and in the claim documents list, with no file downloaded to disk.

### 2.1 Secure content endpoint
- `GET /api/documents/{id}/content` (`CanRead`, plus the existing per-claim access checks) streams the blob.
  - Support **HTTP Range requests** (`enableRangeProcessing: true`), which video needs for seeking.
  - Headers: `X-Content-Type-Options: nosniff`, `Content-Disposition: inline; filename*=…` for previewable types and `attachment` otherwise, and `Cache-Control: private`.
  - Serve **only the stored detected type**, never the client-supplied one.
  - Return 409 or 423 while `ScanStatus != Clean`.
- `GET /api/documents/{id}/download` forces `attachment`.
- Add both to `EndpointAuthorizationTests` (a CLAUDE.md must-not-break rule).
- **Auth wrinkle:** `<img>`, `<video>` and `<iframe>` cannot send a bearer token. Options:
  1. Fetch with the HttpClient (so the interceptor adds the token), then use a `blob:` URL. This is simple and secure, and fine for images, PDFs and emails. It is poor for large video because it downloads everything first.
  2. Issue a **short-lived signed URL** (an Azure Blob user-delegation SAS, 5 minutes, read-only, per request, after the auth check). This is the best option for video and keeps the API out of the data path.
  - **Recommendation:** use option 1 for small files and option 2 for video. Start with option 1 everywhere, and add SAS in phase 2.4.

### 2.2 Viewers (Angular)
| Type | Viewer |
|---|---|
| PDF | `pdfjs-dist` wrapped in a small standalone component (page nav, zoom, text selection). A plain `<iframe>`/`<object>` with the browser PDF viewer is the cheap fallback. |
| Image | `<img>` with zoom and rotate. HEIC needs server-side conversion to JPEG for most browsers. |
| Video | Native `<video controls>`, with poster-frame thumbnail generation optional. |
| Email (.eml/.msg) | Render server-extracted fields (header block, sanitised body, attachment list) in a component. Do not render the raw HTML directly (see security). |
| Docx | Phase 4: convert to PDF server-side for preview. Until then, show extracted text plus download. |
| Other | Icon, metadata and a download button. |

- A `DocumentPreviewComponent` picks the viewer from `Category` or content type, and handles loading, error, scan-pending and empty states explicitly.
- Add Vitest tests for viewer selection and the state handling.

### 2.3 Email preview security
- Email HTML is attacker-controlled. Sanitise it **server-side** (HtmlSanitizer or equivalent, which strips scripts, forms and event handlers), block remote images and trackers by default (with a "load images" opt-in), and render inside a sandboxed `<iframe sandbox>` with `srcdoc` and no `allow-scripts`.
- Add an API endpoint `GET /api/documents/{id}/email` returning structured JSON (headers, sanitised body, attachments) so the UI never parses `.msg` itself.

### 2.4 Large media and thumbnails
- Direct-to-blob upload for video: the API issues a write-only SAS, the browser uploads in blocks, and a `POST …/complete` call confirms and queues scanning.
- Thumbnails for images (resize server-side) and video (poster frame via ffmpeg in a worker). Store under `documents/{id}/derived/`.
- Strip EXIF GPS data from derived copies. Keep the original untouched but decide a retention policy.

## Phase 3: Production readiness

### 3.1 Security and compliance
- Production Entra app registrations and roles. Move all GUIDs and secrets out of `appsettings.json` into configuration or Key Vault.
- Managed identity for Blob Storage, Key Vault and the DB (no connection-string secrets). Add a managed-identity code path to the `BlobServiceClient` registration in `Program.cs`.
- Security headers (HSTS, CSP, `X-Frame-Options` / `frame-ancestors`, Referrer-Policy), a production CORS origin list from config, and a restrictive `AllowedHosts`.
- Rate limiting (`AddRateLimiter`) on upload and `ask` endpoints. The `ask` endpoint is also a cost vector if a paid LLM is used.
- Audit log: who uploaded, viewed, downloaded, linked or deleted (insurance claims usually need this). Never log document contents or tokens.
- Data retention and deletion policy, encryption at rest (default on Azure) and soft-delete or versioning on the blob container.
- Dependency and container vulnerability scanning in CI.

### 3.2 Reliability
- Replace the in-memory `Channel<Guid>` with a **durable queue** (Azure Storage Queue or Service Bus), or at minimum a DB-backed job table. Add retries with back-off, a dead-letter queue and a startup sweep for documents stuck in `Processing`.
- Idempotent processing, so a retried job cannot duplicate chunks (`ReplaceChunksAsync` already replaces).
- Health checks: `/health/live` and `/health/ready` (DB, blob, Ollama or LLM).
- Resilience (timeouts, retries) around Ollama and blob calls.

### 3.3 AI hosting (decision needed)
- Ollama on the developer's `localhost:11434` is not a production setup. Choose one: a self-hosted Ollama on a GPU VM, or a managed service (Azure OpenAI or the Anthropic API). The code already goes through `Microsoft.Extensions.AI`, so swapping is feasible.
- **Embedding size is fixed at 768.** Changing the embedding model needs a migration and a full re-index (CLAUDE.md rule). Decide the production model **before** indexing real data.
- Consider data-residency and privacy requirements for claim documents sent to any third-party model.

### 3.4 Deployment and infrastructure (assumes Azure)
- Dockerfiles for the API and the UI (the UI has an SSR express server, or serve it as a static site).
- Infrastructure as code (Bicep or Terraform): App Service or Container Apps, Azure Database for PostgreSQL Flexible Server with pgvector enabled, Storage Account, Key Vault, Application Insights, and a staging slot or environment.
- CI/CD with GitHub Actions: build, `dotnet test`, `npm test`, `npm run build`, vulnerability scan, then deploy to staging and promote to production. Run EF migrations as a deployment step, not at app startup.
- Environments: dev, staging, prod, each with separate Entra apps and data.
- Backups and tested restore for PostgreSQL and Blob (point-in-time restore, soft-delete).

### 3.5 Observability
- Structured logging (Serilog or OpenTelemetry to Application Insights), request correlation IDs, and metrics for queue depth, processing time and failure rate.
- Alerts on failed-document rate, stuck jobs, 5xx rate and storage cost.

### 3.6 Testing and quality gates
- Unit tests for `FileTypePolicy` (including spoofed extensions and magic-byte mismatches), each extractor (use small fixture files), and the content endpoint (auth, range, `nosniff`, scan-pending).
- API tests following `backend-testing` (WebApplicationFactory; mock `BlobClient`), and all new endpoints added to `EndpointAuthorizationTests`.
- Frontend Vitest tests for the upload component and preview viewer selection.
- A load test for large uploads and concurrent processing before launch.

## Phase 4: Feature candidates (for your input)

Grouped, with a rough value/effort guess. Please strike, add or re-rank. These come from what a claims-document app typically needs, not from your requirements, so treat them as proposals.

**Claims workflow**
- Claim status lifecycle with timeline and activity feed (opened, docs added, assigned, settled).
- Tasks, due dates and SLA reminders. Email or in-app notifications on assignment and new documents.
- Notes and comments on claims and documents, with @mentions.
- Claim-level "ask" (RAG across all documents in a claim, not only one document). **High value, low effort**, since chunks already exist.
- Auto-suggest the matching claim for an unmatched document (match by claim number, policy number or name in the extracted text), which builds on the existing "unmatched" filter.

**Document intelligence**
- OCR for scanned PDFs and images (Azure Document Intelligence or Tesseract). This is a gap in the current pipeline.
- Structured field extraction (claim number, dates, amounts, parties) with review and correction in the UI.
- Document classification (invoice, medical report, police report, correspondence) and auto-tagging.
- Image analysis for damage photos (captioning or damage description). Video transcription via speech-to-text, which makes video searchable.
- Email threading (group `.eml` and `.msg` into conversations) and automatic attachment extraction.
- Duplicate detection via the stored SHA-256, with a "this file already exists on claim X" warning.
- PII detection and redaction.
- Citations in `ask` answers that deep-link to the page or passage in the preview.

**Search and navigation**
- Global search across documents and claims: hybrid (keyword plus vector) with filters by type, date, claim and handler.
- Saved filters and bulk actions (bulk upload, link, tag or delete).
- Document timeline per claim.

**Collaboration and access**
- Read-only share links with expiry for external parties (adjusters, solicitors).
- Document versioning, with a preserved history when a file is replaced.
- Redaction and annotation in the viewer.
- Export a claim bundle (ZIP or merged PDF with an index).

**Intake**
- Email-in: a claims mailbox that auto-ingests `.eml` messages and their attachments into new or existing claims. This is a natural follow-on to Phase 1.
- Mobile-friendly capture for photos in the field.

**Admin and operations**
- Admin dashboard (volumes, processing failures, storage use, AI cost).
- Retention and legal-hold rules, GDPR subject-access and erasure tooling, and a viewable audit trail.
- Re-process and re-index actions for failed or old documents.

## Suggested sequencing

1. **Foundation (1–2 weeks):** `FileTypePolicy`, upload validation fixes, the content/download endpoints with `nosniff`, and the extractor refactor. This unblocks everything else.
2. **Types and preview (3–4 weeks):** `.eml`/`.msg` extraction, image and video support, the PDF, image, video and email viewers, the new upload UI, and the email attachment review step (1.6) with tier 1 signature detection and claim-level duplicate blocking (hashing must be in place from step 1). Tier 2 AI detection follows the AI hosting decision.
3. **Safety gates (1–2 weeks):** malware scanning with quarantine, the durable queue, orphan-blob cleanup, rate limiting and audit log.
4. **Production infrastructure (2 weeks):** the AI hosting decision, containers, IaC, CI/CD, managed identity, health checks and observability.
5. **Staging soak and launch:** load test, security review (use the `security-review` skill on the branch), a restore drill, then production.
6. **Post-launch:** pick from the feature candidates, with claim-level ask, claim suggestion and OCR first.

Estimates are rough and assume one developer. Treat them as relative sizing, not commitments.

## Critical files

- Upload and validation: `src/Api/Controllers/DocumentsController.cs`, `src/Api/Controllers/ClaimsController.cs`, `src/Infrastructure/DocumentService.cs`, `src/Infrastructure/BlobDocumentStorage.cs`
- Extraction: `src/Infrastructure/PdfDocumentTextExtractor.cs` (to generalise), `src/Infrastructure/LocalDocumentProcessingWorker.cs`, `src/Infrastructure/LocalDocumentProcessingQueue.cs`
- Domain: `src/Domain/Document.cs`, `src/Domain/DocumentStatus.cs`, `src/Domain/IDcoumentStorage.cs` (misspelled filename; rename only if touching it anyway)
- Startup and config: `src/Api/Program.cs`, `src/Api/appsettings*.json`
- UI: `src/Ui/src/app/upload-document/`, `document-details/`, `document-list/document.service.ts`, `claims/claim-details/`
- Tests: `tests/…Api.Tests` (`EndpointAuthorizationTests`), `tests/…Infrastructure.Tests`

## Verification (per phase)

- `dotnet build` and `dotnet test AiDocumentIntelligence.sln`; `npm test` and `npm run build` in `src/Ui`.
- Manual end-to-end with `docker-compose up -d` (Postgres and Azurite): upload one sample of each type (pdf, eml, msg, jpg, mp4), confirm status reaches `Completed`, and preview each in the browser.
- Negative cases: a renamed `.exe` as `.pdf`, an oversized file, a zero-byte file, an `.eml` with a `<script>` body, and a user without access to the claim requesting `/content` (expect 403).
- Video seeking works (Range requests return 206).
- Kill the API mid-processing and confirm that, after phase 3, the document recovers.

## Open questions

1. **Hosting target:** this plan assumes Azure (fits Entra and Blob). Is that right?
2. **AI in production:** self-hosted Ollama, Azure OpenAI or another provider? Is claim data allowed to leave your environment?
3. **Video limits:** realistic file sizes and counts per claim? This decides whether direct-to-blob upload is needed in v1.
4. **Email attachments (decided):** the user picks which attachments to import, with preview and automatic signature flagging (1.6). Attachments already in the claim (same hash) are flagged and cannot be imported again. Still to confirm: should unselected attachments be importable later, may signature-image detection send images to an external model, and should linking an existing document that duplicates one already on the claim be blocked or only warned?
5. **Compliance:** any regulatory requirements (GDPR, FCA, retention periods) that affect the audit log and deletion design?
6. **Expected scale:** users, claims per month and documents per claim, for sizing the queue and database.
7. **Feature priorities:** which of the Phase 4 candidates matter most to you, and what is missing?
