File Strider 1.1.1 — 6 September 2026

- Fixed scans of drive roots such as C:\ failing immediately on Windows.
- Scan errors and notices now appear beside the scan controls.
- About shows the running application version in every supported language.
- Added a filesystem-root regression and checks for visible errors and About layout.

File Strider 1.1.0 — 6 September 2026

- Reliable cancellation with partial results, safe worker shutdown, and a shared guard against overlapping scans.
- Accurate treemap sizes, folder drill-down, an Up button, and layout that stays within the window.
- Size filters in B, KiB, MiB or GiB; readable file and folder sizes.
- Recent scan folders and saved language, filters, exclusions, depth, concurrency, and symlink preferences.
- Coverage details for excluded, inaccessible, offline, and depth-limited locations.
- Correct scan totals and metadata in JSON exports; consistent CSV columns in every language and safer spreadsheet text cells.
- Updated Avalonia dependencies, removal of the unused internet capability, and safer platform integration.
- Tests use isolated settings and fixtures. Regression and rendered-layout tests run in CI on Windows, Linux, and macOS.

Scan sizes represent logical file sizes, not allocated blocks. Offline cloud placeholders are skipped. No automatic deletion, file-content analysis, telemetry, or uploads are performed.
