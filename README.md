# File Strider

Find large files and folders, understand storage use, and decide what to keep. File Strider is a free, open-source desktop application built with Avalonia and .NET 10.

[Get File Strider from the Microsoft Store](https://apps.microsoft.com/detail/9PD3M0MHZ8TC)

## Version 1.1

The development branch also includes:

- **Full storage scan** and **Source files only** presets, with active exclusions shown beside the controls. Fresh settings include hidden files, logs, dependencies and build outputs; existing preferences are preserved. Offline placeholders and child symbolic links remain skipped by the full preset.
- An **Explore** tab with breadcrumbs, search within the current folder, extension filters and sorting. Files are read on demand, beyond the global top-N limit. Folder sizes and the treemap retain the scan's totals; current files can differ after filesystem changes. Aggregate treemap tiles open this complete folder list.
- **Saved scans** stored locally on request, including the entire folder tree, scan time, options and coverage. Reopen a scan or compare it with a selected baseline to find growing, new and removed folders. Comparisons require the same root and coverage settings and refuse incomplete scans. Nested folder changes are not additive.
- **Developer insights** for dependency folders, build outputs and caches, without double-counting nested candidates. Old folder timestamps are review hints, not proof that files are unused or safe to remove.
- A **Coverage** inspector with paths and reasons for skipped items. It retains up to 5,000 details and reports additional omitted entries. Retry starts a new full scan of the selected location; save the current scan first if you need to keep it.
- Progress every 200 ms, result context menus, Enter to open, Ctrl/Cmd+C to copy paths, and treemap keyboard navigation using arrow keys, Enter and Backspace.

Snapshots live in `FileStrider/history` under the platform's local application data directory. They remain on your device until removed from **Saved scans**. CSV/JSON exports remain reports, separate from saved snapshots.

Released 1.1 features:

- Reliable, cancellable scans with partial results and coverage details.
- Accurate treemap areas with folder drill-down and an Up button.
- Top 1–200 files and folders, with readable sizes and file-type statistics.
- Minimum sizes in B, KiB, MiB or GiB.
- Saved filters, exclusions, depth, concurrency, language and recent folders.
- CSV and JSON exports with scan metadata and safe spreadsheet text handling.
- English, Spanish, French and Swedish, with light and dark themes.

Choose a folder, configure the scan, and select **Start Scan**. **Scan home** scans your user folder. Click a treemap folder to explore it, or double-click a list result to open its location in the file manager. Use **Save settings** to save preferences without starting a scan.

Sizes represent logical bytes, which may differ from allocated disk space for compressed, sparse or hard-linked files. Offline cloud placeholders are skipped. Coverage details identify filtered, inaccessible, offline and depth-limited items. File Strider does not read file contents, delete files, or upload scan data.

See [release notes](RELEASE_NOTES.md) and the [privacy policy](PRIVACY_POLICY.md).

## Build and test

Install the .NET 10 SDK, then run:

~~~powershell
dotnet build FileStrider.sln -c Release
dotnet test FileStrider.sln -c Release
dotnet run --project src/FileStrider.MauiApp
~~~

The UI project retains its historical MauiApp directory name; the application uses Avalonia, not MAUI. The solution separates core models, scanning, infrastructure, platform integration, UI and tests.

Tests create their own temporary fixtures and settings files. Render tests cover all supported languages, light/dark themes, and compact windows. GitHub Actions runs tests on Windows, Linux and macOS, audits dependencies, and builds Windows packages.

## Microsoft Store packaging

On Windows with the Windows 10/11 SDK installed:

~~~powershell
./scripts/Package-Msix.ps1
~~~

This publishes self-contained x64 and ARM64 applications into fresh staging directories and generates FileStrider_&lt;version&gt;_&lt;architecture&gt;.msix. The [Store manifest](src/FileStrider.MauiApp/Package.appxmanifest) supplies the application identity and version. Optional certificate parameters are available for signed sideloading; Store submission packages are unsigned.

Source builds run as ordinary desktop applications and do not register a development MSIX over the installed Store application.

## Support

Report problems and feature requests in [GitHub Issues](https://github.com/kirakosyan/FileStrider/issues). When sharing a report or export, review it first: filenames and paths may contain personal information.

Licensed under the [MIT License](LICENSE).
