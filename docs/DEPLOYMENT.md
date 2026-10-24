# DEPLOYMENT — Youthnic Packing Desk

## 1. Lookup backend (Youthnic.LookupApi)

The API is the **only** component that talks to the Forward Scan PostgreSQL database.
Packing desks call it over HTTP (loopback if the API runs on the desk PC, LAN
otherwise — set `LOOKUP_API_URL` in the desktop setup wizard accordingly).

### 1.1 Database role (PostgreSQL side — enforce at the role level)

Create a dedicated read-only role; do not rely on application discipline alone:

```sql
CREATE ROLE vms_reader LOGIN PASSWORD '<strong-random-password>';
GRANT CONNECT ON DATABASE forward_scan TO vms_reader;
-- after the schema is verified (section 1.3), grant SELECT on ONLY the required tables/views:
-- GRANT USAGE ON SCHEMA <schema> TO vms_reader;
-- GRANT SELECT ON <schema>.<orders>, <schema>.<items>, <schema>.<sku>, <schema>.<images> TO vms_reader;
ALTER ROLE vms_reader SET default_transaction_read_only = on;
```

The API **additionally** verifies at startup (`SHOW transaction_read_only` must be
`on`) and, with `VMS_DATABASE_READ_ONLY=true` (default), refuses to start if
read-only operation cannot be guaranteed while the database is reachable.

### 1.2 API configuration (environment variables, never in the installer)

| Variable | Default | Purpose |
|---|---|---|
| `VMS_DATABASE_URL` | — | Full Npgsql connection string. The Forward Scan app itself connects without TLS: `postgresql://pawanshukla:<pwd>@200.141.1.119:5433/forward_scan?sslmode=disable` — if the PG server has no TLS certificate, `sslmode=disable` (or `Prefer`, the Npgsql default) is required or every connect fails. Use `Require` only once TLS is confirmed working on this server. |
| `VMS_DATABASE_READ_ONLY` | `true` | Read-only enforcement + startup verification |
| `VMS_DB_QUERY_TIMEOUT_MS` | `2000` | Per-query timeout — slow DB must never stall the panel |
| `VMS_LOOKUP_CACHE_SECONDS` | `60` | Positive-cache TTL |
| `LOOKUP_API_URL` | `http://127.0.0.1:5080` | Bind URL. **Loopback only** unless you deliberately expose it on the LAN (firewall it). |

On Windows, set these as machine/user environment variables or start the exe from a
wrapped shortcut/batch; a `.env` file next to the exe is also read (dev convenience).

### 1.3 Schema mapping (Forward Scan) — **verified 2026-10-24** (see `VMS_INSPECTION.md`)

The production schema was verified from a real dataset snapshot; the API auto-matcher
resolves it end-to-end (unit-tested). Verified mapping values (only needed if auto-match
is ever broken; set as `FS_*` environment variables):

| FS_* variable | Verified value | Meaning |
|---|---|---|
| `FS_SCHEMA_ORDERS` | `public` | orders table schema |
| `FS_ORDERS_TABLE` | `oms_orders` | one row per shipment/AWB |
| `FS_COL_TRACKING` | `tracking_norm` | AWB / tracking ID |
| `FS_COL_SELLER` | `channel_label` | seller account name, verbatim |
| `FS_ITEMS_JSON_COLUMN` | `items_json` | JSON array of line items (`sku`, `qty`, optional inline `title`/`image_url`) — used instead of an items table |
| `FS_SKU_MAP_TABLE` / `FS_COL_SKU` / `FS_COL_SKU_NAME` | `sku_photos` / `sku` / `title` | SKU → product name fallback |
| `FS_IMAGE_TABLE` / `FS_COL_IMAGE_URL` | `sku_photos` / `image_url` | image URL fallback |

There is **no** marketplace column on `oms_orders` (marketplace only exists on the
`channels` master) — leave `FS_COL_MARKETPLACE` unset and filter by seller account.

The API behaviour is unchanged in spirit:

1. On startup, discovers schemas/tables/columns (incl. column data types) via
   `information_schema` / `pg_catalog` (logged compactly).
2. Resolves the mapping from explicit `FS_*` variables (preferred; table above).
3. Only if those are absent, attempts a conservative auto-match strictly against
   discovered metadata (keywords incl. `tracking_norm`, `channel_label`, `items_json`,
   `sku_photos`).
4. If anything required is unresolved, it logs the **exact gap** and serves
   `mapping_pending` (HTTP 503) — it never fabricates joins or returns guessed data.

