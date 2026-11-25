// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;

namespace Funk.Client.Modelle;

/// <summary>Vorrang einer Meldung: Fehler vor der Leitung (Phasen) vor einem Hinweis.</summary>
public enum Meldeart
{
    Hinweis,
    Leitung,
    Fehler,
}

/// <summary>
/// Eine Zeile der Meldezeile; <see cref="Bis"/> <c>long.MaxValue</c>, solange sie gehalten wird (Melden). <see cref="Ausrufe"/>: mit dem gelben "!" der
/// Auto Dauersend-Dämpfung davor (Klangstufe C2b). <see cref="Senden"/>: eine Zeile über das eigene Senden (Melden), grün, solange tatsächlich etwas
/// hinausgeht (Q41, <see cref="Meldezeile.Sendet"/>). <see cref="Entprellt"/>: ein Hinweis der Automatik, dessen Erscheinen die Meldezeile merkt
/// (dieselbe höchstens einmal je 5 min); Tasten, Fehler und Meldungen vom Server tragen es nicht.
/// </summary>
public sealed record Zeilenmeldung(string Schluessel, string Text, Meldeart Art, long Bis, long Seit, bool Ausrufe = false, bool Senden = false, bool Entprellt = false);

/// <summary>Die Zeiten der Meldezeile und der Phasentasten an einer Stelle (RAID-S2-ENTWURF Abschnitt 3); einstellbar erst später.</summary>
public static class Meldezeiten
{
    /// <summary>So lange bleibt eine Phase scharf; ein zweiter Druck derselben Taste schaltet bis dahin.</summary>
    public const int ScharfMs = 5000;

    /// <summary>Ein zweiter Druck zählt erst so lange nach dem ersten (Prellen, Doppeltipp).</summary>
    public const int ZweiterDruckAbMs = 300;

    /// <summary>Ohne Antwort so lange: "Keine Antwort - Stand folgt".</summary>
    public const int AntwortMs = 3000;

    public const int GeschaltetMs = 4000;
    public const int NichtGeschaltetMs = 5000;
    public const int OhneRechtMs = 3000;
    public const int KeinePhasenMs = 3000;
    public const int UmstellungMs = 6000;

    /// <summary>Die Ansage beim Login, wer einen vorab eingeteilt hat (seit 0.11.0): länger als eine Umstellung, sie kommt mitten im Einloggen.</summary>
    public const int AnsageMs = 12000;
    public const int EntzugMs = 5000;
    public const int SendenetzMs = 2000;
    public const int MeldenGehtNichtMs = 3000;
    public const int ErsteAblageMs = 6000;

    /// <summary>Rückfall des Rauschfilters Stark auf Standard (Klangstufe B); der Grund steht danach weiter unter Einstellungen > Senden (bis 0.9.3 Bereich Audio).</summary>
    public const int RauschfilterMs = 8000;

    /// <summary>
    /// Ein neues eigenes Urteil der Auto Dauersend-Dämpfung (Klangstufe C2b) mit Ursache und Hinweis; danach bleibt das gelbe "!" im Funkfeld, solange
    /// das Urteil gilt.
    /// </summary>
    public const int UrteilMs = 8000;

    /// <summary>Taste "Dauersender stumm" (Q8): wen sie stumm oder wieder normal geschaltet hat.</summary>
    public const int DauersenderTasteMs = 3000;

    /// <summary>Taste "Mikro stumm" (Q32): stumm oder wieder an.</summary>
    public const int MikroTasteMs = 2500;

    /// <summary>Sprechtaste bei stummem Mikro (Q31): kurz "Mikro ist stumm".</summary>
    public const int MikroIstStummMs = 2000;

    /// <summary>Gesprochen, aber nichts geht hinaus (ui-fix 0.6.2): "Ruhe: es wird nichts gesendet".</summary>
    public const int NichtsGesendetMs = 2500;

