// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Kanalsperren (0x003A, 6.17, seit 0.6.0) am Steuerkanal: SETZEN, AUFHEBEN und LISTE für Sitzungen mit <c>ZUWEISEN</c>, ohne <c>bis</c>
/// bis zum nächsten 04:00 Ortszeit; Journal (KANAL_GESPERRT, KANAL_FREI mit Kanal und Klarnamen) und Meldung KANALSPERRE an die
/// Einsatzleitung. Was gesperrt heißt, setzt der Zustandsschreiber durch.
/// </summary>
public sealed partial class Zentrale
{
    private Antwort? KanalSperre(Verbindung v, S.KanalSperreAnfrage a, TaskCompletionSource<Antwort> tcs)
    {
        var z = Schreiber.Aktuell;
        if (!HatRecht(z, v, L.Recht.Zuweisen)) return Antwort.Fehl(S.Fehlercode.KeinRecht);
        var jetzt = Zeit.LageMs();
        L.Auftrag auftrag;
        switch (a.Op)
        {
            case S.KanalSperreOperation.Liste:
                return Antwort.Mit(KanalsperrListe(z, jetzt));
            case S.KanalSperreOperation.Setzen:
                var bis = a.Bis ?? Math.Min(S.Auftragszeit.NaechsterMorgen(jetzt), jetzt + S.Kanalrechnung.SperreMaxMs);
                auftrag = L.Auftrag.KanalSperreSetzen(v.KontoId, a.KontoId!.Value, a.NetzId!.Value, bis);
                break;
            default:
                auftrag = L.Auftrag.KanalSperreAufheben(v.KontoId, a.KontoId!.Value, a.NetzId!.Value);
                break;
        }
        Schreiben(auftrag, e => tcs.TrySetResult(e.Erfolg ? Antwort.Mit(KanalsperrListe(Schreiber.Aktuell, Zeit.LageMs())) : FehlerAus(e)));
        return null;
    }

    /// <summary>
    /// Kanalsperren an der Konsole (6.17): <c>kanalsperre-setzen --konto|--alle --netz &lt;id|name&gt; [--ch n] [--bis HH:MM]</c>,
    /// <c>kanalsperre-aufheben</c> mit denselben Angaben ohne <c>--bis</c> und <c>kanalsperren</c> (die Liste). Ohne <c>--ch</c> gilt die
    /// Sperre dem ganzen Netz (bei einem Kanalnetz allen Ch.). Die Konsole ist der Eigentümer am Server: Sie darf auch eine Sperre
    /// aufheben, die die Einsatzleitung gesetzt hat; als Urheber steht 0 im Zustand und im Journal.
    /// </summary>
    private VerwaltungsAntwort? KanalsperrKonsole(string befehl, JsonObject a, TaskCompletionSource<VerwaltungsAntwort> tcs)
    {
        var z = Schreiber.Aktuell;
        var jetzt = Zeit.LageMs();
        if (befehl == "kanalsperren")
        {
            var liste = KanalsperrListe(z, jetzt).Sperren;
            var sb = new StringBuilder();
            var daten = new JsonArray();
            foreach (var s in liste)
            {
                var wer = s.KontoId is null or 0 ? "alle ohne Einsatzleitung" : KontoText(s.KontoId.Value);
                sb.Append(CultureInfo.InvariantCulture,
                    $"{KanalText(z, s.NetzId)} für {wer} bis {S.Auftragszeit.Anzeigen(s.Bis)}, von {DurchText(s.Durch)}\n");
                daten.Add(new JsonObject { ["netzId"] = s.NetzId, ["kontoId"] = s.KontoId ?? 0, ["bis"] = s.Bis, ["durch"] = s.Durch });
            }
            return VerwaltungsAntwort.Gut(liste.Count == 0 ? "Keine Kanalsperren" : sb.ToString().TrimEnd(), daten);
        }

        var alle = a["alle"] is JsonValue v && v.TryGetValue<bool>(out var ja) && ja;
        uint konto = 0;
        if (!alle)
        {
            if (Zahl(a, "konto") is not { } k) return VerwaltungsAntwort.Fehler("Für wen? --konto <id> oder --alle. Nichts geändert.");
            if (!z.Konten.ContainsKey(k)) return VerwaltungsAntwort.Fehler($"Das Konto {k.ToString(CultureInfo.InvariantCulture)} gibt es nicht. Nichts geändert.");
            konto = k;
        }
        var (kanal, fehler) = KanalAusNetzUndCh(z, Text(a, "netz"), Zahl(a, "ch"));
        if (kanal is not { } netzId) return VerwaltungsAntwort.Fehler(fehler + " Nichts geändert.");
        var fuer = alle ? "alle ohne Einsatzleitung" : KontoText(konto);

        if (befehl == "kanalsperre-aufheben")
        {
            Schreiben(L.InterneAuftraege.Verwaltung(befehl, ziel => ziel.KanalSperreAufheben(konto, netzId)), e => tcs.TrySetResult(e.Erfolg
                ? VerwaltungsAntwort.Gut($"{KanalText(Schreiber.Aktuell, netzId)} für {fuer} wieder frei.")
                : VerwaltungsAntwort.Fehler(e.Fehler == L.Fehlercode.Unbekannt
                    ? $"Auf {KanalText(Schreiber.Aktuell, netzId)} liegt für {fuer} keine Sperre. Nichts geändert."
                    : (e.Text ?? L.Drahtnamen.Von(e.Fehler)) + " Nichts geändert.")));
            return null;
        }

        long bis;
        if (Text(a, "bis") is { } bisText)
        {
            if (S.Auftragszeit.Lesen(bisText, jetzt) is not { } b) return VerwaltungsAntwort.Fehler("--bis: HH:MM oder JJJJ-MM-TT HH:MM. Nichts geändert.");
            bis = b;
        }
        else
        {
            bis = Math.Min(S.Auftragszeit.NaechsterMorgen(jetzt), jetzt + S.Kanalrechnung.SperreMaxMs);
        }
        Schreiben(L.InterneAuftraege.Verwaltung(befehl, ziel => ziel.KanalSperreSetzen(konto, netzId, bis)), e => tcs.TrySetResult(e.Erfolg
            ? VerwaltungsAntwort.Gut($"{KanalText(Schreiber.Aktuell, netzId)} für {fuer} gesperrt bis {S.Auftragszeit.Anzeigen(bis)}.")
            : VerwaltungsAntwort.Fehler((e.Text ?? L.Drahtnamen.Von(e.Fehler)) + " Nichts geändert.")));
        return null;
    }

