# Running SkillKite Locally

Day-to-day workflow for running the Phase 1 WhatsApp bot on your laptop.

## Prerequisites (one-time, already done)

- ✅ .NET 8 SDK
- ✅ PostgreSQL 18 running as a Windows service (`postgresql-x64-18`), with a `skillkite` database created
- ✅ `dotnet user-secrets` populated with:
  - `Claude:ApiKey`
  - `WhatsApp:PhoneNumberId`
  - `WhatsApp:AccessToken` ⚠️ 24-hour expiry on temp tokens
  - `WhatsApp:AppSecret`
  - `WhatsApp:VerifyToken`
  - `Pdf:PublicBaseUrl` (the cloudflared URL + `/roadmaps`)
- ✅ `cloudflared.exe` downloaded at `A:\Github\SkillKite\tools\cloudflared.exe`
- ✅ Your personal WhatsApp number added to Meta → WhatsApp → API Setup → "Manage phone number list"

To inspect current secrets:
```powershell
cd A:\Github\SkillKite\src\SkillKite.API
dotnet user-secrets list
```

---

## Starting everything (3 terminals)

### Terminal 1 — Postgres
Already running as a Windows service. Verify:
```powershell
Get-Service postgresql-x64-18
```
If `Status` is `Stopped`, start it:
```powershell
Start-Service postgresql-x64-18
```

### Terminal 2 — API
```powershell
cd A:\Github\SkillKite
dotnet run --project src/SkillKite.API
```
On first start of the day, migrations apply automatically (idempotent) and the 27 career paths are seeded if missing. You'll see:
```
Now listening on: http://localhost:5007
Application started. Press Ctrl+C to shut down.
```

Smoke test:
```powershell
Invoke-WebRequest http://localhost:5007/ -UseBasicParsing
# {"app":"SkillKite API","status":"ok"}
```

### Terminal 3 — Cloudflared tunnel
```powershell
A:\Github\SkillKite\tools\cloudflared.exe tunnel --url http://localhost:5007
```
Within ~10 seconds it prints a new public URL like:
```
https://<random-3-words>.trycloudflare.com
```
**This URL is different every time you start the tunnel.** Copy it.

---

## Reconnect to WhatsApp (each session — required because tunnel URL changes)

### 1. Update the PDF base URL secret
```powershell
cd A:\Github\SkillKite\src\SkillKite.API
dotnet user-secrets set "Pdf:PublicBaseUrl" "https://<new-url>.trycloudflare.com/roadmaps"
```
Then **restart the API** (Terminal 2: `Ctrl+C` and `dotnet run` again) so it picks up the new value.

### 2. Update Meta's webhook callback URL
1. Open https://developers.facebook.com/apps → your app → **WhatsApp** → **Configuration**.
2. Under **Webhook**, click **Edit**.
3. Callback URL: `https://<new-url>.trycloudflare.com/api/webhook/whatsapp`
4. Verify token: same value as `WhatsApp:VerifyToken` in user-secrets.
5. Click **Verify and save**. ✅ should appear.
6. Confirm `messages` is still in the subscribed fields below (it stays subscribed across edits).

### 3. Check the access token hasn't expired
If your bot stops replying after ~24h, the temporary access token expired. Get a new one from Meta → WhatsApp → API Setup → copy the new Temporary access token, then:
```powershell
cd A:\Github\SkillKite\src\SkillKite.API
dotnet user-secrets set "WhatsApp:AccessToken" "<new-token>"
```
Restart the API.

---

## Test the bot

From the WhatsApp number you whitelisted in Meta's "recipient list":

1. Open WhatsApp.
2. Message the Meta test number (shown on the API Setup page).
3. Send `Hi`.
4. Within 2-3 seconds you get a Hinglish greeting and the first assessment question.
5. Reply to 10-12 questions. Brief answers (`"BCA Jaunpur"`, `"phone hai"`) work fine.
6. After the final answer, you'll receive **in this order**:
   - 🪁 *"Bas mil gaya sab kuch! Ek minute do mujhe..."* (interstitial)
   - 🎯 Roadmap summary text (~15-25 sec later)
   - 📎 PDF document attachment (~2 sec after summary)

---

## Stopping everything

```powershell
# Stop the API + tunnel
Get-Process dotnet -ErrorAction SilentlyContinue | ForEach-Object {
  try { $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine
        if ($cmd -match "SkillKite.API") { Stop-Process -Id $_.Id -Force } } catch {}
}
Get-Process cloudflared -ErrorAction SilentlyContinue | Stop-Process -Force
```

Or just press `Ctrl+C` in each terminal.

Postgres is a Windows service — leave it running, or stop with `Stop-Service postgresql-x64-18`. Your `skillkite` database persists either way.

---

## Inspecting data

### Generated PDFs on disk
```
A:\Github\SkillKite\src\SkillKite.API\wwwroot\roadmaps\*.pdf
```

### Query the database
```powershell
$env:PGPASSWORD = "postgres"
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -d skillkite
```

Useful queries (Postgres needs double-quotes around mixed-case identifiers):
```sql
-- All students
SELECT "Phone", "Name", "City", "EducationLevel" FROM "Students";

-- Active vs completed assessments
SELECT "Status", COUNT(*) FROM "ChatSessions" GROUP BY "Status";

-- Latest conversation for a phone number
SELECT "Role", "Content", "CreatedAt"
FROM "ChatMessages" m
JOIN "ChatSessions" s ON s."Id" = m."SessionId"
JOIN "Students"     st ON st."Id" = s."StudentId"
WHERE st."Phone" = '919492040362'
ORDER BY m."CreatedAt" DESC LIMIT 30;

-- Generated roadmaps with PDF URLs
SELECT r."CreatedAt", st."Phone", st."Name", r."TotalWeeks", r."PdfUrl"
FROM "Roadmaps" r JOIN "Students" st ON st."Id" = r."StudentId"
ORDER BY r."CreatedAt" DESC;
```

### Live API logs
```powershell
Get-Content A:\Github\SkillKite\.run.log -Tail 50 -Wait
```
(`-Wait` streams new lines as they appear.)

---

## Common gotchas

| Symptom | Cause | Fix |
|---|---|---|
| Bot replies stop after a day | WhatsApp temp token expired | Regenerate in Meta → set via user-secrets → restart API |
| WhatsApp shows grey double-tick, no reply | Webhook URL in Meta points to a dead cloudflared URL | Restart tunnel, update Meta webhook callback URL |
| Bot sends text reply + summary but no PDF | `Pdf:PublicBaseUrl` not pointing to current cloudflared URL | Update user-secret and restart API |
| 401 / "invalid signature" in logs | `WhatsApp:AppSecret` doesn't match the app in Meta | Re-copy App Secret from Meta App Settings → Basic |
| "Recipient phone number not in allowed list" | Phone not added to Meta's test recipients | Meta → WhatsApp → API Setup → Manage phone number list |
| 500 on `/api/chat/message` after a long pause | API crashed or DB connection dropped | Check `.run.log`, restart API |

---

## When laptop dependency becomes painful

The cloudflared-URL-changes-on-restart routine is the main daily pain. Two options to remove it:

1. **Named Cloudflare tunnel** (free, requires a domain managed by Cloudflare — `skillkite.in` qualifies). Gives you a permanent URL like `https://bot.skillkite.in` that survives restarts.
2. **Deploy to Azure App Service** (~₹650/mo per plan §8). Permanent URL, no laptop required, push-to-deploy.

Ask Claude Code: *"set up a named cloudflare tunnel"* or *"deploy to Azure"* when ready.
