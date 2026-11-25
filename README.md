# JG TacRelay

Sprechfunk für Star-Citizen-Gruppen und andere Spielergruppen: ein Client für Windows, mit dem die Mitglieder funken,
und ein Server unter Linux, über den jeder Funkspruch läuft.

> **Vorabversion 0.13.1.** JG TacRelay ist noch nicht fertig. Getestet ist es bisher im kleinen Kreis; ein Abend mit
> vielen Leuten und die Anzeigen über dem laufenden Spiel stehen noch aus.

## Was es kann

- ein Funkfeld mit bis zu zehn Kanälen und ihren Ch., Rollen, die die Kanäle in die Felder legen, und eine fertige
  Kanalvorlage für Star Citizen
- Sprechtaste (auch am Joystick) und Sprachaktivierung, Rauschunterdrückung, Echoschutz und automatische Lautstärke
- die Liste "Wer ist da" und die Funkfeld-Zuteilung, in der die Einsatzleitung die Leute per Ziehen einteilt
- eine Verwaltung für Admins mit zweitem Faktor (TOTP), die Freigabe jedes Geräts und signierte Serverprofile
- Abzeichen mit Rängen und Tätigkeiten
- über dem Spiel die Sprecheranzeige und das Meldesystem der Leitung: Befehle, Countdown, Ampel, Bereit-Abfrage und
  Textmeldungen
- den Linux-Server, Setup und Admin-Setup und ein Handbuch (`docs\handbuch`)

## Was noch fehlt

- ein echter Abend mit vielen Leuten und der Test der Anzeigen im laufenden Spiel
- ein feineres Rechtesystem für die Leitung, Gruppen oder Fraktionen innerhalb eines Raids
- eine Serverinstallation, die ohne Konsolenkenntnisse geht

## Eine eigene Gruppe einrichten

Jede Gruppe betreibt ihren eigenen Server mit eigenen Schlüsseln. Von Sarevoc braucht dafür niemand etwas: keinen
Schlüssel, kein Profil, kein Passwort. Ein Client vertraut nur den Profilschlüsseln, die beim Bau in ihm stehen. Den
öffentlichen Teil deines eigenen Profilschlüssels trägst du in `konfig\profilschluessel.txt` ein und baust Setup und
Admin-Setup selbst. Der Weg Schritt für Schritt steht im Handbuch, Abschnitt B0.4; den Server einzurichten beschreibt
Teil B.

## Bauen

Voraussetzungen: Windows 10 oder 11 (x64), .NET 10 SDK, PowerShell 7.

```
dotnet build TacRelay.slnx -c Release
```

Die nativen Bibliotheken für den Klang (Opus, RNNoise, SpeexDSP, WebRTC-Audioverarbeitung, DeepFilterNet) liegen nicht im
Repository. Sie entstehen mit den Skripten unter `native\skripte` aus den Quellen ihrer Herkunftsorte (`native\BAUBERICHT.md`);
ein installiertes Setup bringt sie ebenfalls mit. Den Ordner mit den DLLs nennst du dem Bau mit
`-p:FunkNativeOrdner=<ordner>`.

Etwa 40 Klangtests brauchen den Klangkorpus, Sprache der Windows-Sprachausgabe, die nicht im Repository liegt. Ihn
erzeugt `tests\Funk.Tests\Daten\klangkorpus\klangkorpus-erzeugen.ps1` mit den deutschen Stimmen Stefan, Katja und Hedda,
einmal ohne Schalter und einmal mit `-Name klangkorpus-kurz`.

Setup und Admin-Setup bringen die .NET Desktop Runtime 10 mit und installieren sie, wenn sie fehlt. Den Installer von
Microsoft (`windowsdesktop-runtime-10.x.y-win-x64.exe`, von https://dotnet.microsoft.com/download/dotnet/10.0) legt
`tools\setup-bauen.ps1` ins Setup; er liegt nicht im Repository und kommt mit dem Schalter `-Laufzeit <datei>`.

Die Skripte unter `tools\` und `tests\` (Testlauf, Pakete, Setup mit Inno Setup) stammen aus der Arbeitsumgebung des
Maintainers und erwarten an einigen Stellen den Ordner `C:\Claude`. Wer anderswo baut, passt diese Pfade an.

## Aufbau

| Ordner | Inhalt |
| --- | --- |
| `src\` | Client (WPF), Server, Protokoll, Klang, Verwaltung und `funk-admin` |
| `tests\` | Tests, Testserver und Testclient |
| `docs\` | Handbuch, Funkprotokoll und Anleitungen für den Server |
| `konfig\` | Beispielkonfiguration, systemd-Units und Einrichtungsskript |
| `installer\` | Setup (Inno Setup) |
| `native\` | Bauskripte und Bericht der nativen Bibliotheken |
| `extern\Glasdesign\` | Kopie der Designbibliothek Glasdesign |
| `tools\` | Werkzeuge zum Bauen und Prüfen |

## Lizenz

Apache License 2.0, siehe `LICENSE`. Wer JG TacRelay oder Teile davon weitergibt, gibt auch den Inhalt von `NOTICE`
mit. Die Fremdbestandteile und ihre Lizenzen stehen in `THIRD-PARTY-NOTICES.txt`.

## Mitwirkende

- **Sarevoc**: Idee, Anforderungen, Entscheidungen und Tests; Maintainer und Rechteinhaber. Sein anderes Projekt ist
  [Joystick Guardian](https://github.com/Nitrolaus/JoystickGuardian).
- **Coding**: Code, Tests und Dokumentation schreibt Claude, ein KI-Modell von Anthropic, nach den Vorgaben von Sarevoc.
  Claude ist weder Urheber noch Rechteinhaber.
- **Meldungsbilder**: von Sarevoc mit ChatGPT erstellt.

## Hinweis

Star Citizen ist eine Marke von Cloud Imperium. Discord ist eine Marke von Discord Inc. JG TacRelay ist mit keinem
dieser Unternehmen verbunden.