### 1.4 One-call verification: `GET /api/inspect` (loopback only)

Once the API can reach the database, `curl http://127.0.0.1:5080/api/inspect`
answers the three deployment questions in one shot, without exposing any credential:

- `database.connected` / `readOnly` — is the URL right and read-only enforced?
- `columns` / `mapping` — the discovered schema and the resolved (or pending) mapping.
- `sellerAccounts` — every distinct seller value in the orders table; confirms the
  real names of **ETHNIC JUNCTION - Flipkart**, **ETHNIC JUNCTION - Myntra Youthnic**
  and **VB EXPORT - Myntra PPMP** (whatever they actually are in Forward Scan).
- `samples` — for each matched seller: tracking ID → SKU → SKU name → qty → image
  link (grouped, latest first).
- `imageFetch` — measured HTTP timing (headers/total ms, bytes, content type) for a
  sample image link, i.e. how fast the panel can render product photos.

Use these results to fill in the `FS_*` values (if auto-match did not already) and
the per-desk seller-account setting. Access from non-loopback clients is refused (403).

### 1.5 Seller-account filtering

`GET /api/lookup` accepts an optional exact `seller` parameter
(e.g. `?marketplace=flipkart&trackingId=…&seller=ETHNIC JUNCTION - Flipkart`).
Each packing desk configures its own seller account per marketplace in the setup
wizard; duplicate tracking IDs across seller accounts then never mix. The filter is
an exact parameterized match (`upper(a) = upper($N)`) — copy the seller name exactly
as shown by `/api/inspect`. The setup wizard seller fields are editable combo boxes
prefilled with the three verified accounts
(`ETHNIC JUNCTION - Flipkart` / `ETHNIC JUNCTION - Myntra Youthnic` /
`VB EXPORT - Myntra PPMP`); free text still works for any other account.

The schema, mapping values and seller names are verified — see `VMS_INSPECTION.md`.
Once the live host is reachable from the API machine, run the remaining checklist in
`docs/TESTING.md` (database section, items 1–3 and 10) and confirm `/api/inspect`
output matches the recorded values.

## 2. Desktop app (packing desks)

1. Install `YouthnicPackingDeskSetup.exe` (admin). WebView2 Evergreen Runtime must be
   present (the installer warns if missing).
2. First launch → **Settings / Setup wizard** (admin-only in practice):
   - Device ID (auto-generated GUID — unique per PC), packing desk name.
   - Flipkart: VMS URL, username, password, Feed ID, optional **seller account**
     (e.g. `ETHNIC JUNCTION - Flipkart`). Myntra: same when provided
     (e.g. `ETHNIC JUNCTION - Myntra Youthnic`, `VB EXPORT - Myntra PPMP`).
   - Lookup API URL (e.g. `http://127.0.0.1:5080` desk-side, or
     `http://<lan-server>:5080` shared) → **Test connection** calls `/health`.
   If the installer wrote `install-defaults.json` (see below), the API URL and the
   suggested seller accounts are pre-filled — review, adjust, save.
3. Operators then only: open app → pick marketplace → pack. Logins and sessions are
   remembered per marketplace (isolated WebView2 profile under `%LocalAppData%`).

### Changing credentials / Feed IDs later

No rebuild needed: open the Setup wizard from the selection screen, update values,
save. To force re-authentication, delete or rename
`%LocalAppData%\Youthnic\PackingDesk\profiles\<marketplace>` for that desk.

## 2a. Installer options

`YouthnicPackingDeskSetup.exe` offers, at install time:

- **Components** — the packing desk application (always) and the optional desk-side
  **Lookup API** (skip it when a LAN server hosts the API for many desks).
- **Lookup API auto-start at logon** — only offered with the API component.
- **Desk defaults page (optional)** — admin-entered default lookup API URL and
  suggested Flipkart/Myntra seller accounts (Flipkart field pre-filled with
  `ETHNIC JUNCTION - Flipkart`; both verified Myntra accounts offered for the Myntra
  field). Written to
  `%ProgramData%\Youthnic\PackingDesk\install-defaults.json` (no secrets). Each
  desk's setup wizard pre-fills from it on first run.

## 3. Network

- Desk → VMS websites: 443 outbound (Flipkart/Myntra VMS domains).
- Desk → image hosts: outbound 443 (SKU images load from the URLs stored in Forward
  Scan).
