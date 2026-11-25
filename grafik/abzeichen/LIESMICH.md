# Abzeichen - Vorlage für neue Bilder

JG TacRelay liefert alle Abzeichen mit dem Programm aus (Sarevoc, 15.10.2026). Wer ein neues will, zeichnet es nach dieser Vorlage
und gibt es dem Maintainer; der spielt es ein und baut ein neues Setup.

## Was abgegeben wird

1. **Ein Bild als SVG** (oder ersatzweise PNG): quadratisch, 128 x 128 Bildpunkte, durchsichtiger Hintergrund.
   - Im SVG `viewBox="0 0 32 32"`, alle Formen innerhalb von 2 bis 30.
   - Keine Schrift, keine eingebetteten Bilder: Buchstaben als eigene Striche zeichnen (wie bei Gunner S/M/L).
   - Lesbar in 16 Bildpunkten: wenige, kräftige Formen, Linien ab 1,5 Einheiten.
   - Die Zeichen (Gold, Silber, Weiß) mit Kontrast mindestens 3:1 gegen dunkles Grau (#1B1D22) und gegen die Leitfarbe
     (dunkles Petrol #0D3536).
   - Nur selbst Gezeichnetes. Keine Logos, keine Spielgrafik, keine Vorlagen aus dem Netz.
2. **Eine Zeile für den Katalog** `abzeichen.json`:
   - Rang: Laufbahn (MARINE, NAVY, ORGA), Name, Platz in der Leiter.
   - Tätigkeit: Name und Gruppe (Piloten, Waffen, Technik und Versorgung, Erkundung und Gewerbe).

## Bildsprache

| Art | Grundform | Farben |
|---|---|---|
| Rang Marine | Winkel nach oben, Bögen, Balken, Sterne | Gold #D9B452, Kante #7A5A12 |
| Rang Navy | Winkel nach unten, Ärmelstreifen mit Stern | Silber #C6D3E2, Kante #2B3A4C |
| Rang Orga | Schild in Anthrazit #2B2F33 mit Rand in Petrol #2F8A8C, darin Punkte, Balken, Sterne | Gold wie Marine |
| Tätigkeit | Sechseck #26323F mit Rand #7FA0C0, Zeichen darin | Weiß #E8EEF5 |

## Einspielen (Maintainer)

1. SVG nach `grafik\abzeichen\<KENNUNG>.svg` legen. Kennung: Großbuchstaben, Ziffern und `_`, höchstens 16 Zeichen;
   Ränge `MARINE_01` usw., Tätigkeiten `T_<NAME>`.
2. Die Zeile in `grafik\abzeichen\abzeichen.json` ergänzen. Eine Kennung, die schon vergeben war, nie wiederverwenden:
   Der Server hat sie bei Mitgliedern gespeichert.
3. `pwsh -File tools\abzeichen-rendern.ps1 -Bogen <datei.png>` rechnet alle Bilder in PNG um, kopiert den Katalog nach
   `src\Funk.Protokoll\Abzeichen\` und zeigt auf Wunsch einen Übersichtsbogen.
4. Tests laufen lassen und Setup bauen. Client und Server müssen dieselbe Fassung haben, sonst kennt der Server das neue
   Abzeichen nicht und lehnt das Vergeben ab.
