<div align="center">

<img src="site/og-image.svg" alt="SkillKite" width="640"/>

# SkillKite

**Right skills. Higher reach.** 🪁
*A free AI career coach on WhatsApp for Tier 2/3 India.*

[![CI](https://github.com/akkyyakhilesh/SkillKite/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/akkyyakhilesh/SkillKite/actions/workflows/ci.yml)
[![Live site](https://img.shields.io/badge/site-skillkite.in-FF7A00?style=flat-square)](https://skillkite.in)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Claude](https://img.shields.io/badge/Claude-Sonnet-D4A373?style=flat-square)](https://www.anthropic.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?style=flat-square&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

</div>

---

## What it does

You message a WhatsApp number. The bot asks where you are in your journey and routes you to one of four flows:

| Flow | For | Output |
|---|---|---|
| **📚 After 10th** | Just finished class 10 | Stream selection guide — Science (PCM/PCB), Commerce, Arts, polytechnic & paramedical options |
| **🎯 After 12th** | Just finished class 12 | Stream-specific guide (B.Tech / MBBS / CA / BA LLB / etc.) with entrance exams + realistic salary bands |
| **💼 After Graduation** | Degree done / final year | 27 career paths across Tech, Creative, Government, Gig, Trades & Emerging fields with salary bands, timelines, and personalized roadmaps |
| **🌱 Skill Upgrade** | Already working | Field-specific guides for Software/IT, Data, Design, Marketing, Finance, Healthcare, Teaching & Ops |

Everything is in Hinglish, tappable buttons over typing, and delivered on WhatsApp. The website at [skillkite.in](https://skillkite.in) lets students browse all options directly.

Built for students in places like Bhagalpur, Purnea, Muzaffarpur, Darbhanga — where career counselors charge ₹5,000+ and metro mentors don't reach.

## Why it exists

90% of India's students live outside major metros. They have the same degrees as their Bangalore peers but wildly different access to career guidance, mentorship, and exposure. SkillKite is a single founder's attempt to close that gap with the one thing every student already has: a WhatsApp chat window.

Long-form thesis available on request — DM me on [LinkedIn](https://www.linkedin.com/in/akkyyakhilesh/) or [open an issue](https://github.com/akkyyakhilesh/SkillKite/issues).

## Try it

- 📱 **WhatsApp the bot:** **+91 62012 26351** — no signup, no whitelist, no early-access gate
- 🪁 **Website:** [skillkite.in](https://skillkite.in) — browse all career paths, stream guides, and skill-upgrade options
- 🌐 **API:** [bot.skillkite.in/api/healthz](https://bot.skillkite.in/api/healthz)

## Architecture

```
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────┐
│  WhatsApp       │ →   │  .NET 8 Web API      │ →   │  Claude     │
│  Cloud API      │     │  (clean architecture) │     │  Sonnet     │
└─────────────────┘     │                       │     └─────────────┘
                        │  ┌─────────────────┐ │
┌─────────────────┐     │  │ Orchestrator   │ │     ┌─────────────┐
│  Astro SSG      │     │  │ Career Engine  │ │ →   │ PostgreSQL  │
│  (skillkite.in) │     │  │ PDF Generator  │ │     │ (jsonb)     │
└─────────────────┘     │  └─────────────────┘ │     └─────────────┘
                        └──────────────────────┘
```

**Backend** — clean architecture, 5 projects:

| Project | Purpose |
|---|---|
| `SkillKite.Core` | Domain models, enums, DTOs, interfaces — zero infrastructure dependencies |
| `SkillKite.Data` | `AppDbContext`, EF Core migrations, career-path seed |
| `SkillKite.Infrastructure` | `ClaudeCareerEngine`, `WhatsAppService`, `RoadmapPdfGenerator`, `AssessmentOrchestrator`, `CareerPathRepository` |
| `SkillKite.API` | ASP.NET Core controllers (Webhook, Chat, Roadmap, Progress, Careers, Health) + signature-validation middleware |
| `SkillKite.Tests` | xUnit — payload parsing, HMAC verification |

**Frontend** — Astro static site (`web/`):

| Area | What |
|---|---|
| Pages | Homepage, About, After 10th (4 streams), After 12th (4 streams), Graduation (27 careers), Skill Upgrade (8 fields), Privacy, Terms, 404 |
| SEO | JSON-LD (Organization, BreadcrumbList, FAQPage, WebSite), OG/Twitter cards, canonical URLs, sitemap |
| Design | Dark navy + saffron palette, mobile-first, Noto Sans + Noto Sans Devanagari |
| Deploy | Firebase Hosting via GitHub Actions (auto-deploy on push to main) |

## Tech stack

| Layer | Choice | Why |
|---|---|---|
| Web framework | **.NET 8** | Strong typing + perf, fits founder's stack experience |
| AI engine | **Claude Sonnet** (Anthropic) | Best Hinglish reasoning + JSON output reliability |
| Database | **PostgreSQL** + Npgsql | `jsonb` for flexible assessment data; cheap to host |
| Messaging | **WhatsApp Cloud API** | Zero-friction reach for Tier 2/3 users |
| PDF | **QuestPDF** + Noto Sans Devanagari | Clean bilingual rendering |
| Website | **Astro** (SSG) | Static HTML, zero JS by default, fast on 3G |
| Tunnel (dev) | **Cloudflare Tunnel** | Permanent `bot.skillkite.in` URL, no laptop port forwarding |
| Hosting | **Firebase Hosting** | Auto-deploy via GitHub Actions on push to main |
| Analytics | **Cloudflare Web Analytics** | Privacy-first, zero JS overhead |

## Endpoints

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

# 3. Run the API (auto-applies migrations, seeds 27 career paths)
dotnet run --project src/SkillKite.API

# 4. Run the website
cd web && npm install && npm run dev

# 5. Verify
curl http://localhost:5007/api/healthz    # API
# http://localhost:4321                    # Website
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
├── web/                            Astro static site (skillkite.in)
│   ├── src/pages/                  All page routes (index, about, category pickers, career/stream details)
│   ├── src/layouts/                Base layout with nav, footer, OG tags
│   ├── src/components/             Breadcrumbs, GuideView
│   ├── src/config/                 Categories, streams, career groupings, WhatsApp config
│   ├── src/lib/                    JSON-LD schema builders
│   ├── src/styles/                 Global CSS (dark navy + saffron design system)
│   └── public/                     Static assets (favicon, OG image, founder photo)
├── site/                           Legacy vanilla HTML landing (superseded by web/)
├── content/                        Marketing drafts (LinkedIn posts, etc.)
├── tools/                          Local-only binaries (cloudflared.exe — gitignored)
├── README.md                       This file
├── RUNNING.md                      Day-to-day dev workflow
└── SkillKite.sln
```

## Roadmap

| Phase | Status | What |
|---|---|---|
| **1. WhatsApp MVP (career roadmap)** | ✅ Shipped | Bot, Claude engine, 27 careers, bilingual PDFs |
| **1.5. 10th + 12th student flows** | ✅ Shipped | 4-way entry split, stream/course selection guides |
| **1.6. Astro website** | ✅ Shipped | Full browsable site with all categories, career details, about page, SEO |
| **2. Skill upgrade flow** | ✅ Shipped | 8-field skill-upgrade guides for working professionals |
| **3. Angular PWA** | ⏳ Planned | Installable web app, progress dashboard, offline cache |
| **4. Android via Capacitor** | ⏳ Future | Native push notifications, Play Store listing |
| **5. Monetization** | ⏳ Future | Premium tier (mock interviews, resume builder), college B2B |
| **6. Scale** | ⏳ Future | Regional languages, mentor matching, job board |


## Contributing

This is currently a solo project being built in public. Contributions and ideas are welcome — open an issue first to discuss anything substantial. If you're a student or you know one stuck on *"what do I do next?"* — just WhatsApp **+91 62012 26351** and try the bot. No allowlist any more.

## License

MIT — see [`LICENSE`](LICENSE).

## Author

**Akhilesh Kumar** ([@akkyyakhilesh](https://github.com/akkyyakhilesh)) — building SkillKite alongside a full-time job, one weekend at a time.

If this resonates and you know a small-city student who's stuck on *"what do I do after my degree?"* — please share the link with them. That's the entire point.

🪁 *Right skills. Higher reach.*
