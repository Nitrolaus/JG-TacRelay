// JG TacRelay - Maintainer: Sarevoc
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Was eine Meldung bewirkt hat. <see cref="Fehler"/> mit <see cref="Grund"/> (Klartext für <c>Fehler.text</c>), sonst
/// <see cref="Push"/> (der neue <see cref="S.Meldestand"/> an alle, <c>null</c>: nichts zu senden), <see cref="BereitOffen"/> (eine
/// Bereit-Abfrage begann, der Bereit-Stand geht an die Sendeberechtigten) oder <see cref="BereitEnde"/> (sie ist zu, mit dem letzten
/// Stand).
/// </summary>
internal sealed record Meldeergebnis(S.Fehlercode? Fehler = null, string? Grund = null)
{
    public S.Meldestand? Push { get; init; }

    public bool BereitOffen { get; init; }

    public Bereitende? BereitEnde { get; init; }

    public static Meldeergebnis Fehl(string grund) => new(S.Fehlercode.Ungueltig, grund);
}

/// <summary>Eine geschlossene Bereit-Abfrage und wer bis dahin bereit war, für den letzten <see cref="S.BereitStand"/>.</summary>
internal sealed record Bereitende(S.Meldungseintrag Abfrage, IReadOnlySet<uint> Bereite);

/// <summary>
/// Der laufende Stand des Meldesystems (0.12.0, M19, M20, M24) im Speicher der Zentrale: Zustand, Ampel, Countdown, die offene
/// Bereit-Abfrage samt den Konten, die bereit gemeldet haben, und eine bleibende Textmeldung. Befehle und Textmeldungen mit 8 s
/// bleiben nicht stehen (wer später kommt, sieht keine vergangenen). Nur in der Arbeitsschleife; die Zeit kommt als Parameter
/// (Unix-ms, Serverzeit), damit Tests sie stellen. Nach einem Neustart des Servers ist alles leer. Das Recht prüft die Zentrale
/// vorher (<see cref="S.Melderecht"/>).
/// </summary>
internal sealed class Meldesystem
{
    private HashSet<uint> _bereit = [];
    private uint _nummer;

    /// <summary>Die laufende Textmeldung, die stehen bleibt (M24, Dauer "bleibt"); eine mit 8 s steht nur im Push ihres Anlasses.</summary>
    public S.Textmeldung? Text { get; private set; }

    public S.Meldungseintrag? Zustand { get; private set; }

    public S.Meldungseintrag? Ampel { get; private set; }

    public S.Meldungseintrag? Countdown { get; private set; }

    /// <summary>Die offene Bereit-Abfrage; <c>Nummer</c> trägt ihre Nummer, <c>Ziel</c> ihr spätestes Ende.</summary>
    public S.Meldungseintrag? Abfrage { get; private set; }

    /// <summary>Konten, die auf die offene Abfrage bereit gemeldet haben.</summary>
    public IReadOnlySet<uint> Bereite => _bereit;

