# LinkedIn Post #1 — Phase 1 ship

**Status:** Ready to post when you are. Tweak the tone if it doesn't sound like you.

**Suggested time to post:** Weekday evening 7–9 PM IST (highest LinkedIn engagement window in India).

**Image to attach:** `site/og-image.png` (1200×630, the kite preview card) — LinkedIn shows it big.

---

## Draft A — personal-first, ~1700 chars

I'm Akhilesh — grew up in a small town in Bihar, did B.Tech in Computer Science, now working as a developer in Bangalore.

Every year I watch BCA, MCA, B.Tech, M.Tech students from places like Bhagalpur, Purnea, Muzaffarpur graduate with no clue what jobs actually exist beyond the local IT center or a govt exam.

Metro students get LinkedIn, mentors, bootcamps. Tier 2/3 students get cousin's advice.

So I'm building SkillKite — a free AI career coach on WhatsApp, in Hindi + English.

Today I shipped Phase 1. 🪁

You send "Hi" to a WhatsApp number. The bot asks ~10 questions in Hinglish — your education, skills, whether you have a laptop or only a phone, what salary would make your family proud. In ~5 minutes it sends back a personalized 12–24 week career roadmap with free YouTube + NPTEL resources, delivered as a bilingual PDF in the same chat.

What's running underneath:
→ .NET 8 Web API
→ Claude Sonnet as the conversational + roadmap engine
→ PostgreSQL for student / session / roadmap state
→ WhatsApp Cloud API for the chat
→ QuestPDF with Noto Sans Devanagari for the bilingual PDF
→ 27 curated career paths (tech, govt, creative, gig, trades, emerging) with realistic Tier 2/3 salary ranges

Why WhatsApp first, not an app: my target user has a ₹10,000 phone, intermittent 4G, and 3+ hours daily on WhatsApp. Installing an app is friction. Texting a number is zero.

Why free forever: career counseling shouldn't be a paywall for the people who need it most. Eventually funded by YouTube ad revenue, college partnerships, and CSR — not student wallets.

Try it (very early — be kind): skillkite.in
Code: github.com/akkyyakhilesh/SkillKite (open source, built in public)

If you know a BCA / MCA / B.Tech / M.Tech student stuck on "what do I do after graduation?" — please share this with them. Their feedback will shape week 2.

🪁 Apne hunar ki patang udao.

#buildinpublic #aiforindia #careerguidance #edtech #bihar

---

## Draft B — shorter, ~900 chars (if Draft A feels too long)

Today I shipped Phase 1 of SkillKite — a free AI career coach on WhatsApp for Tier 2/3 India.

You send "Hi". The bot chats with you in Hinglish for ~5 minutes — your degree, skills, phone vs. laptop, family expectations. Then it sends back a 12–24 week personalized career roadmap as a bilingual PDF.

Stack: .NET 8 + Claude Sonnet + WhatsApp Cloud API + PostgreSQL + QuestPDF.

Why I'm building this: I grew up in a small town in Bihar. Every BCA/MCA student I knew there had the same problem — no idea what jobs exist beyond the local IT center or a govt exam. Metro students get LinkedIn and mentors. We got cousin's advice.

Try it: skillkite.in
Code: github.com/akkyyakhilesh/SkillKite (open source)

Share it with a small-city student you know. Week 2 will be shaped by their feedback. 🪁

#buildinpublic #aiforindia #edtech

---

## Tips before posting

1. **Add the OG image as a separate attachment** — LinkedIn renders attached images larger than auto-fetched link previews.
2. **Mention 1–2 friends** at the end (`/cc @friend`) — boosts initial reach.
3. **Reply to every comment in the first 60 min** — LinkedIn's algorithm heavily weights early engagement.
4. **Don't edit after posting** — edits suppress reach.
5. **Cross-post a 60–90 sec phone-recording demo to Instagram Reels** the same day for compounding (plan §9 content strategy).

## Content backlog for the next 2 weeks (plan §15)

- Week 2 LinkedIn: "I tested SkillKite with 3 BCA students this weekend — here's what they said."
- Week 3 LinkedIn: "First success story — student from [town] got their first roadmap deliverable shipped."
- Week 4 LinkedIn / YouTube: "Why I built the bot in Hinglish, not English-only" (with screenshot of an actual chat).