    /// <summary>Die Probe unter Einstellungen (seit 0.11.1): Ein Klick auf ein Feld des Rasters zeigt die Meldezeile 3 s an ihrer Stelle.</summary>
    public const int ProbeMs = 3000;

    /// <summary>
    /// Dieselbe Meldung der Automatik (gleicher Schlüssel und Text) erscheint höchstens einmal in dieser Zeit (seit 0.11.1, Sarevoc 12.10.2026: "nicht
    /// immerzu spammen"), gezählt ab ihrem Erscheinen. Ausgenommen sind, was eine Taste beantwortet und was der Server meldet
    /// (<c>Meldezeile.Zeigen</c> mit <c>immer</c>) sowie jeder Fehler (Befund der Prüfung 0.11.1).
    /// </summary>
    public const int WiederholungMs = 300_000;
}

/// <summary>
/// Die Meldezeile (RAID-S2-ENTWURF Abschnitt 3): kurze Sätze für die Leitung (Phase scharf, geschaltet, nicht geschaltet) und
/// für jeden Funker (Umstellung, Entzug, Sendenetz, Melden, erste Ablage), auch wenn das Funkfeld abgelegt ist. Jede Meldung hat einen
/// Schlüssel (derselbe ersetzt die alte), einen Vorrang (Fehler vor Leitung vor Hinweis) und eine Dauer an der Uhr des Aufrufers
/// (Tests stellen sie vor); gehaltene Meldungen (Melden) stehen, bis sie gelöst werden. Das Fenster dazu (<c>MeldezeileFenster</c>)
/// zeigt sich nur, solange <see cref="Sichtbar"/>. Nur auf dem Oberflächen-Thread.
/// <para>
/// Seit 0.11.0 (Sarevoc, 12.10.2026: "maximal einzeilig, das Funkfeld muss klein bleiben") ist jede Meldung genau eine Zeile: Ein
/// Zeilenumbruch im Text wird zum Leerzeichen (<see cref="Einzeilig"/>), das Fenster bricht nie um und kürzt mit "...", höchstens so
/// breit wie das Funkfeld (<c>Meldezeilenlage.Hoechstbreite</c>). Die Sätze selbst sind kurz; die ausführliche Fassung steht dort,
/// wo es einen Ort dafür gibt (Kacheln, Hilfetext oben links, das "!" im Funkfeld, Einstellungen).
/// </para>
/// <para>
/// Und es steht immer nur <b>eine</b> Meldung da (Sarevoc Q112, 12.10.2026; bis dahin höchstens drei untereinander): Eine neue Meldung
/// gleichen oder höheren Rangs ersetzt die stehende; eine niedrigere wartet, bis die stehende abläuft oder gelöst wird, und verfällt,
/// wenn ihre eigene Dauer bis dahin abgelaufen ist (sie läuft ab ihrem Eingang, nicht ab ihrem Erscheinen). Warten mehrere, kommt die
/// ranghöchste, bei gleichem Rang die neueste. Derselbe Schlüssel ersetzt wie bisher, auch eine wartende. Eine gehaltene Meldung
/// (Melden) ist ein Zustand, kein Ereignis: Verdrängt, wartet sie und steht wieder da, solange die Taste gehalten ist.
/// </para>
/// <para>
/// Kein Dauerfeuer (seit 0.11.1, Sarevoc 12.10.2026: "nicht immerzu spammen"): Dieselbe Meldung der Automatik, gleicher Schlüssel und gleicher
/// Text, erscheint höchstens einmal je <see cref="Meldezeiten.WiederholungMs"/> (5 min), gezählt ab dem letzten Erscheinen (nicht ab dem
/// Versuch: Eine wartende Meldung, die ungesehen verfällt, sperrt nichts); was dazwischen kommt, fällt still weg (<see cref="Zeigen"/>).
/// Entprellt sind nur die Hinweise, die von selbst entstehen (Ruhe und die Sätze daneben, das Urteil des Wächters, der Rauschfilter).
/// Jedes Mal erscheint, was eine Taste ohne Bild beantwortet (Phasentasten, Mikro stumm, Dauersender stumm, Sendekanal in der Ablage,
/// Melden geht nicht), die Probe unter Einstellungen und alles, was der Server meldet (Einteilung, Phase, Kanäle, Entzug): Sie rufen
/// <see cref="Zeigen"/> mit <c>immer</c>, denn derselbe Stand, der binnen 5 min zurückkehrt, ist eine neue Nachricht (Befund der
/// Prüfung 0.11.1). Ein Fehler (<see cref="Meldeart.Fehler"/>) erscheint immer. Eine gehaltene Meldung ist ein Zustand und zählt nicht.
/// </para>
/// </summary>
public sealed partial class Meldezeile : ObservableObject
{
    /// <summary>Eine Meldung, eine Zeile (Sarevoc Q112); bis Q112 standen drei untereinander.</summary>
    public const int ZeilenMax = 1;

