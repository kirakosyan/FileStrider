Unreleased — Storage exploration and history

- Full-storage and source-only presets with visible active filters; existing settings are preserved.
- Time-based scan progress, including while file processing is stalled.
- Complete folder exploration with breadcrumbs, search, extension filters, sorting and expandable aggregate tiles.
- Local saved scans and folder growth comparisons, with guards for differing roots, filters and incomplete coverage.
- Developer storage insights for dependencies, build outputs and caches, with nested totals deduplicated.
- Coverage details with bounded memory and targeted rescans.
- Result context menus and keyboard actions, plus keyboard treemap navigation.
- Updated English, French, Spanish and Swedish interfaces and render/regression coverage.

File Strider 1.1.2 — 1 October 2026

- More accurate totals when following directory links, including linked roots, aliases and cycles. Real folders keep their original paths in results.
- Followed file links use the target's size and timestamp. Unavailable targets appear in coverage details.
- Folder timestamps are preserved during concurrent scans.
- CSV reports begin with scan status, totals and coverage, using the same metadata as JSON.
- Settings recover cleanly after a failed save while preserving the original error.
- Treemaps remain stable with very large size differences and avoid repeated summation during layout.
- Clearer app branding and more readable advanced scan controls.
- Refreshed Store descriptions, search keywords and localized screenshots using only synthetic sample data.

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
