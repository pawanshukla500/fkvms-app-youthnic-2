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

- All marketplace credentials and Feed IDs are pre-seeded with authorized defaults (`ethnicfeed8`, `Ethnic@123`, `9e5195bd`) and encrypted locally with Windows DPAPI (CurrentUser scope) with automatic local recovery. Operators can customize settings anytime via the Setup Wizard.
- The desktop app never connects to PostgreSQL directly; only the Lookup API does, through a dedicated read-only role, inside read-only transactions.
- See `DEPLOYMENT.md` for the database role, TLS, and mapping configuration.

## Status & Verified Capabilities

- **Zero-Setup First Run**: Both Flipkart and Myntra desks are automatically pre-configured with authorized credentials (`ethnicfeed8` / `Ethnic@123`), feed IDs (`9e5195bd`), VMS URLs (`https://fkvms.synlabs.io/`), and respective seller accounts (`ETHNIC JUNCTION - Flipkart` and `ETHNIC JUNCTION - Myntra Youthnic`). Both cards show "Configured ✓" out of the box with zero manual typing required.
- **Auto-Login & Auto-Feed Connection**: The background VMS automation drives the live DOM asynchronously through login and feed connection on both marketplaces, automatically recovering if sessions expire.
- **High-Performance Progressive Image Rendering**: When an AWB barcode is scanned, the SKU name, seller, and quantity display instantly. Product photos load progressively in the background from CDNs or the local two-tier cache (Tier 1: in-memory LRU, Tier 2: persistent local SSD cache in `%LocalAppData%\Youthnic\PackingDesk\ImageCache`). Over 63,000 catalog photos are synced across styles (`forward_scan.sku_photos`).
- **Multi-Item Visual Cards & Centered Modal (vms.youthnic.shop Match)**: Multi-item parcels automatically render compact individual product cards in the side panel with photos, top-right yellow `x{qty}` badges, SKU titles, and sizes. Simultaneously triggers a prominent centered popup modal displaying all parcel items side-by-side with an auto-dismissing 5-second countdown timer, click-outside dismissal, and Esc shortcut to prevent wrong item packing.
- **Seamless In-App Updates**: Integrated update checker in both `SelectionWindow` and `PackingWindow` status strip. Operators can click "Check for Updates" or "⚡ Update Available" to download and install updates over the running app seamlessly without uninstalling.
- **Cross-Marketplace Detection**: Automatically detects cross-marketplace scans (e.g. scanning a Myntra barcode on a Flipkart desk) and displays visual warning overlays.
- **Comprehensive Test Coverage & CI/CD**: 147 unit tests across `Youthnic.LookupApi.Tests` (99 tests) and `Youthnic.Desktop.Tests` (48 tests) validate SQL generation, auto-matching, contract serialization, product cards, size extraction, DPAPI storage, scan observer, image URL candidate filtering, duplicate scan suppression, and VMS script generation. Every commit and PR is verified via GitHub Actions (`ci.yml`), including self-contained desktop publishing.