    /// <summary>
    /// Eine Meldung anwenden (0x0026). Prüft Art, Motiv, Form und Zielzeit gegen den <see cref="S.Meldungskatalog"/>; ein Verstoß ändert
    /// nichts und kommt als UNGUELTIG mit Klartext zurück.
    /// </summary>
    public Meldeergebnis Senden(uint von, S.MeldungSendenAnfrage a, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(a);
        var groesse = a.Groesse ?? 1;
        var zeile = string.IsNullOrWhiteSpace(a.Zeile) ? null : a.Zeile.Trim();
        S.Meldungseintrag Eintrag(string motiv, byte g) => new() { Motiv = motiv, Groesse = g, Zeile = zeile, Von = von, Seit = jetzt };
        switch (a.Art)
        {
            case S.Meldungsvorgang.Befehl:
                if (MotivFehler(a.Motiv, S.Meldungsart.Befehl, "Befehl") is { } fb) return fb;
                return Ergebnis(a.Art, von, jetzt, befehl: Eintrag(a.Motiv!, groesse));
            case S.Meldungsvorgang.Zustand:
                if (MotivFehler(a.Motiv, S.Meldungsart.Zustand, "Zustand") is { } fz) return fz;
                // Zustände bleiben immer klein (M21).
                Zustand = Eintrag(a.Motiv!, 1);
                return Ergebnis(a.Art, von, jetzt);
            case S.Meldungsvorgang.ZustandAufheben:
                Zustand = null;
                return Ergebnis(a.Art, von, jetzt);
            case S.Meldungsvorgang.Ampel:
                if (MotivFehler(a.Motiv, S.Meldungsart.Ampel, "Ampel") is { } fa) return fa;
                Ampel = Eintrag(a.Motiv!, groesse);
                return Ergebnis(a.Art, von, jetzt);
            case S.Meldungsvorgang.AmpelAus:
                Ampel = null;
                return Ergebnis(a.Art, von, jetzt);
            case S.Meldungsvorgang.Countdown:
            {
                if (S.Meldungskatalog.MotivFuerForm(a.Form) is not { } motiv) return Meldeergebnis.Fehl("Countdown braucht die Form Startzeit oder Restzeit");
                if (a.Motiv is { } m && !string.Equals(m, motiv, StringComparison.Ordinal)) return Meldeergebnis.Fehl(Kurz(m) + " passt nicht zur Form " + a.Form);
                if (a.Ziel is not { } ziel) return Meldeergebnis.Fehl("Countdown braucht eine Zielzeit");
                if (ziel <= jetzt) return Meldeergebnis.Fehl("Die Zielzeit ist schon vorbei");
                if (ziel - jetzt > S.Grenzen.CountdownVorlaufMaxMs) return Meldeergebnis.Fehl("Die Zielzeit liegt mehr als 24 Stunden voraus");
                Countdown = Eintrag(motiv, groesse) with { Ziel = ziel, Form = a.Form };
                return Ergebnis(a.Art, von, jetzt);
            }
            case S.Meldungsvorgang.CountdownAbbrechen:
                Countdown = null;
                return Ergebnis(a.Art, von, jetzt);
            case S.Meldungsvorgang.BereitAbfrage:
            {
                var motiv = a.Motiv ?? S.Meldungskatalog.BereitMotiv;
                if (MotivFehler(motiv, S.Meldungsart.Bereit, "Bereit-Abfrage") is { } fr) return fr;
                // Eine neue Abfrage ersetzt die laufende: neue Nummer, niemand ist bereit.
                if (++_nummer == 0) _nummer = 1;
                _bereit = [];
                Abfrage = Eintrag(motiv, groesse) with { Nummer = _nummer, Ziel = jetzt + S.Fristen.BereitAbfrageMs };
                return Ergebnis(a.Art, von, jetzt) with { BereitOffen = true };
            }
            case S.Meldungsvorgang.BereitSchliessen:
                // Schon zu (von selbst abgelaufen oder von jemand anderem geschlossen): nichts zu tun, kein Fehler.
                if (Schliessen() is not { } ende) return new Meldeergebnis();
                return Ergebnis(a.Art, von, jetzt) with { BereitEnde = ende };
            case S.Meldungsvorgang.Text:
            {
                // Textmeldung (M24): nur Text, Farbe und Dauer aus der festen Liste; Motiv und Zeile zählen hier nicht.
                var text = a.Text?.Trim();
                if (string.IsNullOrEmpty(text)) return Meldeergebnis.Fehl("Textmeldung braucht einen Text");
                var farbe = a.Farbe ?? S.Textmeldungen.Weiss;
                if (!S.Textmeldungen.IstFarbe(farbe)) return Meldeergebnis.Fehl("unbekannte Farbe " + Kurz(farbe));
                var dauer = a.Dauer ?? S.Textmeldungen.Kurz;
                if (!S.Textmeldungen.IstDauer(dauer)) return Meldeergebnis.Fehl("unbekannte Dauer " + Kurz(dauer));
                var t = new S.Textmeldung { Text = text, Farbe = farbe, Groesse = groesse, Dauer = dauer, Von = von, Seit = jetzt };
                // Eine neue ersetzt die laufende, gleich von wem; zum Stand gehört nur eine, die stehen bleibt.
                Text = S.Textmeldungen.Bleibend(t);
                return Ergebnis(a.Art, von, jetzt, text: t);
            }
            case S.Meldungsvorgang.TextAufheben:
                Text = null;
                return Ergebnis(a.Art, von, jetzt);
            default:
                return Meldeergebnis.Fehl("unbekannte Art " + Kurz(a.Art));
        }
    }

    /// <summary>
    /// "Bereit" (0x0027) eines Kontos auf die offene Abfrage mit <paramref name="nummer"/>. <c>null</c>: keine solche Abfrage
    /// (UNBEKANNT); sonst, ob sich der Stand geändert hat (ein zweites Bereit ändert nichts).
    /// </summary>
    public bool? Bereit(uint konto, uint nummer, long jetzt)
    {
        if (Abfrage is not { } a || a.Nummer != nummer || a.Ziel <= jetzt) return null;
        return _bereit.Add(konto);
    }