    /// <summary>
    /// Der Kanal einer Konsolenangabe: <paramref name="netz"/> als Nummer oder Name (ohne Rücksicht auf Groß- und Kleinschreibung), mit
    /// <paramref name="ch"/> der Ch. eines Kanalnetzes (Kanal-ID), ohne das Netz selbst (bei einem Kanalnetz: alle Ch.). Sonst der Grund.
    /// </summary>
    private static (ushort? Id, string? Fehler) KanalAusNetzUndCh(L.Schnappschuss z, string? netz, uint? ch)
    {
        if (netz is null) return (null, "--netz fehlt (Nummer oder Name, siehe netzliste).");
        L.Netz? n = null;
        if (ushort.TryParse(netz, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            z.Netze.TryGetValue(id, out n);
        }
        else
        {
            // Neben einer Vorlage heißen oft mehrere Kanäle gleich (Kommando der alten Flotte und der Vorlage): Dann sperrte der erste nach
            // Nummer, und die Antwort nennte denselben Namen. Bei mehreren gleichen Namen nur mit der Nummer (Gegenprüfung 3.7).
            var gleich = z.Netze.Values.Where(x => string.Equals(x.Name, netz, StringComparison.OrdinalIgnoreCase)).ToList();
            if (gleich.Count > 1)
                return (null, $"Den Kanal '{netz}' gibt es {gleich.Count.ToString(CultureInfo.InvariantCulture)}-mal (Nummern "
                    + string.Join(", ", gleich.Select(x => x.Id.ToString(CultureInfo.InvariantCulture))) + "); gib die Nummer an (siehe netzliste).");
            n = gleich.FirstOrDefault();
        }
        if (n is null) return (null, $"Den Kanal '{netz}' gibt es nicht (siehe netzliste).");
        if (ch is not { } c) return (n.Id, null);
        if (n.Kanaele.IsEmpty || n.Id > S.Kanalrechnung.KanalnetzIdMax) return (null, $"{n.Name} hat keine Ch.; --ch geht nur an einem Kanalnetz.");
        if (c >= n.Kanaele.Length) return (null, $"{n.Name} hat nur Ch. 0 bis {(n.Kanaele.Length - 1).ToString(CultureInfo.InvariantCulture)}.");
        return (S.Kanalrechnung.Id(n.Id, (byte)c), null);
    }

    /// <summary>Wer eine Sperre gesetzt hat: 0 ist die Konsole.</summary>
    private string DurchText(uint durch) => durch == KonsoleDurch ? "Konsole" : KontoText(durch);

    /// <summary>Alle laufenden Kanalsperren, zuerst die für alle (<c>kontoId</c> 0), höchstens 500.</summary>
    private static S.KanalSperreAntwort KanalsperrListe(L.Schnappschuss z, long jetzt) => new()
    {
        Sperren = [.. z.SperrenAlle.Where(s => s.Bis > jetzt).Select(s => Abbildung.Kanalsperre(s, 0))
            .Concat(z.Konten.Values.SelectMany(k => k.Sperren.Where(s => s.Bis > jetzt).Select(s => Abbildung.Kanalsperre(s, k.Id))))
            .Take(S.Kanalrechnung.SperrenMax)],
    };

    /// <summary>Folgen der Kanalsperren (6.17): Journal und Meldung KANALSPERRE an alle Sitzungen mit <c>ZUWEISEN</c>.</summary>
    private void KanalsperrFolgenVerteilen(L.Ergebnis e)
    {
        var z = Schreiber.Aktuell;
        foreach (var f in e.Kanalsperren)
        {
            uint? konto = f.KontoId == 0 ? null : f.KontoId;
            Vermerken(f.Anlass == L.KanalsperrAnlass.Gesetzt ? "KANAL_GESPERRT" : "KANAL_FREI", f.Durch, konto, KanalsperrText(z, f));
            MeldungSenden(S.MeldungsArten.Kanalsperre, L.Recht.Zuweisen, konto, netzId: f.Sperre.NetzId, details: Antwort.DetailsBauen(w =>
            {
                w.WriteString("op", L.Drahtnamen.Von(f.Anlass));
                w.WriteNumber("bis", f.Sperre.Bis);
                w.WriteNumber("durch", f.Sperre.Durch);
                if (f.Grund is { } g) w.WriteString("warum", g);
            }));
        }
    }

    /// <summary>Journaltext einer Kanalsperre mit Kanal und Klarnamen, Zeiten in Ortszeit, höchstens 200 Zeichen.</summary>
    private string KanalsperrText(L.Schnappschuss z, L.KanalsperrFolge f)
    {
        var kanal = KanalText(z, f.Sperre.NetzId);
        var wer = f.KontoId == 0 ? "alle ohne Einsatzleitung" : KontoText(f.KontoId);
        var text = f.Anlass switch
        {
            L.KanalsperrAnlass.Gesetzt => $"{kanal} für {wer} gesperrt bis {S.Auftragszeit.Anzeigen(f.Sperre.Bis)} durch {DurchText(f.Durch)}",
            L.KanalsperrAnlass.Aufgehoben => $"{kanal} für {wer} wieder frei, aufgehoben durch {DurchText(f.Durch)}",
            L.KanalsperrAnlass.Abgelaufen => $"{kanal} für {wer} wieder frei, Sperre bis {S.Auftragszeit.Anzeigen(f.Sperre.Bis)} abgelaufen",
            _ when f.Grund == "NETZ" => $"{kanal} für {wer}: Sperre endet, Kanal gelöscht",
            _ => $"{kanal} für {wer} wieder frei: {DurchText(f.Sperre.Durch)} hat keine Einsatzleitung mehr",
        };
        return text.Length <= 200 ? text : text[..200];
    }

    /// <summary>"Brücke Ch. 3 (Schiff 3)", "Brücke (alle Ch.)", der Name eines gewöhnlichen Netzes oder "Kanal 77".</summary>
    internal static string KanalText(L.Schnappschuss z, ushort netzId)
    {
        ArgumentNullException.ThrowIfNull(z);
        if (S.Kanalrechnung.Zerlegen(netzId, out var n, out var c) && z.Netze.TryGetValue(n, out var kanalnetz))
        {
            var ch = c.ToString(CultureInfo.InvariantCulture);
            return c < kanalnetz.Kanaele.Length ? $"{kanalnetz.Name} Ch. {ch} ({kanalnetz.Kanaele[c]})" : $"{kanalnetz.Name} Ch. {ch}";
        }
        if (z.Netze.TryGetValue(netzId, out var netz)) return netz.Kanaele.IsEmpty ? netz.Name : netz.Name + " (alle Ch.)";
        return "Kanal " + netzId.ToString(CultureInfo.InvariantCulture);
    }
}
