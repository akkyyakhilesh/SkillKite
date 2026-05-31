# SkillKite landing page

Single-page, mobile-first, vanilla HTML/CSS/JS landing for skillkite.in.

## Files

| File | What it does |
|---|---|
| `index.html` | Markup, all content in EN + HI side by side, hidden via CSS by language toggle |
| `styles.css` | Saffron + sky-blue palette, mobile-first responsive layout, no framework |
| `app.js` | Language toggle (persists in localStorage) + WhatsApp deep-link wiring |
| `favicon.svg` | Inline SVG kite icon |

Zero JS framework, zero build step. Open `index.html` in a browser, it just works.

## Configure the WhatsApp CTA

Open `app.js`, set:

```js
var BOT_PHONE = '15550123456'; // your Meta test number, digits only, no +
```

The number is shown on Meta → WhatsApp → API Setup under "From".

Save → reload → the "Chat on WhatsApp" buttons now deep-link to `wa.me/<number>?text=Hi%20SkillKite!`.

## Preview locally

```powershell
cd A:\Github\SkillKite\site
python -m http.server 8000
# or
npx serve .
```
Then open http://localhost:8000.

## Deploy to Cloudflare Pages (free, ~5 min)

1. Push to GitHub (already done — repo is `akkyyakhilesh/SkillKite`).
2. Sign in to https://dash.cloudflare.com → **Workers & Pages** → **Create application** → **Pages** → **Connect to Git**.
3. Pick the `SkillKite` repo.
4. Build settings:
   - **Framework preset:** None
   - **Build command:** *(leave empty)*
   - **Build output directory:** `site`
5. Deploy. You get a `*.pages.dev` URL within a minute.
6. Add custom domain → `skillkite.in` (Cloudflare walks you through DNS).

## Customize

- Hero copy: edit the `<h1>` and `.lede` paragraphs in `index.html`.
- Add testimonials section: copy a `<section class="section">` block, swap content.
- Logo: replace the inline `<svg class="logo-kite">` and `favicon.svg` with your designer's version when ready.
- Track engagement: drop a script tag for Plausible or Cloudflare Web Analytics in `<head>`.
