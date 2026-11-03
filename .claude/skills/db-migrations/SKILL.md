---
name: db-migrations
description: EF Core and PostgreSQL schema work for this repo: creating/applying migrations, adding entities, first-time database setup, and the list of existing migrations. Use when changing entities, AppDbContext, or the database schema.
---

# Database migrations

## Initial setup
1. Start PostgreSQL: `docker-compose up -d postgres`
2. Apply migrations: `dotnet ef database update --project src/Infrastructure --startup-project src/Api`

## Create / apply a migration
```bash
dotnet ef migrations add MigrationName --project src/Infrastructure --startup-project src/Api
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```
Migrations live in `src/Infrastructure/Migrations/`. Review the generated migration before applying.

## Adding a new entity
1. Create the entity class in `src/Domain/`.
2. Add `DbSet<Entity>` to `AppDbContext` (and an `IEntityTypeConfiguration` in `src/Infrastructure/` if mapping is non-trivial, as with `ClaimConfiguration.cs`).
3. Create the migration (command above).
4. Review and apply.

## Existing migrations
- `20260829125915_InitialCreate.cs`: initial schema with Documents table.
- `20261001202016_AddDocumentChunks.cs`: enables the `vector` extension and adds `DocumentChunks` (text + 768-dim embedding, HNSW cosine index).
- `20261002153227_AddClaims.cs`: adds `Claims` (unique index on `Reference`) and the nullable `Documents.ClaimId` foreign key.

- `20261015105529_AddClaimOwnership.cs`: adds `Users` (Id = Entra object id), `Claims.AssignedToUserId`/`CreatedByUserId` (FKs to `Users`, SetNull), `Claims.IsPrivate`, and `Documents.UploadedByUserId`. The legacy `Claims.AssignedTo` string stays until the assignment endpoints switch to users.
- `20261015112551_AddHandlerFlagAndRemoveLegacyAssignee.cs`: adds `Users.IsHandler` and **drops** the legacy free-text `Claims.AssignedTo` (its values were never mapped to users, so they are discarded).
- `20261016050617_LinkDocumentsToManyClaims.cs`: adds the `ClaimDocuments` join table (composite key, FK to Claims `Restrict`, to Documents `Cascade`, to Users `SetNull`), copies every existing `Documents.ClaimId` into it, then drops `Documents.ClaimId`. `Down` restores one claim per document (the earliest link). The scaffold dropped the column first; it was reordered by hand, so keep that order if regenerated.
- `AddDocumentContentHash`: adds nullable `Documents.ContentHash` (varchar(64), lowercase hex SHA-256 of the uploaded bytes) with a **non-unique** index. Existing documents stay null until backfilled.

## Notes
- Connection string is in `appsettings.json` (development uses localhost:5432; user `aidocint`, password `devpassword` for the local Docker container only).
- Changing the embedding dimensions needs a migration and a re-index (see `rag-pipeline`).
