# Privacy Policy

**App name:** FileStrider  
**App ID:** com.kirakosyan.filestrider  
**Platforms:** Windows  
**Last updated:** 2026-02-21

---

## 1. Overview

FileStrider is a free, open-source disk space analysis tool that helps you find the largest files and folders on your system. We are committed to protecting your privacy. This policy explains what data the app accesses, how it is used, and what is never collected.

**Short version:** The app does not collect, store, share, or transmit any personal information to us or any third party.

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
| App settings (language, preferences) | Local app data directory | Persist your preferences |
| Scan results (file/folder sizes, paths) | In-memory only | Display analysis results during the current session |
| Export files (CSV/JSON) | User-chosen location | Save scan results when you explicitly export them |

Scan results are **not** persisted between sessions unless you explicitly export them. You can delete all locally stored settings by uninstalling the app or clearing its storage via Windows Settings.

---

## 4. File System Access

FileStrider requires access to your file system **solely** to scan directories and calculate file and folder sizes. The app:

- Reads file and folder metadata (names, sizes, dates) in the directories you choose to scan
- Does **not** read or inspect file contents
- Does **not** modify, move, or delete any files
- Does **not** transmit any file information to any external server

All scanning is performed entirely on your local device.

---

## 5. Internet Access

FileStrider does **not** require internet access to function. The `internetClient` capability declared in the app manifest is reserved for potential future features (e.g., checking for updates). The app does **not** contact any server owned or operated by the FileStrider developers.

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
