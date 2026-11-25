// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Dienste;

/// <summary>
/// Meldesystem (0.12.0) in der Attrappe, wie ein freundlicher Server: Recht nach <see cref="KontoInfo.DarfMelden"/>, höchstens eine
/// Meldung in 2 s (nach <see cref="FunkDienstAttrappe.Uhr"/>), Art und Motiv gegen den <see cref="Meldungskatalog"/>, Countdown mit
/// Zielzeit in der Zukunft, Textmeldung mit Text, Farbe und Dauer (eine mit 8 s nur im Ereignis, eine bleibende auch in der Lage).
/// Jede Meldung setzt <see cref="FunkLage.Meldestand"/> und löst <see cref="MeldungEingegangen"/> aus wie der
/// Push 0x4005; die Bereit-Abfrage zählt die Funker der Lage, die online sind. Andere Funker melden über <see cref="BereitVon"/>.
/// </summary>
public sealed partial class FunkDienstAttrappe
{
    private readonly HashSet<uint> _bereit = [];
    private uint _abfrageNummer;
    private long? _letzteMeldungMs;

    public Task<string?> MeldungSendenAsync(MeldungSendenAnfrage anfrage)
    {
        ArgumentNullException.ThrowIfNull(anfrage);
        Merken("Meldung " + anfrage.Art + (anfrage.Motiv is { } m ? " " + m : "") + (anfrage.Groesse is { } g ? " x" + g.ToString(CultureInfo.InvariantCulture) : "")
            + (anfrage.Zeile is { } z ? " \"" + z + "\"" : "")
            + (anfrage.Text is { } tx ? " " + (anfrage.Farbe ?? "-") + " " + (anfrage.Dauer ?? "-") + " \"" + tx + "\"" : ""));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        var l = Lage;
        if (l.Konto is not { DarfMelden: true } ich)
            return Task.FromResult<string?>(FunkClientDienst.Meldefehler(Fehlercode.KeinRecht, "Meldungen senden nur mit Ernennung durch einen Admin und als Führung, Admin oder Einsatzleitung"));
        var jetzt = ServerJetztMs();
        if (_letzteMeldungMs is { } letzte && jetzt - letzte < Fristen.MeldungAbstandMs)
            return Task.FromResult<string?>(FunkClientDienst.Meldefehler(Fehlercode.Grenze, null));
        var alt = l.Meldestand ?? new Meldestand { Anlass = Meldungsvorgang.Stand };
        var zeile = string.IsNullOrWhiteSpace(anfrage.Zeile) ? null : anfrage.Zeile.Trim();
        Meldungseintrag Eintrag(string motiv, byte groesse) => new() { Motiv = motiv, Groesse = groesse, Zeile = zeile, Von = ich.Id, Seit = jetzt };
        var groesse = anfrage.Groesse ?? 1;
        Meldungseintrag? befehl = null;
        Textmeldung? text = null;
        var neu = alt with { Anlass = anfrage.Art, Von = ich.Id, Befehl = null };
        var bereitNeu = false;
        string? fehler = null;
        switch (anfrage.Art)
        {
            case Meldungsvorgang.Befehl:
                fehler = Motivfehler(anfrage.Motiv, Meldungsart.Befehl);
                if (fehler is null) befehl = Eintrag(anfrage.Motiv!, groesse);
                break;
            case Meldungsvorgang.Zustand:
                fehler = Motivfehler(anfrage.Motiv, Meldungsart.Zustand);
                if (fehler is null) neu = neu with { Zustand = Eintrag(anfrage.Motiv!, 1) };
                break;
            case Meldungsvorgang.ZustandAufheben:
                neu = neu with { Zustand = null };
                break;
            case Meldungsvorgang.Ampel:
                fehler = Motivfehler(anfrage.Motiv, Meldungsart.Ampel);
                if (fehler is null) neu = neu with { Ampel = Eintrag(anfrage.Motiv!, groesse) };
                break;
            case Meldungsvorgang.AmpelAus:
                neu = neu with { Ampel = null };
                break;
            case Meldungsvorgang.Countdown:
                if (Meldungskatalog.MotivFuerForm(anfrage.Form) is not { } cm) fehler = "Countdown braucht die Form Startzeit oder Restzeit";
                else if (anfrage.Ziel is not { } ziel || ziel <= jetzt) fehler = "Die Zielzeit ist schon vorbei";
                else if (ziel - jetzt > Grenzen.CountdownVorlaufMaxMs) fehler = "Die Zielzeit liegt mehr als 24 Stunden voraus";
                else neu = neu with { Countdown = Eintrag(cm, groesse) with { Ziel = ziel, Form = anfrage.Form } };
                break;
            case Meldungsvorgang.CountdownAbbrechen:
                neu = neu with { Countdown = null };
                break;
            case Meldungsvorgang.BereitAbfrage:
                fehler = Motivfehler(anfrage.Motiv ?? Meldungskatalog.BereitMotiv, Meldungsart.Bereit);
                if (fehler is null)
                {
                    _bereit.Clear();
                    neu = neu with { Abfrage = Eintrag(anfrage.Motiv ?? Meldungskatalog.BereitMotiv, groesse) with { Nummer = ++_abfrageNummer, Ziel = jetzt + Fristen.BereitAbfrageMs } };
                    bereitNeu = true;
                }
                break;
            case Meldungsvorgang.BereitSchliessen:
                if (alt.Abfrage is null) return Task.FromResult<string?>(null);
                neu = neu with { Abfrage = null };
                bereitNeu = true;
                break;
            case Meldungsvorgang.Text:
            {
                var farbe = anfrage.Farbe ?? Textmeldungen.Weiss;
                var dauer = anfrage.Dauer ?? Textmeldungen.Kurz;
                if (string.IsNullOrWhiteSpace(anfrage.Text)) fehler = "Textmeldung braucht einen Text";
                else if (!Textmeldungen.IstFarbe(farbe)) fehler = "unbekannte Farbe " + farbe;
                else if (!Textmeldungen.IstDauer(dauer)) fehler = "unbekannte Dauer " + dauer;
                else
                {
                    text = new Textmeldung { Text = anfrage.Text.Trim(), Farbe = farbe, Groesse = groesse, Dauer = dauer, Von = ich.Id, Seit = jetzt };
                    neu = neu with { Text = Textmeldungen.Bleibend(text) };
                }
                break;
            }
            case Meldungsvorgang.TextAufheben:
                neu = neu with { Text = null };
                break;
            default:
                fehler = "unbekannte Art " + anfrage.Art;
                break;
        }
        if (fehler is not null) return Task.FromResult<string?>(FunkClientDienst.Meldefehler(Fehlercode.Ungueltig, fehler));
        _letzteMeldungMs = jetzt;
        LageSetzen(x => x with
        {
            Meldestand = neu,
            BereitStand = bereitNeu ? BereitStandBauen(x, neu.Abfrage ?? alt.Abfrage!, offen: neu.Abfrage is not null) : x.BereitStand,
        });
        EreignisMelden(new MeldungEingegangen(neu with { Befehl = befehl, Text = text ?? neu.Text }));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> BereitMeldenAsync(uint nummer)
    {
        Merken("Bereit " + nummer.ToString(CultureInfo.InvariantCulture));
        if (Scheitern() is { } f) return Task.FromResult<string?>(f);
        return Task.FromResult(BereitVon(Lage.Konto?.Id ?? 0, nummer));
    }

    /// <summary>
    /// "Bereit" eines Kontos auf die offene Abfrage (Tests, Vorführung: andere Funker melden); der Bereit-Stand rechnet neu.
    /// <c>null</c> bei Erfolg, sonst der Satz wie vom Server.
    /// </summary>
    public string? BereitVon(uint kontoId, uint nummer)
    {
        if (Lage.Meldestand?.Abfrage is not { } a || a.Nummer != nummer)
            return FunkClientDienst.Meldefehler(Fehlercode.Unbekannt, "Keine offene Bereit-Abfrage mit dieser Nummer");
        if (_bereit.Add(kontoId)) LageSetzen(x => x with { BereitStand = BereitStandBauen(x, a, offen: true) });
        return null;
    }

    public long ServerJetztMs() => Uhr().ToUnixTimeMilliseconds() + (long)Lage.ZeitVersatz.TotalMilliseconds;

    /// <summary>
    /// Bereit-Stand wie der Server: online sind die Funker der Lage mit <c>online</c>, dazu das eigene Konto; wer die Abfrage gestartet hat,
    /// zählt nicht mit (weder in m noch unter "fehlt").
    /// </summary>
    private BereitStand BereitStandBauen(FunkLage l, Meldungseintrag abfrage, bool offen)
    {
        var online = l.Funker.Where(x => x.Online).Select(x => x.KontoId).Append(l.Konto?.Id ?? 0).Where(k => k != 0 && k != abfrage.Von).Distinct().ToList();
        return new BereitStand
        {
            Nummer = abfrage.Nummer ?? 0,
            Offen = offen,
            Bereit = (ushort)online.Count(_bereit.Contains),
            Online = (ushort)online.Count,
            Fehlen = [.. online.Where(k => !_bereit.Contains(k)).Order()],
            Von = abfrage.Von,
            Bis = abfrage.Ziel ?? abfrage.Seit,
        };
    }

    private static string? Motivfehler(string? motiv, Meldungsart art) => motiv is null ? "Motiv fehlt"
        : Meldungskatalog.Motiv(motiv) is not { } m ? "unbekanntes Motiv " + motiv
        : m.Art != art ? motiv + " passt nicht zur Art"
        : null;
}
