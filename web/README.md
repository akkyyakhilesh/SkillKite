# SkillKite Website

Astro static site for [skillkite.in](https://skillkite.in) — the browsable companion to the WhatsApp bot.

## Pages

| Route | What |
|---|---|
| `/` | Homepage — hero, 4 category cards, "Why SkillKite" feature grid |
| `/about` | About page — problem statement, how it works, founder story, contact |
| `/after-10th` | Stream picker (Science, Commerce, Arts, Not sure) |
| `/after-10th/[interest]` | Stream detail with FAQ-style guide |
| `/after-12th` | Stream picker (PCM, PCB, Commerce, Arts) |
| `/after-12th/[stream]` | Stream detail with courses and entrance exams |
| `/after-graduation` | 27 career paths grouped by category |
| `/after-graduation/[career]` | Career detail — salary, demand, timeline, roadmap CTA |
| `/skill-upgrade` | 8-field picker (Software, Data, Design, Marketing, etc.) |
| `/skill-upgrade/[field]` | Field detail with skill-up guide |
| `/privacy`, `/terms` | Legal pages |
| `/admin` | Password-protected stats dashboard |

## Design system

- **Palette:** Dark navy (`#0F1834`), saffron accent (`#FF8A3D`), WhatsApp green (`#25D366`)
- **Typography:** Noto Sans (Latin) + Noto Sans Devanagari (Hindi), weight 900 headings
- **Layout:** max-width 760px content, 38px padding desktop, 22px mobile, breakpoint 640px
- **Cards:** `rgba(255,255,255,0.05)` bg, `rgba(255,255,255,0.1)` border, saffron hover glow

## SEO

- JSON-LD schemas: Organization, WebSite, BreadcrumbList, FAQPage (via `src/lib/seo.ts`)
- OG and Twitter cards on every page (via Base layout)
- Canonical URLs and sitemap generation

## Commands

All commands run from `web/`:

| Command | Action |
|---|---|
| `npm install` | Install dependencies |
| `npm run dev` | Dev server at `localhost:4321` |
| `npm run build` | Production build to `dist/` |
| `npm run preview` | Preview production build locally |

## Deploy

Deployed to Firebase Hosting via GitHub Actions — auto-deploys on push to `main`.
