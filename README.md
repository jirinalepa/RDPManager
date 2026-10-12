# RDP Manager

*English | [Čeština](README.cs.md)*

A simple RDP connection manager for Windows: an auto-hiding panel on the screen edge, connections run in the native `mstsc` client (including full screen / multi-monitor), and passwords are read directly from a KeePass 2.x database.

## Build and run

Requires the .NET 9 SDK.

```powershell
dotnet build
dotnet run --project src/RDPManager
# single-file .exe (requires the .NET 9 Desktop Runtime):
dotnet publish src/RDPManager -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Usage

- **Show the panel**: move the mouse to the configured screen edge (left by default) and rest it there briefly – this also works over a full-screen RDP session. Alternatively use the hotkey (default `Ctrl+Alt+R`), click the tray icon, or start the application again.
- **Connect**: double-click, `Enter` (in the search box it connects to the first match), `Shift+Enter` = `/admin`. Right-click = edit, duplicate, delete…
- `Ctrl+F` search, `F2` edit, `Del` delete, `Esc` hide. The pin keeps the panel open.
- **Active sessions** at the bottom of the panel: click to switch to the session window, ✕ disconnects it.

## Language

The UI is available in English and Czech. By default the application follows the Windows display language (Czech → Czech, anything else → English); you can override it in Settings → General → Language. A change takes effect after the application restarts (it offers to restart right away).

## Sign-in

| Mode | Behaviour |
|---|---|
| Domain / current account | No credentials are passed; mstsc uses a saved credential or asks. Signing in with the current account without a prompt (SSO) requires the GPO *Allow delegating default credentials* for `TERMSRV/*`. |
| User name only | The user name is pre-filled, the password is entered in mstsc. |
| KeePass | User name and password are read from the entry (the connection stores only the entry UUID). `{REF:U@I:…}` / `{REF:P@I:…}` references are supported. |

How the KeePass mode works: the database is unlocked with the master password (and optionally a key file) in memory only. When connecting, the credentials are written to Windows Credential Manager as `TERMSRV/<host>` (persisted only for the current logon session, never on a command line). mstsc reads them during sign-in, and they are deleted after a configurable time (60 s by default) or when mstsc exits. Leftovers after a crash are cleaned up on the next start. If you already have your own saved credential for the server, the application asks which one to use.

The database locks after inactivity (30 min by default) and when Windows is locked. Changes saved in KeePass are picked up automatically.

Limitations: the KeePass “Windows User Account” key component is not supported. Some corporate policies (*Do not allow passing of saved credentials*, NTLM-only servers without *Allow delegating saved credentials with NTLM-only server authentication*) can block passing the saved password – mstsc then asks for it.

## Data

- `%APPDATA%\RDPManager\settings.json` – settings
- `%APPDATA%\RDPManager\connections.json` – connections (no passwords)
- `%TEMP%\RDPManager\*.rdp` – temporary files, deleted automatically

## Version and author

The version is set in `src/RDPManager/RDPManager.csproj` (`<Version>`) and is shown in Settings → About, in the tray icon tooltip and in the .exe properties.

Author: Jiri Nalepa – https://github.com/jirinalepa/RDPManager

## Project structure

```
src/RDPManager/
  Docking/     EdgeDockController (edge auto-hide, DPI, multi-monitor), hotkey, P/Invoke
  Services/    RdpLauncher, CredentialInjector, KeePassService (KeePassLib/KPCLib), SessionTracker, .rdp import
  Views/       dock panel, connection editor, settings, KeePass unlock, entry picker
  Properties/  Strings.resx (English) and Strings.cs.resx (Czech) – all UI texts
```

To add or change a text, edit both `.resx` files; Visual Studio regenerates `Strings.Designer.cs` automatically (or add the matching property there by hand).
