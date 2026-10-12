# RDP Manager

Jednoduchý správce RDP spojení pro Windows: vysouvací lišta na okraji obrazovky, spojení běží v nativním `mstsc` (i full-screen / multimon), hesla se berou přímo z KeePass 2.x databáze.

## Sestavení a spuštění

Vyžaduje .NET 9 SDK.

```powershell
dotnet build
dotnet run --project src/RDPManager
# samostatné .exe (vyžaduje nainstalovaný .NET 9 Desktop Runtime):
dotnet publish src/RDPManager -c Release -o publish
```

## Ovládání

- **Vysunutí lišty**: najeď myší na zvolený okraj (výchozí levý) a chvíli podrž, funguje i nad full-screen RDP. Dále klávesová zkratka (výchozí `Ctrl+Alt+R`), klik na ikonu v oznamovací oblasti nebo opětovné spuštění aplikace.
- **Připojení**: dvojklik, `Enter` (ve vyhledávání připojí první nalezené), `Shift+Enter` = `/admin`. Pravé tlačítko = upravit, duplikovat, smazat…
- `Ctrl+F` hledání, `F2` úprava, `Del` smazání, `Esc` zasunutí. Špendlík lištu připne.
- **Aktivní relace** dole v liště: klik přepne na okno relace, ✕ ji odpojí.

## Přihlašování

| Režim | Chování |
|---|---|
| Doménový / aktuální účet | Žádné údaje se nepředávají; mstsc použije uložené pověření nebo se zeptá. Přihlášení bez dotazu aktuálním účtem (SSO) vyžaduje GPO *Allow delegating default credentials* pro `TERMSRV/*`. |
| Jen uživatelské jméno | Jméno je předvyplněné, heslo se zadá v mstsc. |
| KeePass | Jméno a heslo se načtou ze záznamu (u spojení je uložené jen UUID záznamu). Podporuje `{REF:U@I:…}` / `{REF:P@I:…}`. |

Jak to funguje u KeePassu: databáze se odemkne hlavním heslem (+ volitelně souborem s klíčem) jen v paměti. Při připojení se údaje zapíšou do Správce pověření Windows jako `TERMSRV/<host>` (persistence jen pro aktuální přihlášení, bez hesla na příkazové řádce), mstsc si je přečte a po nastaveném čase (výchozí 60 s) nebo po ukončení mstsc se smažou. Pozůstatky po pádu se uklidí při dalším startu. Pokud už pro server existuje vlastní uložené pověření, aplikace se zeptá, které použít.

Databáze se zamyká po nečinnosti (výchozí 30 min) a při zamčení Windows. Změny uložené v KeePassu se načtou automaticky.

Omezení: složka klíče „Windows User Account“ z KeePassu není podporovaná. Některé firemní politiky (*Do not allow passing of saved credentials*, NTLM-only servery bez *Allow delegating saved credentials with NTLM-only server authentication*) mohou předání uloženého hesla zablokovat – mstsc se pak zeptá.

## Data

- `%APPDATA%\RDPManager\settings.json` – nastavení
- `%APPDATA%\RDPManager\connections.json` – spojení (bez hesel)
- `%TEMP%\RDPManager\*.rdp` – dočasné soubory, mažou se automaticky

## Verze a autor

Verze je v `src/RDPManager/RDPManager.csproj` (`<Version>`) a zobrazuje se v Nastavení → O aplikaci, v tooltipu ikony v oznamovací oblasti a ve vlastnostech .exe.

Autor: Jiri Nalepa – https://github.com/jirinalepa/RDPManager

## Struktura

```
src/RDPManager/
  Docking/   EdgeDockController (auto-hide na okraji, DPI, multi-monitor), hotkey, P/Invoke
  Services/  RdpLauncher, CredentialInjector, KeePassService (KeePassLib/KPCLib), SessionTracker, import .rdp
  Views/     dokovaná lišta, editor spojení, nastavení, odemčení KeePassu, výběr záznamu
```