    /// <summary>Schlüssel der Probe aus den Einstellungen (seit 0.11.1).</summary>
    public const string ProbeSchluessel = "probe";

    /// <summary>Der Satz der Probe: zeigt, wo die Meldezeile steht.</summary>
    public const string ProbeText = "Hier steht die Meldezeile";

    /// <summary>
    /// Was überhaupt über dem Spiel erscheint (seit 0.11.2, Sarevoc 13.10.2026: "ich will nur eine Overlay-Meldung, wenn ein Admin oder Raidleiter die
    /// absetzt oder einer funkt, und wo diese Meldung kommt, muss fest eingestellt werden, und immer nur eine Meldung"): was die Leitung tut
    /// (Einteilung, Kanäle, Phase, Ansage beim Login, Entzug des Senderechts, die Phasentasten der Leitung), das eigene Funken mit der
    /// Melde-Taste und die Probe unter Einstellungen, mit der man den Platz festlegt. Alles andere (Mikro stumm, Sendekanal, Dauersender,
    /// Urteil des Wächters, Rauschfilter, erste Ablage, "es wird nichts gesendet") fällt still weg.
    /// </summary>
    public static readonly IReadOnlySet<string> Erlaubt = new HashSet<string>(StringComparer.Ordinal)
    {
        Teilnehmermeldungen.UmstellungSchluessel, Teilnehmermeldungen.AnsageSchluessel, Teilnehmermeldungen.EntzugSchluessel,
        Teilnehmermeldungen.MeldenSchluessel, Phasensteuerung.Schluessel, ProbeSchluessel,
    };

    private readonly Func<long> _uhr;

    /// <summary>Wann eine entprellte Meldung (Schlüssel, Text) zuletzt erschienen ist, für die Entprellung.</summary>
    private readonly Dictionary<(string Schluessel, string Text), long> _erschienen = [];

    /// <summary>Die entprellte Meldung, deren Erscheinen zuletzt gemerkt wurde (dieselbe stehende zählt nur einmal).</summary>
    private Zeilenmeldung? _zuletztErschienen;

    /// <summary>Die gezeigte Meldung.</summary>
    private Zeilenmeldung? _steht;

    /// <summary>Die wartenden, in der Reihenfolge ihres Eingangs (bei gleicher Uhr entscheidet sie, welche die neuere ist).</summary>
    private readonly List<Zeilenmeldung> _wartend = [];

    public Meldezeile(Func<long> uhrMs)
    {
        ArgumentNullException.ThrowIfNull(uhrMs);
        _uhr = uhrMs;
    }

    /// <summary>Der Satz der gezeigten Meldung, leer ohne Meldung.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    /// <summary>Die gezeigte Meldung ist ein Fehler (rote Schrift der Farbstellung).</summary>
    [ObservableProperty]
    public partial bool HatFehler { get; private set; }

    /// <summary>Q41: Es geht gerade etwas hinaus (das Funkfeld setzt es im Bildtakt); Zeilen über das eigene Senden stehen dann grün.</summary>
    [ObservableProperty]
    public partial bool Sendet { get; set; }

