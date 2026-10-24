# Youthnic Packing Desk

Lightweight Windows packing software for **Flipkart and Myntra VMS**. It embeds the
real VMS website (WebView2) and shows the correct product image + SKU name in a side
panel whenever the operator scans a tracking (AWB) barcode **inside the VMS** — without
touching the VMS workflow (camera, recording, scanning, uploading all stay on the
original site).

## Components

| Path | What it is |
|---|---|
| `src/Youthnic.Desktop` | WPF + WebView2 desktop app (marketplace selection, setup wizard, packing window, SKU panel) |
| `src/Youthnic.LookupApi` | ASP.NET Core read-only lookup API over the existing Forward Scan PostgreSQL database |
| `tests/Youthnic.LookupApi.Tests` | xUnit tests for the API (mapping validation, SQL builder, cache, read-only decision) |
| `installer` | `publish.ps1` + Inno Setup script → `YouthnicPackingDeskSetup.exe` |
| `docs` | `VMS_INSPECTION.md`, `DEPLOYMENT.md`, `TESTING.md` |

## How it works

1. App opens → two marketplace cards (Flipkart / Myntra). First run: admin setup
   wizard (device ID, desk name, per-marketplace VMS URL/credentials/Feed ID,
   lookup API URL). Credentials are stored DPAPI-encrypted per Windows user.
2. Selecting a marketplace opens one WebView2 with that VMS only (isolated, persistent
   profile per marketplace → saved login sessions). Camera/microphone permission is
   auto-granted **only** for the configured VMS origin.
3. The automation service probes the live DOM (≥ 800 ms, no hardcoded selectors —
   elements are found by accessible name/placeholder at runtime): fills login if
   needed, fills the Feed ID if needed, clicks Connect, detects the packing screen.
   Session expiry (route back to `#/login`) re-runs the normal login flow.
4. A scan observer is injected once on the packing screen. It listens **only** on the
   AWB input (placeholder contains `AWB`), on Enter/Tab, never preventing default —
   the VMS receives every keystroke exactly as before. Each scan is posted to the
   host, which calls the lookup API and updates the SKU panel. Newer scans cancel
   older lookups, so stale results can never overwrite the panel.
5. The lookup API resolves `marketplace + trackingId → order items → SKU name +
   image URLs` from the **existing** Forward Scan database — SELECT-only, read-only
   transactions, parameterized, with a short query timeout and bounded cache. An
   optional per-desk `seller` filter (e.g. "ETHNIC JUNCTION - Flipkart") pins
   lookups to one Forward Scan seller account. A loopback-only `/api/inspect`
   endpoint verifies connectivity, lists real seller-account names, samples
   tracking→SKU→qty→image rows, and measures image-URL speed.
   Not-yet-synced orders (Forward Scan copies every ~5 min) return a distinct
   `not_found` state with a pending-sync hint and a Retry button.

## Build & run (development)

```bash
dotnet build YouthnicPackingDesk.sln
dotnet test  tests/Youthnic.LookupApi.Tests
dotnet run   --project src/Youthnic.LookupApi        # needs VMS_DATABASE_URL
dotnet run   --project src/Youthnic.Desktop          # WPF, Windows only
```

Copy `.env.example` to `.env` for the API (dev only; the real `.env` is gitignored
and never ships in the installer).

## Installer

```powershell
powershell -File installer/publish.ps1     # publishes to installer/publish
# compile installer/setup.iss with Inno Setup 6
# → installer/Output/YouthnicPackingDeskSetup.exe
```

The installer is self-contained (no .NET runtime needed on packing PCs), checks for
the WebView2 Evergreen Runtime, and can optionally auto-start the lookup API on the
PC. Per-desk credentials/profiles are created on first launch — never bundled.

## Hosted lookup API + automatic updates

- The lookup API deploys to **Render** ("Flipkart VMS Server2.0", Starter plan $7/mo,
  sized for 8–9 packing PCs) straight from `main` via GitHub Actions; secrets live in
  GitHub Secrets and are pushed to Render by the workflow. Details: `DEPLOYMENT.md` §3a.
- Packing PCs get app updates automatically: the desktop checks GitHub Releases and
  shows an **Update** button; shipping an update is just pushing a `v*` tag (CI builds
  the installer and attaches it to the release).

## Security notes

- No marketplace credentials or Feed IDs exist in source, tests, or the installer;
  they are entered per machine in the setup wizard and DPAPI-encrypted.
- The desktop app never connects to PostgreSQL; only the API does, through a
  dedicated read-only role, inside read-only transactions.
- See `DEPLOYMENT.md` for the database role, TLS and mapping configuration.

## Status / limitations

- Flipkart VMS flow verified live (2026-10-23) — see `docs/VMS_INSPECTION.md`.
- **Forward Scan database schema verified (2026-10-24)** from a real dataset snapshot:
  single `oms_orders` table (AWB = `tracking_norm`, seller = `channel_label`), line items
  in the `items_json` JSON column, names/images from inline JSON fields with a
  `sku_photos` fallback. The lookup API auto-resolves this schema (`FS_ITEMS_JSON_COLUMN`
  support added, 65/65 tests green). All three packing seller accounts exist verbatim:
  `ETHNIC JUNCTION - Flipkart`, `ETHNIC JUNCTION - Myntra Youthnic`,
  `VB EXPORT - Myntra PPMP` — they are preset in the setup wizard and offered in the
  installer's desk-defaults page. Details: `docs/VMS_INSPECTION.md`, `docs/DEPLOYMENT.md`.
- The desktop panel now renders progressively: SKU name/qty appear the instant the API
  responds; the product image streams in asynchronously (~0.7 s measured on the Flipkart
  CDN) instead of blocking the panel.
- ⏳ The live DB host (`200.141.1.119:5433`) was unreachable from the dev machine on
  2026-10-24; run the remaining live checklist in `docs/TESTING.md` once the network
  path is up. The API degrades safely until then (`mapping_pending`, no guessed data).
- Myntra VMS URL/login/feed flow is **not yet verified**; the Myntra card stays
  "configuration pending" until real configuration is supplied and inspected.
