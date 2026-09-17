# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a cloud-native document processing platform built with:
- **Backend**: .NET 9 (C#) with Entity Framework Core
- **Frontend**: Angular 21 with Tailwind CSS
- **Database**: PostgreSQL 16
- **Storage**: Azure Blob Storage (via Azurite in development)
- **Vector/AI**: Azure OpenAI integration for RAG workflows

The project uses Clean Architecture principles with three main .NET layers:
- `AiDocumentIntelligence.Domain` - Core business logic and entities
- `AiDocumentIntelligence.Api` - ASP.NET Core REST API (port 5000/5001)
- `AiDocumentIntelligence.Infrastructure` - Data access, external services, EF Core migrations
- `Ui` - Angular SPA (port 4200)

## Common Development Commands

### Backend (.NET)

**Build the solution:**
```bash
cd /Users/nathanadams/Dev/ai-document-intelligence
dotnet build AiDocumentIntelligence.sln
```

**Run the API:**
```bash
dotnet run --project src/Api/AiDocumentIntelligence.Api.csproj
```
The API will start on `https://localhost:5001` (or `http://localhost:5000` for HTTP).

**Run tests:**
```bash
dotnet test AiDocumentIntelligence.sln
```

**Entity Framework Core migrations:**
```bash
# Create a new migration (run from repo root or src/Infrastructure directory)
dotnet ef migrations add MigrationName --project src/Infrastructure --startup-project src/Api

# Apply pending migrations to the database
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

### Frontend (Angular)

**Navigate to UI directory:**
```bash
cd src/Ui
```

**Install dependencies:**
```bash
npm install
```

**Start development server:**
```bash
npm start
```
The UI will be available at `http://localhost:4200` and hot-reloads on file changes.

**Run tests:**
```bash
npm test
```

**Build for production:**
```bash
npm run build
```

### Full-Stack Development

**Start services (PostgreSQL + Azurite):**
```bash
docker-compose up -d
```
- PostgreSQL: localhost:5432 (user: `aidocint`, password: `devpassword`)
- Azurite (Azure Storage emulator): localhost:10000-10002

**Stop services:**
```bash
docker-compose down
```

## Project Structure

```
src/
├── Api/                          # ASP.NET Core REST API
│   ├── Controllers/              # API endpoints (DocumentsController, WeatherForecastController)
│   ├── Program.cs                # DI configuration, middleware setup
│   ├── appsettings.json          # Production configuration
│   └── appsettings.Development.json  # Development overrides (Azure Storage, logging)
├── Domain/                       # Core business logic and entities
│   ├── Document.cs               # Main document entity
│   ├── DocumentStatus.cs         # Document status enum
│   ├── UploadDocumentRequest.cs  # API request contract
│   └── IDcoumentStorage.cs       # Storage interface
├── Infrastructure/               # Data access and external services
│   ├── AppDbContext.cs           # EF Core DbContext
│   ├── BlobDocumentStorage.cs    # Azure Blob Storage implementation
│   ├── DocumentService.cs        # Business logic orchestration
│   ├── DocumentRepository.cs     # Data access layer
│   ├── AzureStorageOptions.cs    # Azure configuration
│   └── Migrations/               # EF Core migrations (PostgreSQL)
└── Ui/                           # Angular 21 SPA
    ├── src/
    │   ├── app/                  # Components and services
    │   │   ├── app.ts            # Root component
    │   │   ├── app.routes.ts      # Route definitions
    │   │   └── upload-document/   # Feature module for document upload
    │   ├── main.ts               # Angular bootstrap
    │   └── styles.css            # Global Tailwind styles
    ├── angular.json              # Angular build configuration
    ├── package.json              # Dependencies (Vitest for unit tests)
    └── tsconfig.json             # TypeScript configuration
```

## Architectural Patterns

### Dependency Injection
The API uses ASP.NET Core's built-in DI container (configured in `Program.cs`):
- `IDocumentStorage` - Blob storage abstraction (implemented by `BlobDocumentStorage`)
- `IDocumentRepository` - Data repository pattern for PostgreSQL
- `DocumentService` - Orchestrates storage + database operations
- `BlobServiceClient` - Singleton for Azure Blob Storage

### Database Access
- **ORM**: Entity Framework Core 9.0 with PostgreSQL
- **Migrations**: Located in `src/Infrastructure/Migrations/`
- **Connection string**: Configured in `appsettings.json` (development uses localhost:5432)
- **DbContext**: `AppDbContext` with `DbSet<Document>`

### Frontend State Management
- Angular signals for reactive state (used in components)
- HTTP client for API communication
- Feature-based folder structure (e.g., `upload-document/`)

### Azure Integration
- **Blob Storage**: Configured via `AzureStorageOptions` in `appsettings.Development.json`
- **Development mode**: Uses Azurite emulator (`UseDevelopmentStorage=true`)
- **Production mode**: Uses real Azure Storage connection string (via configuration)
- **OpenAI**: Azure OpenAI integration for document processing and RAG

### CORS Configuration
API allows requests from Angular dev server (`http://localhost:4200`) in development mode only.

## Database Setup

### Initial Setup
1. Start PostgreSQL: `docker-compose up -d postgres`
2. Apply migrations: `dotnet ef database update --project src/Infrastructure --startup-project src/Api`

### Existing Migrations
- `20260829125915_InitialCreate.cs` - Initial schema with Documents table

### Adding New Entities
1. Create entity class in `src/Domain/`
2. Add `DbSet<Entity>` to `AppDbContext`
3. Create migration: `dotnet ef migrations add EntityName`
4. Review and apply: `dotnet ef database update`

## Angular/TypeScript Specifics

- **Version**: Angular 21.2.0 with latest TypeScript 5.9
- **Build tool**: Angular CLI with modern standalone components
- **Styling**: Tailwind CSS 4.1 with PostCSS
- **Testing**: Vitest with jsdom
- **Package manager**: npm 11.6.2
- **Server**: Angular SSR (Server-Side Rendering) configured via Express

## Configuration Files

### Backend
- `AiDocumentIntelligence.sln` - Solution file
- `.csproj` files - Project definitions with NuGet package references
- `appsettings.json` - Default configuration (production database connection)
- `appsettings.Development.json` - Dev overrides (Azurite storage, logging levels)

### Frontend
- `angular.json` - Build and serve configuration
- `package.json` - npm dependencies and scripts
- `tsconfig.json` - TypeScript compiler options
- `tailwind.config.js` - Tailwind CSS configuration (if present)

## Key Dependencies

### Backend
- **AspNetCore.OpenApi** - OpenAPI (Swagger) support
- **EntityFrameworkCore** - ORM
- **Npgsql.EntityFrameworkCore.PostgreSQL** - PostgreSQL provider
- **Azure.Storage.Blobs** - Azure Blob Storage SDK

### Frontend
- **@angular/core, @angular/common, @angular/router** - Core Angular
- **@angular/forms** - Reactive and template-driven forms
- **rxjs** - Reactive programming
- **tailwindcss** - Utility-first CSS

## Debugging

### Backend Debugging
VS Code launch configuration is set up in `.vscode/launch.json` for Chrome debugging of the Angular app.

To debug the .NET API:
1. Run with debug configuration in Visual Studio or use `dotnet run` with debugger attached
2. Set breakpoints in C# code
3. API will restart and hit your breakpoints

### Frontend Debugging
1. Use Chrome DevTools (F12) when running `npm start`
2. Set breakpoints in TypeScript source files (visible in DevTools Sources)
3. Angular DevTools browser extension is helpful

## Important Notes

- **Signals**: Angular components use signals for reactive state instead of traditional RxJS subscriptions
- **Zoneless Angular**: The application is configured to work with Angular's zoneless mode for better performance
- **Azure Storage**: Development uses local Azurite emulator; production must configure real Azure Storage connection string
- **Database**: Ensure PostgreSQL is running via Docker before starting the API
- **Port conflicts**: API uses 5000/5001 (HTTPS), UI uses 4200 - ensure these are available
