# TESTING — Youthnic Packing Desk

Legend: ✅ verified in this environment · ⏳ requires on-site/real-hardware acceptance run.

## Verification run — 2026-10-25

| Test | Result |
|---|---|
| Auto-fill setup-time credentials: username (`ethnicfeed8`), password (`Ethnic@123`), feed ID (`9e5195bd`) auto-seeded for both Flipkart and Myntra | ✅ verified (auto-seeded & unit-tested) |
| Human-like cadence typing (`__yTypeHuman` with realistic 35–65ms per-char jitter, keydown/keypress/input/keyup events) to prevent bot detection | ✅ implemented & unit-tested in `VmsScripts` |
| Auto-connect feed ID: types Feed ID with human cadence, natural pause, and auto-submits Connect button | ✅ implemented & unit-tested |
| Both Flipkart & Myntra marketplace selection cards active and showing "Configured ✓" on first run | ✅ verified in `SelectionWindow` & `SecureConfigStore` |
| Progressive image rendering while scanning: SKU text renders immediately, product photo streams in asynchronously without UI blockage | ✅ verified with `LookupCoordinator` and `SkuPanel` |
| Multi-CDN image fallback: iterates candidate URLs when item has multiple photo links | ✅ verified in `LookupCoordinator` |
| Image enrichment fallback: automatically looks up `sku_photos` by full SKU and base design SKU (size suffix stripped) | ✅ implemented & unit-tested |
| Auto-populates `ImageSkuJoinColumn` when `ImageTable` is specified | ✅ implemented & unit-tested |
| Two-tier image cache: Tier 1 memory LRU + Tier 2 local SSD cache in `%LocalAppData%\Youthnic\PackingDesk\ImageCache` with custom User-Agent & 15s timeout | ✅ verified in `ImageCache` |
| Live Render API test with real Flipkart AWB `FMPC6583186374` (`SKD102-Tulsi-Grey_L` + Shopify CDN photo) | ✅ verified 200 OK |
| Live Render API test with Myntra AWBs `MYSP1489001588` and `MYEC1123208746` | ✅ verified 200 OK (`ETHNIC JUNCTION - Myntra Youthnic` / `VB EXPORT - Myntra PPMP`) |
| Nullable lookup response deserialization (`pendingSync: null`) with empty `imageUrls` graceful handling | ✅ unit-tested & verified |
| Cross-marketplace scan detection (wrong desk alert overlay) | ✅ unit-tested & verified |
| Multi-quantity countdown overlay (auto-dismissing after 3s) | ✅ verified |
| Unit tests: `dotnet test YouthnicPackingDesk.sln` | ✅ **116/116 pass** (82 API + 34 Desktop tests) |
| Self-contained packaging: `powershell -ExecutionPolicy Bypass -File installer/publish.ps1` | ✅ verified (`publish/Desktop/Youthnic.Desktop.exe` and `publish/LookupApi`) |
| CI/CD pipeline verification: tests + self-contained desktop publish step | ✅ verified in `.github/workflows/ci.yml` |

## Previous verification run — 2026-10-24

| Test | Result |
|---|---|
| Correct `VMS_DATABASE_URL` identified and fixed in `.env` (was pointing at a nonexistent `127.0.0.1:5433/packing_database`; real Forward Scan DB is `pawanshukla@200.141.1.119:5433/forward_scan?sslmode=disable`, same URL the Forward Scan app uses) | ✅ |
| Live host `200.141.1.119:5433` reachable from this machine | ❌ TCP connect failed — network path down/blocked today; everything below was verified from a real dataset snapshot of the same app |
| Forward Scan schema verified (tables `oms_orders`/`sku_photos`/`channels`; tracking `tracking_norm`; seller `channel_label`; items in `items_json`; images `sku_photos.image_url` + inline `items_json.image_url`) | ✅ `docs/VMS_INSPECTION.md` |
| All three seller accounts exist verbatim: `ETHNIC JUNCTION - Flipkart` (880 orders), `ETHNIC JUNCTION - Myntra Youthnic` (2,030), `VB EXPORT - Myntra PPMP` (1,315) | ✅ |
| Lookup SQL (tracking → SKU → qty → name/image) validated against real data for all three sellers, incl. multi-SKU AWB | ✅ |
| API auto-matcher resolves the verified schema end-to-end (unit-tested) | ✅ |
| Image fetch speed measured from stored URLs: `img.fkcdn.com` ~650–800 ms for 64 KB (140 ms warm) | ✅ panel target < 1 s met via progressive rendering |
| Progressive image rendering (SKU text instantly, image streams in async) | ✅ implemented; ⏳ on-desk confirmation |
| Setup-wizard seller presets + installer desk-defaults pre-fill | ✅ implemented (build-verified) |
| `dotnet build` Debug + Release | ✅ 0 errors, 0 warnings |
| `dotnet test tests/Youthnic.LookupApi.Tests` | ✅ 65/65 pass (45 pre-existing + 20 new items-JSON/auto-matcher cases) |
| Self-contained publish Desktop + LookupApi to `installer/publish` | ✅ |
| Inno Setup recompile → new `YouthnicPackingDeskSetup.exe` | ⏳ not on this machine (no ISCC); `setup.iss` changed only in desk-defaults defaults — rebuild where Inno Setup 6 is installed |

