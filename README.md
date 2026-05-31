<div align="center">

<img src="site/og-image.svg" alt="SkillKite" width="640"/>

# SkillKite

**Right skills. Higher reach.** 🪁
*A free AI career coach on WhatsApp for Tier 2/3 India.*

[![CI](https://github.com/akkyyakhilesh/SkillKite/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/akkyyakhilesh/SkillKite/actions/workflows/ci.yml)
[![Live site](https://img.shields.io/badge/site-skillkite.in-FF7A00?style=flat-square)](https://skillkite.pages.dev)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Claude](https://img.shields.io/badge/Claude-Sonnet-D4A373?style=flat-square)](https://www.anthropic.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat-square&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

</div>

---

## What it does

You message a WhatsApp number. The bot asks ~10 questions in Hinglish — your education, skills, whether you have a phone or a laptop, what salary would make your family proud. In about 5 minutes, you get back a **personalized 12–24 week career roadmap** with free YouTube + NPTEL resources, delivered as a **bilingual PDF** in the same chat.

Built for BCA, MCA, B.Tech, and M.Tech students in places like Bhagalpur, Purnea, Muzaffarpur, Darbhanga — where career counselors charge ₹5,000+ and metro mentors don't reach.

## Why it exists

90% of India's students live outside major metros. They have the same degrees as their Bangalore peers but wildly different access to career guidance, mentorship, and exposure. SkillKite is a single founder's attempt to close that gap with the one thing every student already has: a WhatsApp chat window.

Long-form thesis available on request — DM me on [LinkedIn](https://www.linkedin.com/in/akkyyakhilesh/) or [open an issue](https://github.com/akkyyakhilesh/SkillKite/issues).

## Try it

- 🪁 **Live:** [skillkite.pages.dev](https://skillkite.pages.dev) → tap "Chat on WhatsApp"
- 🌐 **Custom domain:** `skillkite.in` (DNS propagation in progress)
- 💬 The WhatsApp test number only replies to whitelisted recipients during the Meta sandbox phase. DM me to be added.

## Architecture

```
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────┐
│  WhatsApp       │ →   │  .NET 8 Web API      │ →   │  Claude     │
│  Cloud API      │     │  (clean architecture) │     │  Sonnet     │
└─────────────────┘     │                       │     └─────────────┘
                        │  ┌─────────────────┐ │
┌─────────────────┐     │  │ Orchestrator   │ │     ┌─────────────┐
│  Angular PWA    │ →   │  │ Career Engine  │ │ →   │ PostgreSQL  │
│  (Phase 2)      │     │  │ PDF Generator  │ │     │ (jsonb)     │
└─────────────────┘     │  └─────────────────┘ │     └─────────────┘
                        └──────────────────────┘
```

Clean architecture, 5 projects:

| Project | Purpose |
|---|---|
| `SkillKite.Core` | Domain models, enums, DTOs, interfaces — zero infrastructure dependencies |
| `SkillKite.Data` | `AppDbContext`, EF Core migrations, career-path seed |
| `SkillKite.Infrastructure` | `ClaudeCareerEngine`, `WhatsAppService`, `RoadmapPdfGenerator`, `AssessmentOrchestrator`, `CareerPathRepository` |
| `SkillKite.API` | ASP.NET Core controllers (Webhook, Chat, Roadmap, Progress, Careers, Health) + signature-validation middleware |
| `SkillKite.Tests` | xUnit — payload parsing, HMAC verification |

The API is **frontend-agnostic**: WhatsApp today, Angular PWA tomorrow (Phase 2), Capacitor Android app (Phase 3) — same orchestrator, no rewrite.

## Tech stack

| Layer | Choice | Why |
|---|---|---|
| Web framework | **.NET 8** | Strong typing + perf, fits founder's stack experience |
| AI engine | **Claude Sonnet** (Anthropic) | Best Hinglish reasoning + JSON output reliability |
| Database | **PostgreSQL** + Npgsql | `jsonb` for flexible assessment data; cheap to host |
| Messaging | **WhatsApp Cloud API** | Zero-friction reach for Tier 2/3 users |
| PDF | **QuestPDF** + Noto Sans Devanagari | Clean bilingual rendering |
| Tunnel (dev) | **Cloudflare Tunnel** | Permanent `bot.skillkite.in` URL, no laptop port forwarding |
| Landing | Vanilla HTML/CSS/JS on **Cloudflare Pages** | Sub-500 ms load on 3G; no framework bloat |
| Analytics | **Cloudflare Web Analytics** | Privacy-first, zero JS overhead |

## Endpoints (Phase 1)

```http
GET    /api/healthz                  Liveness + DB probe
GET    /api/stats                    Aggregate counts (no PII)
GET    /api/careers                  List 27 curated career paths
GET    /api/careers/{id}             Career path detail with resources

POST   /api/chat/start               Begin an assessment session
POST   /api/chat/message             Send a message, get the reply
GET    /api/chat/session/{id}        Inspect session history

POST   /api/roadmaps/generate        Generate a roadmap for a session
GET    /api/roadmaps/{id}            Roadmap detail
GET    /api/roadmaps/{id}/pdf        Download the PDF
GET    /api/roadmaps/by-phone/{p}    All roadmaps for a student

POST   /api/progress/{roadmapId}     Log weekly progress
GET    /api/progress/{roadmapId}     Progress history

GET    /api/webhook/whatsapp         Meta verification handshake
POST   /api/webhook/whatsapp         Receive WhatsApp messages
```

## Local setup

See [`RUNNING.md`](RUNNING.md) for the full step-by-step. Short version:

```bash
# 1. secrets
cd src/SkillKite.API
dotnet user-secrets set "Claude:ApiKey" "sk-ant-..."
dotnet user-secrets set "WhatsApp:PhoneNumberId" "..."
dotnet user-secrets set "WhatsApp:AccessToken" "..."
dotnet user-secrets set "WhatsApp:AppSecret" "..."
dotnet user-secrets set "WhatsApp:VerifyToken" "your_random_string"

# 2. Postgres
docker run -d --name skillkite-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16

# 3. Run (auto-applies migrations, seeds 27 career paths)
dotnet run --project src/SkillKite.API

# 4. Verify
curl http://localhost:5007/api/healthz
```

## Repository layout

```
SkillKite/
├── src/
│   ├── SkillKite.API/              ASP.NET Core + controllers + middleware
│   ├── SkillKite.Core/             Models, enums, interfaces, DTOs
│   ├── SkillKite.Data/             EF Core context + migrations + seed
│   └── SkillKite.Infrastructure/   Claude engine, WhatsApp service, PDF generator, orchestrator
├── tests/
│   └── SkillKite.Tests/            xUnit tests
├── site/                           Vanilla HTML landing page (Cloudflare Pages)
├── content/                        Marketing drafts (LinkedIn posts, etc.)
├── tools/                          Local-only binaries (cloudflared.exe — gitignored)
├── README.md                       This file
├── RUNNING.md                      Day-to-day dev workflow
└── SkillKite.sln
```

## Roadmap

| Phase | Status | What |
|---|---|---|
| **1. WhatsApp MVP** | ✅ Shipped | Bot, Claude engine, 27 careers, bilingual PDFs |
| **2. Angular PWA** | 🟡 Planned | Installable web app, progress dashboard, offline cache |
| **3. Android via Capacitor** | ⏳ Future | Native push notifications, Play Store listing |
| **4. Monetization** | ⏳ Future | Premium tier (mock interviews, resume builder), college B2B |
| **5. Scale** | ⏳ Future | Regional languages, mentor matching, job board |


## Contributing

This is currently a solo project being built in public. Contributions and ideas are welcome — open an issue first to discuss anything substantial. If you're a BCA / MCA / B.Tech / M.Tech student and want to **test the bot**, please open an issue with your WhatsApp number (international format) and I'll whitelist you in the Meta sandbox.

## License

MIT — see [`LICENSE`](LICENSE).

## Author

**Akhilesh Kumar** ([@akkyyakhilesh](https://github.com/akkyyakhilesh)) — building SkillKite alongside a full-time job, one weekend at a time.

If this resonates and you know a small-city student who's stuck on *"what do I do after my degree?"* — please share the link with them. That's the entire point.

🪁 *Right skills. Higher reach.*