    /// <summary>Die gezeigte Meldung als Liste für das Fenster: leer oder genau eine (<see cref="ZeilenMax"/>).</summary>
    public IReadOnlyList<Zeilenmeldung> Zeilen { get; private set; } = [];

    /// <summary>
    /// Zeigt <paramref name="text"/> für <paramref name="dauerMs"/>; eine Meldung mit demselben Schlüssel wird ersetzt. Mit <paramref name="ausrufe"/>
    /// steht das gelbe "!" davor (Klangstufe C2b). Ein Hinweis der Automatik (Schlüssel und Text) erscheint höchstens einmal je
    /// <see cref="Meldezeiten.WiederholungMs"/> (seit 0.11.1); mit <paramref name="immer"/> (die Antwort auf eine Taste, die Probe, eine Meldung
    /// vom Server) jedes Mal, ein Fehler ebenso.
    /// </summary>
    public void Zeigen(string schluessel, string text, Meldeart art, int dauerMs, bool ausrufe = false, bool immer = false)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        ArgumentNullException.ThrowIfNull(text);
        if (!Erlaubt.Contains(schluessel)) return;
        var jetzt = _uhr();
        // Befund der Prüfung 0.11.1: Ein Fehler fällt nie still weg (ein zweiter Entzug des Senderechts binnen 5 min wäre sonst unsichtbar).
        var entprellt = !immer && art != Meldeart.Fehler;
        if (entprellt && !Wiederholbar(schluessel, Einzeilig(text), jetzt)) return;
        Setzen(new Zeilenmeldung(schluessel, text, art, jetzt + Math.Max(0, dauerMs), jetzt, ausrufe, Entprellt: entprellt));
    }

    /// <summary>
    /// Ob die Meldung jetzt erscheinen darf: nicht, wenn dieselbe binnen <see cref="Meldezeiten.WiederholungMs"/> schon erschienen ist. Gemerkt
    /// wird das Erscheinen in <see cref="Neu"/>, nicht der Versuch: Eine wartende Meldung, die ungesehen verfällt, sperrt nichts, und ein
    /// unterdrückter Versuch verlängert die Sperre nicht. Abgelaufene Einträge fallen dabei weg.
    /// </summary>
    private bool Wiederholbar(string schluessel, string text, long jetzt)
    {
        foreach (var (k, t) in _erschienen)
            if (jetzt - t >= Meldezeiten.WiederholungMs) _erschienen.Remove(k);
        return !_erschienen.TryGetValue((schluessel, text), out var zuletzt) || jetzt - zuletzt >= Meldezeiten.WiederholungMs;
    }

    /// <summary>
    /// Zeigt <paramref name="text"/>, bis <see cref="Loesen"/> kommt (etwa solange die Melde-Taste gehalten ist); mit <paramref name="senden"/> eine
    /// Zeile über das eigene Senden (grün, solange <see cref="Sendet"/>).
    /// </summary>
    public void Halten(string schluessel, string text, Meldeart art, bool senden = false)
    {
        if (!Erlaubt.Contains(schluessel)) return;
        Setzen(new Zeilenmeldung(schluessel, text, art, long.MaxValue, _uhr(), Senden: senden));
    }

    /// <summary>Nimmt die Meldung mit diesem Schlüssel fort, stehend oder wartend; stand sie, rückt die nächste wartende nach.</summary>
    public void Loesen(string schluessel)
    {
        var vorher = _steht;
        Abraeumen(_uhr());
        _wartend.RemoveAll(m => Gleich(m, schluessel));
        if (_steht is { } s && Gleich(s, schluessel)) _steht = null;
        Nachruecken();
        if (!ReferenceEquals(vorher, _steht)) Neu();
    }

    /// <summary>Ob eine Meldung mit diesem Schlüssel steht oder noch wartet (seit Q112 steht nur eine).</summary>
    public bool Zeigt(string schluessel) => (_steht is { } s && Gleich(s, schluessel)) || _wartend.Any(m => Gleich(m, schluessel));

    /// <summary>Abgelaufenes entfernen, die nächste wartende nachrücken (im Bildtakt; ohne Änderung kostet es nichts).</summary>
    public void Takt()
    {
        var vorher = _steht;
        Abraeumen(_uhr());
        Nachruecken();
        if (!ReferenceEquals(vorher, _steht)) Neu();
    }

    /// <summary>
    /// Eine Meldung als eine Zeile (seit 0.11.0): Zeilenumbrüche (auch aus fremden Sätzen, etwa einem Sperrgrund) werden zu einem
    /// Leerzeichen, denn ein TextBlock bricht an ihnen auch ohne Umbruch um.
    /// </summary>
    public static string Einzeilig(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOfAny(Umbrueche) < 0) return text;
        return string.Join(' ', text.Split(Umbrueche, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static readonly char[] Umbrueche = ['\r', '\n', '\u000B', '\u000C', '\u0085', (char)0x2028, (char)0x2029];

    /// <summary>
    /// Q112: Derselbe Schlüssel fällt fort, stehend oder wartend. Gleicher oder höherer Rang als die stehende ersetzt sie (eine gehaltene
    /// wartet dann), ein niedrigerer wartet. Steht nichts, kommt die ranghöchste von allen.
    /// </summary>
    private void Setzen(Zeilenmeldung m)
    {
        ArgumentNullException.ThrowIfNull(m.Schluessel);
        ArgumentNullException.ThrowIfNull(m.Text);
        m = m with { Text = Einzeilig(m.Text) };
        Abraeumen(m.Seit);
        _wartend.RemoveAll(x => Gleich(x, m.Schluessel));
        if (_steht is { } alt && Gleich(alt, m.Schluessel)) _steht = null;
        if (_steht is not { } steht)
        {
            _wartend.Add(m);
            Nachruecken();
        }
        else if (m.Art >= steht.Art)
        {
            if (steht.Bis == long.MaxValue) _wartend.Add(steht);
            _steht = m;
        }
        else
        {
            _wartend.Add(m);
        }
        Neu();
    }

    /// <summary>Was bis <paramref name="jetzt"/> abgelaufen ist, fällt fort, stehend wie wartend (eine wartende verfällt ungesehen).</summary>
    private void Abraeumen(long jetzt)
    {
        _wartend.RemoveAll(m => m.Bis <= jetzt);
        if (_steht is { } s && s.Bis <= jetzt) _steht = null;
    }

    /// <summary>Steht nichts, kommt die ranghöchste wartende, bei gleichem Rang die neueste (spätere Uhr, sonst später eingegangen).</summary>
    private void Nachruecken()
    {
        if (_steht is not null || _wartend.Count == 0) return;
        var beste = 0;
        for (var i = 1; i < _wartend.Count; i++)
        {
            var w = _wartend[i];
            var b = _wartend[beste];
            if (w.Art > b.Art || (w.Art == b.Art && w.Seit >= b.Seit)) beste = i;
        }
        _steht = _wartend[beste];
        _wartend.RemoveAt(beste);
    }

    private static bool Gleich(Zeilenmeldung m, string schluessel) => string.Equals(m.Schluessel, schluessel, StringComparison.Ordinal);

    /// <summary>Der neue Stand an die Oberfläche; steht jetzt eine entprellte Meldung, gilt das als ihr Erscheinen (einmal je Meldung).</summary>
    private void Neu()
    {
        if (_steht is { Entprellt: true } e && !ReferenceEquals(e, _zuletztErschienen))
        {
            _erschienen[(e.Schluessel, e.Text)] = _uhr();
            _zuletztErschienen = e;
        }
        Zeilen = _steht is { } s ? [s] : [];
        OnPropertyChanged(nameof(Zeilen));
        Text = _steht?.Text ?? "";
        HatFehler = _steht?.Art == Meldeart.Fehler;
        Sichtbar = _steht is not null;
    }
}