    /// <summary>
    /// Takt der Zentrale: Ein Countdown nach seinem Ziel fällt still aus dem Stand (die Clients blenden ihn bei 0 selbst aus), eine
    /// Bereit-Abfrage endet nach 2 min mit Anlass <c>BereitAbgelaufen</c>. <c>null</c>: nichts zu senden.
    /// </summary>
    public Meldeergebnis? Takt(long jetzt)
    {
        if (Countdown is { Ziel: { } ziel } && ziel <= jetzt) Countdown = null;
        if (Abfrage is not { Ziel: { } bis } || bis > jetzt) return null;
        var ende = Schliessen()!;
        return new Meldeergebnis { Push = StandBauen(S.Meldungsvorgang.BereitAbgelaufen, null, null, jetzt), BereitEnde = ende };
    }

    /// <summary>
    /// Ob etwas läuft, das ein später Kommender sehen soll (M20): Zustand, Ampel, Countdown vor dem Ziel, offene Abfrage, eine bleibende
    /// Textmeldung.
    /// </summary>
    public bool HatStand(long jetzt) =>
        Zustand is not null || Ampel is not null || Countdown is { Ziel: { } z } && z > jetzt || Abfrage is not null || Text is not null;

    /// <summary>
    /// Der ganze laufende Stand als Push; ein Countdown nach seinem Ziel fehlt. <paramref name="text"/> ist die gerade gesendete
    /// Textmeldung (auch eine mit 8 s), sonst steht die bleibende darin.
    /// </summary>
    public S.Meldestand StandBauen(string anlass, uint? von, S.Meldungseintrag? befehl, long jetzt, S.Textmeldung? text = null) => new()
    {
        Anlass = anlass,
        Von = von,
        Befehl = befehl,
        Zustand = Zustand,
        Ampel = Ampel,
        Countdown = Countdown is { Ziel: { } z } c && z > jetzt ? c : null,
        Abfrage = Abfrage,
        Text = text ?? Text,
    };

    /// <summary>
    /// "n von m bereit" (M3-M14): <paramref name="online"/> sind die Konten der aktiven Sitzungen, <c>bereit</c> zählt nur die davon, die
    /// bereit gemeldet haben; <c>fehlen</c> nennt die übrigen, aufsteigend nach Konto. Wer die Abfrage gestartet hat
    /// (<see cref="S.Meldungseintrag.Von"/>), zählt nicht mit, weder in m noch unter "fehlt" (Schritt 2 des Meldesystems, 16.10.2026: er
    /// fragt, er meldet sich nicht selbst bereit).
    /// </summary>
    public static S.BereitStand BereitStandBauen(S.Meldungseintrag abfrage, IReadOnlySet<uint> bereite, IReadOnlyCollection<uint> online, bool offen)
    {
        ArgumentNullException.ThrowIfNull(abfrage);
        ArgumentNullException.ThrowIfNull(bereite);
        ArgumentNullException.ThrowIfNull(online);
        var gezaehlt = online.Where(k => k != abfrage.Von).ToList();
        var fehlen = gezaehlt.Where(k => !bereite.Contains(k)).Order().Take(S.Grenzen.KontenMax).ToList();
        return new S.BereitStand
        {
            Nummer = abfrage.Nummer ?? 0,
            Offen = offen,
            Bereit = (ushort)Math.Min(gezaehlt.Count(bereite.Contains), ushort.MaxValue),
            Online = (ushort)Math.Min(gezaehlt.Count, ushort.MaxValue),
            Fehlen = fehlen,
            Von = abfrage.Von,
            Bis = abfrage.Ziel ?? abfrage.Seit,
        };
    }

    private Bereitende? Schliessen()
    {
        if (Abfrage is not { } a) return null;
        var ende = new Bereitende(a, _bereit);
        Abfrage = null;
        _bereit = [];
        return ende;
    }

    private Meldeergebnis Ergebnis(string anlass, uint von, long jetzt, S.Meldungseintrag? befehl = null, S.Textmeldung? text = null) =>
        new() { Push = StandBauen(anlass, von, befehl, jetzt, text) };

    /// <summary>Motiv fehlt, ist unbekannt oder von einer anderen Art: Klartext, sonst <c>null</c>.</summary>
    private static Meldeergebnis? MotivFehler(string? motiv, S.Meldungsart art, string was)
    {
        if (motiv is null) return Meldeergebnis.Fehl(was + " braucht ein Motiv");
        if (S.Meldungskatalog.Motiv(motiv) is not { } m) return Meldeergebnis.Fehl("unbekanntes Motiv " + Kurz(motiv));
        return m.Art == art ? null : Meldeergebnis.Fehl(motiv + " ist kein Motiv für " + was);
    }

    private static string Kurz(string? text) => text is null ? "" : text.Length <= S.Grenzen.MotivKennungMax ? text : text[..S.Grenzen.MotivKennungMax];
}