- Desk → Lookup API: as configured (loopback, LAN 5080, or the hosted Render URL over
  443).
- **Nothing** connects from the desk or the API to the Forward Scan DB except the API
  (5433), and the API performs no writes, ever.

## 3a. Hosted lookup API on Render (primary deployment)

The lookup API is hosted on Render as **"Flipkart VMS Server2.0"** (Render project
`prj-db7k1pl9fdbs73ehkmc0`), deployed from `github.com/pawanshukla500/fkvms-app-youthnic-2`.

**Cost / capacity (target ≤ $5–7/mo, 8–9 packing PCs):**

- Plan: **Starter ($7/mo, 512 MB RAM, 0.5 CPU, always-on)** — the free plan is NOT used
  (it spins down on idle, which would stall scans). No other billable Render resources:
  the PostgreSQL stays on the existing `200.141.1.119` server (no Render managed DB).
- Load: 8–9 PCs produce well under ~2 lookups/sec at scan time; each lookup is a single
  indexed-ish SELECT with a 2 s timeout, a 60 s positive cache, and a 500-entry bounded
  cache — far below Starter capacity. The container is memory-tuned
  (`DOTNET_GCConserveMemory=1`, 300 MB heap cap).
- Watch: Render dashboard → Metrics (CPU/RAM). If RAM ever approaches 512 MB, lower
  `VMS_LOOKUP_CACHE_SECONDS` or `MaxPoolSize` before considering a bigger plan.

**Security model (public internet):**

- `LOOKUP_API_KEY` is set on the service → every `/api/*` call from non-loopback clients
  must send `X-Api-Key` (401 otherwise). `/health` stays open for Render health checks.
- Each packing PC gets the key via the setup wizard / installer desk-defaults
  (`install-defaults.json` → `lookupApiKey`) or manually. The key is a shared desk
  secret (not per-user); rotate by changing the GitHub secret + Render var and updating
  desks.
- `VMS_DATABASE_URL` lives only in GitHub Secrets → pushed to Render env vars by the
  deploy workflow; never in the repo, the image, or logs.

**Prerequisites before the first successful deploy:**

1. Render API key: render.com → Account Settings → API Keys → copy → GitHub secret
   **`RENDER_API_KEY`**.
2. Create the service "Flipkart VMS Server2.0" in project `prj-db7k1pl9fdbs73ehkmc0`
   (New → Blueprint → pick this repo, or let the deploy workflow create env vars on an
   existing service). Copy its service id from the dashboard URL → GitHub secret
   **`RENDER_SERVICE_ID`**.
3. GitHub secrets already set: `VMS_DATABASE_URL`, `LOOKUP_API_KEY`.
4. **Database must allow inbound from Render**: on the `200.141.1.119` PostgreSQL, add a
   `pg_hba.conf` entry for the `pawanshukla` user from Render's egress IPs (or
   `0.0.0.0/0` with a strong password, knowing Render outbound IPs are not static), and
   ensure the firewall/NAT forwards 5433. Without this the service starts in degraded
   mode (`mapping_pending`) — safe, but lookups won't work.
5. Desks point `LOOKUP_API_URL` at the Render URL (e.g.
   `https://flipkart-vms-server2-0.onrender.com`) and enter the API key in the setup
   wizard.

**Workflows:**

- `.github/workflows/deploy.yml` — every push to `main`: pushes `VMS_DATABASE_URL` +
  `LOOKUP_API_KEY` from GitHub secrets to the Render service env, then triggers a
  deploy. Idempotent.
- `.github/workflows/release.yml` — every `v*` tag: builds the WPF app self-contained,
  compiles the Inno Setup installer (version from the tag), creates a GitHub Release
  with `YouthnicPackingDeskSetup.exe` attached.

## 4. Upgrades

- App: build new installer, install over the old one. Profiles and DPAPI config
  survive (they live in `%LocalAppData%`).
- **Automatic updates (implemented 2026-10-24):** the desktop app checks the latest
  GitHub Release on startup, every 4 h, and on demand (click the version in the
  selection window). When a newer `YouthnicPackingDeskSetup.exe` asset exists, an
  "Update available" button appears; one click downloads and runs the installer, which
  installs over the old version. To ship an update: bump the tag (`git tag v1.0.1 &&
  git push origin v1.0.1`) — the release workflow builds and publishes everything.
- Never enable automatic updates on packing PCs during working hours; update between
  shifts — the button notifies, the operator chooses when to install.
