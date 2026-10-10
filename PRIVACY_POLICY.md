# Privacy Policy

**App name:** FileStrider  
**App ID:** com.kirakosyan.filestrider  
**Platforms:** Windows  
**Last updated:** 2026-10-10

---

## 1. Overview

FileStrider is a free, open-source disk space analysis tool that helps you find the largest files and folders on your system. We are committed to protecting your privacy. This policy explains what data the app accesses, how it is used, and what is never collected.

**Short version:** FileStrider keeps scan data and preferences on your device and does not send them to us or any third party. The Microsoft Store edition uses Microsoft's Store service to check for app updates.

---

## 2. Data We Do NOT Collect

FileStrider does **not** collect or transmit:

- Personal identification information (name, email, phone number, etc.)
- Device identifiers or advertising IDs
- Location data
- Usage analytics or telemetry
- Crash reports sent to any remote server
- File contents or file names to any external server
- Browsing history

---

## 3. Data Stored Locally on Your Device

All application data is stored exclusively on your device and is never sent anywhere:

| Data | Where it is stored | Purpose |
|---|---|---|
| App settings (language, scan preferences, up to eight recent folder paths, recovery copy) | Local app data directory | Restore preferences and recently scanned locations |
| Scan results (file/folder sizes, paths) | In-memory only | Display analysis results during the current session |
| Saved scans (folder tree, top files, sizes, timestamps, scan filters and coverage paths) | `FileStrider/history` under the local app data directory | Reopen or compare scans saved with Save current scan or before closing through Upgrade and close |
| Export files (CSV/JSON) | User-chosen location | Save scan results when you explicitly export them |

Scan results are **not** persisted between sessions unless you save a scan, export a report, or choose Upgrade and close (which saves the current scan before exiting). Saved scans can be removed individually from the Saved scans tab. Their filenames and paths may contain personal information. Settings and saved scans can also be removed by clearing the FileStrider directory in local application data.

---

## 4. File System Access

FileStrider requires access to your file system **solely** to scan directories and calculate file and folder sizes. The app:

- Reads file and folder metadata (names, sizes, dates) in the directories you choose to scan
- Does **not** read or inspect file contents
- Does **not** modify, move, or delete scanned files. It writes its own settings, explicitly saved scans and requested exports, and removes saved scans when you request that action.
- Does **not** transmit any file information to any external server

All scanning is performed entirely on your local device.

---

## 5. Internet Access

Scanning and local reports work without internet access. FileStrider does **not** contact any server owned or operated by the developers. Starting with version 1.2.1, the Microsoft Store edition asks the Windows Store API for available app updates on startup and when you select Check for updates. This requires an internet connection and uses Microsoft's Store services; no scanned paths, filenames, file contents, reports or settings are supplied to those services. Selecting Upgrade and close opens the app's Microsoft Store page so you can install the update. Microsoft's handling of Store service data is governed by its privacy statement. Opening the source-code or support link launches your default browser only when you request it.

---

## 6. Third-Party Libraries

FileStrider uses the following open-source libraries:

| Library | Purpose |
|---|---|
| [Avalonia UI](https://avaloniaui.net/) | Cross-platform UI framework |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM architecture support |
| [MessageBox.Avalonia](https://github.com/AvaloniaCommunity/MessageBox.Avalonia) | Dialog boxes |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) | Dependency injection and hosting |

No advertising SDKs, analytics frameworks, or tracking libraries are included in the app.

---

## 7. Children's Privacy

FileStrider is not directed at children under the age of 13 (or the applicable age of digital consent in your jurisdiction). We do not knowingly collect any information from children.

---

## 8. Changes to This Policy

We may update this Privacy Policy from time to time. When we do, we will update the **Last updated** date at the top of this document. Continued use of the app after any changes constitutes acceptance of the updated policy.

---

## 9. Open Source

FileStrider is open source. You are welcome to inspect the full source code to verify the claims made in this policy:

**Repository:** https://github.com/kirakosyan/FileStrider

---

## 10. Contact

If you have any questions or concerns about this Privacy Policy, please open an issue in the GitHub repository:

https://github.com/kirakosyan/FileStrider/issues