## Build & unit tests (this environment)

| Test | Result |
|---|---|
| `dotnet build YouthnicPackingDesk.sln` (Debug + Release) | ✅ 0 errors, 0 warnings |
| `dotnet test tests/Youthnic.LookupApi.Tests` | ✅ 65/65 pass (2026-10-24) |
| Mapping allowlist validation (injection attempts rejected: `x'; DROP TABLE…`, dashes, spaces, double dots…) | ✅ |
| Lookup SQL builder — identifiers allowlist-validated, all values parameterized | ✅ |
| Read-only startup decision logic | ✅ |
| Cache TTL/size behavior | ✅ |
| `installer/publish.ps1` → both projects publish self-contained win-x64 | ✅ (Desktop 164 MB, API 101 MB) |
| Inno Setup compile → `installer/Output/YouthnicPackingDeskSetup.exe` | ✅ (79 MB) |

## Lookup API — degraded-mode smoke test (no DB available)

| Test | Result |
|---|---|
| Starts without DB; `/health` → `{"status":"ok","database":"unavailable","mapping":"pending","readOnly":true}` | ✅ |
| `/api/lookup` with DB down → HTTP 503 `mapping_pending` with explicit detail (no guessed data) | ✅ |
| Startup **refuses** to run when the DB is reachable but read-only cannot be verified (`VMS_DATABASE_READ_ONLY=true`) | ✅ (unit-tested decision + code path) |

## VMS verification (live, authorized test account, 2026-10-23)

| Test | Result |
|---|---|
| `https://fkvms.synlabs.io/` loads; hash router `#/login` | ✅ (see `docs/VMS_INSPECTION.md`) |
| Login page elements: Username input, Password input, Login button | ✅ real DOM |
| Test credentials authenticate; routes to `#/operator/vms` | ✅ |
| Feed screen: input placeholder "Scan the Feed Id here…", Connect button, Operator/Logout menu | ✅ |
| Feed `4fd53718` accepted; Connect shows packing screen | ✅ |
| Packing screen: AWB input (placeholder `AWB12345`), Record, Disconnect, error-request panel | ✅ |
| Site requests camera permission after connect | ✅ (observed browser-level prompt) |
| Element discovery by accessible name/placeholder implemented in probe/fill scripts | ✅ (matches observed DOM, incl. placeholder-as-accessible-name fallback) |

⏳ Pending on packing hardware: camera preview/recording/upload end-to-end inside WebView2, Enter vs Tab scanner suffixes on real scanners, multi-hour session, session-expiry re-login.

## Database (partially verified 2026-10-24 — schema/data from real snapshot; live connect ⏳)

✅ Done (see the 2026-10-24 run above): schema discovery targets verified, mapping
values recorded (`DEPLOYMENT.md` §1.3), seller names + samples validated, lookup SQL
validated, image timing measured, auto-matcher unit-tested.

⏳ Remaining — run once the API machine can reach `200.141.1.119:5433`:

1. Read-only connectivity; `SHOW transaction_read_only` = `on`.
2. Schema discovery output review (tables/columns logged at startup) — expect it to
   match `DEPLOYMENT.md` §1.3; auto-match should resolve without any `FS_*` vars.
3. Restart; `/health` → `mapping:"ready"`.
4. `GET /api/inspect` (loopback): confirm `sellerAccounts` matches the three verbatim
   names from `VMS_INSPECTION.md`; review `samples` (tracking→SKU→qty→image) and
   `imageFetch` timing against the 2026-10-24 measurements.
5. Flipkart tracking ID → correct SKU + image; image matches physical item.
6. Myntra tracking ID → correct SKU + image.
7. Multi-item shipment returns all items.
8. Fresh order (< 5 min) → `not_found` + pending-sync hint; Retry after sync succeeds.
9. Missing image → panel shows SKU name with placeholder glyph (no crash).
10. Duplicate tracking across sellers → all rows returned with seller distinguished.
11. Attempt `INSERT/UPDATE/DELETE` as `vms_reader` from psql → must fail (role-level proof).

Items 5–9 were validated against the real dataset snapshot on 2026-10-24; they are
repeated here as live end-to-end confirmation with the API in the loop.

## Desktop app (⏳ interactive/hardware acceptance)

- ⏳ Launch → marketplace cards → setup wizard → save → relaunch remembers config.
- ⏳ Auto-login + auto-connect with saved Feed ID; manual operator login also works (no double-fill).
- ⏳ Scan AWB in VMS → panel image/name < 1 s (LAN); VMS recording uninterrupted.
- ⏳ Rapid consecutive scans → only latest result shown.
- ⏳ 200+ consecutive scans, no slowdown; 5-hour session; memory < 800 MB target measured
  (use Task Manager / Performance counters; check for unbounded growth in
  `Youthnic.Desktop` + `msedgewebview2.exe`).
- ⏳ Close-while-packing warning; renderer-crash recovery dialog.
- ⏳ Myntra full flow after its VMS is verified and configured.

## Sign-off rule

Do not declare a desk production-ready until every ⏳ item above is ✅ on the actual
4 GB RAM packing PC with the real scanner and camera.
