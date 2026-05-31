# SkillKite — Phase 1 MVP (WhatsApp Bot)

**"Apne hunar ki patang udao"** — A Claude-powered AI career coach for students in Tier 2/3 India, delivered over WhatsApp.

## Architecture

Clean architecture, 4 projects:

| Project | Purpose |
|---|---|
| `SkillKite.Core` | Domain models, enums, DTOs, interfaces (`ICareerEngine`, `IMessagingService`, `IRoadmapPdfGenerator`) |
| `SkillKite.Data` | EF Core + Npgsql `AppDbContext` and entity configuration |
| `SkillKite.Infrastructure` | `ClaudeCareerEngine`, `WhatsAppService`, `RoadmapPdfGenerator` (QuestPDF), `AssessmentOrchestrator` |
| `SkillKite.API` | ASP.NET Core Web API. `WebhookController` (WhatsApp) and `ChatController` (web/PWA) |

The .NET API is **frontend-agnostic** — same orchestrator powers WhatsApp today and Angular PWA in Phase 2.

## Career assessment flow

1. Student sends any message to the WhatsApp number.
2. `WebhookController` verifies the Meta signature, parses the message, and hands it to `AssessmentOrchestrator`.
3. Orchestrator upserts the `Student`, finds/creates an active `ChatSession`, and calls `ClaudeCareerEngine.NextTurnAsync`.
4. Claude (Sonnet 4.6) returns JSON: `{ reply, extracted, complete }`. The orchestrator merges extracted fields into the session's `AssessmentDataJson`.
5. The reply is sent back via WhatsApp Cloud API.
6. When `complete: true`, `GenerateRoadmapAsync` produces a structured 12–24 week roadmap. `RoadmapPdfGenerator` writes a PDF to `wwwroot/roadmaps/`, and the orchestrator sends the URL as a WhatsApp document.

The anchor questions live in [`AssessmentQuestions.cs`](src/SkillKite.Infrastructure/AI/AssessmentQuestions.cs) — Claude rephrases them naturally in Hinglish.

## Setup

### 1. Configure secrets

```bash
cd src/SkillKite.API
dotnet user-secrets init
dotnet user-secrets set "Claude:ApiKey" "sk-ant-..."
dotnet user-secrets set "WhatsApp:PhoneNumberId" "..."
dotnet user-secrets set "WhatsApp:AccessToken" "..."
dotnet user-secrets set "WhatsApp:AppSecret" "..."
dotnet user-secrets set "WhatsApp:VerifyToken" "your_verify_token"
```

Or edit `appsettings.Development.json`.

### 2. Database

Start Postgres locally (Docker example):

```bash
docker run -d --name skillkite-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16
```

Create the initial migration and apply it:

```bash
cd src/SkillKite.API
dotnet ef migrations add InitialCreate -p ../SkillKite.Data -s .
dotnet ef database update -p ../SkillKite.Data -s .
```

### 3. Run

```bash
dotnet run --project src/SkillKite.API
```

Swagger UI: `https://localhost:7000/swagger`.

### 4. Local WhatsApp testing without Meta

Use the `ChatController` to simulate WhatsApp messages:

```bash
curl -X POST https://localhost:7000/api/chat/message \
  -H "Content-Type: application/json" \
  -d '{"phone":"919999999999","text":"Hi","name":"Test"}'
```

The orchestrator will run the same engine; replies go through `WhatsAppService.SendTextAsync` (which will no-op fail without credentials — wrap or stub for pure offline testing).

### 5. Connect to WhatsApp Cloud API

- Webhook URL: `https://<your-domain>/api/webhook/whatsapp`
- Verify token: whatever you set in `WhatsApp:VerifyToken`
- Subscribe to `messages` field.

## Phase 2 (Angular PWA)

The Angular client will call `POST /api/chat/message` directly — no code changes needed on the server. Add JWT auth in `Program.cs` before exposing publicly.
