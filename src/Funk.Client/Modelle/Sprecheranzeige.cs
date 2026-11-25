// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Die Sprecheranzeige über dem Spiel (seit 0.11.9, Sarevoc 11.10. und 15.10.2026, SPRECHERANZEIGE-ENTWURF.md): Solange jemand zu hören ist,
/// steht sein Name fett und dahinter normal der Kanal mit Ch. ("Brücke (Schiff 1)", <see cref="FunkLage.KanalName"/>), eine Durchsage als
/// "Durchsage an alle" oder "Durchsage: Kanal (Ch.)", Global als "Global"; ohne Zeichen, ohne Fläche, ohne Farben, weiße Schrift mit Schatten.
/// <list type="bullet">
/// <item>Woher: die <see cref="KlangBruecke"/> meldet je Sprecher, der gerade spricht, Konto, Art und Netze aus dem Stempel seines letzten Pakets
/// (<see cref="Melden"/>, auf dem Oberflächen-Thread). Kein Server-Umbau, kein neues Recht, nicht die Sprechanzeige 0x0053.</item>
/// <item>Nur wer wirklich gehört wird: In einem Netz zählen nur die eigenen Felder mit "hören" (abgeschaltete nicht, Ruhe und gesperrte auch
/// nicht; der Server stempelt diese ohnehin nicht), ein Funker, den du am gelben Zeichen stumm geschaltet hast, gar nicht. Durchsage und Global hört man auch in einem abgeschalteten Kanal
/// (FUNKPROTOKOLL 6.6), sie stehen also da. Die eigene Sendung nur mit dem Schalter, als "Du -> Kanal (Ch.)".</item>
/// <item>Reihenfolge: wer zuerst sprach, steht oben. Höchstens so viele Zeilen wie eingestellt (ab Werk 1); sprechen mehr, steht bei mindestens 2
/// Zeilen in der letzten "+n weitere", bei 1 Zeile genau ein Name. Hört der Oberste auf, rückt nach dem Nachhalten der nächste nach.</item>
/// <item>Zeit: sofort mit der ersten Meldung, bis <see cref="SprecheranzeigeEinstellungen.NachhaltenMs"/> nach der letzten voll, dann in
/// <see cref="SprecheranzeigeEinstellungen.AusblendenMs"/> aus. Die Maschine meldet alle 40 ms; ein Sprecher gilt darum noch
/// <see cref="MeldeLueckeMs"/> nach seiner letzten Meldung als gehört, auch mit 0 s Nachhalten. Keine Daueranimation: Der Takt ändert nur
/// während des Ausblendens etwas.</item>
/// </list>
/// Jedes Schieben an den Reglern der Lage zeigt die Probezeile genau dort, bis <see cref="ProbeMs"/> nach dem letzten Schieben
/// (<see cref="ProbeZeigen"/>; bis zu Sarevocs Änderung vom 15.10.2026 ein Klick ins Raster, Q91). Das Bild
/// (<see cref="Bild"/>) teilt sich die Vorlage mit der Vorschau in den Einstellungen; das Fenster ist <c>Fenster.SprecheranzeigeFenster</c>.
/// </summary>
public sealed partial class Sprecheranzeige : ObservableObject
{
    /// <summary>So lange gilt ein Sprecher nach seiner letzten Meldung noch als gehört (die Maschine meldet alle 40 ms, dazu der Bildtakt).</summary>
    public const long MeldeLueckeMs = 120;

    /// <summary>So lange steht die Probezeile nach dem letzten Schieben an den Reglern der Lage.</summary>
    public const long ProbeMs = 3000;

    /// <summary>Die Probezeile: Name fett, dahinter der Satz.</summary>
    public const string ProbeName = "Probe";

    public const string ProbeSatz = "So steht hier, wer spricht";

    /// <summary>Der Name der eigenen Zeile.</summary>
    public const string DuName = "Du";

    private readonly IFunkDienst _dienst;
    private readonly Func<SprecheranzeigeEinstellungen> _einstellungen;
    private readonly Func<long> _uhr;
    private readonly Func<uint, bool> _stumm;
    private readonly Func<IReadOnlyList<ushort>?> _eigeneSendung;
    private readonly List<Eintrag> _eintraege = [];
    private long _probeBis = long.MinValue;

    private sealed class Eintrag(string schluessel, string name, string rest, long zuletzt)
    {
        public string Schluessel { get; } = schluessel;
        public string Name { get; set; } = name;
        public string Rest { get; set; } = rest;
        public long Zuletzt { get; set; } = zuletzt;
    }

    /// <param name="einstellungen">Die gemerkten Einstellungen, je Aufruf gelesen (im Betrieb die der Ablage).</param>
    /// <param name="uhr">Millisekunden (im Betrieb <c>Environment.TickCount64</c>, in Tests eine Attrappe).</param>
    /// <param name="stumm">Ob du einen Funker am gelben Zeichen stumm geschaltet hast; ohne Angabe keiner.</param>
    /// <param name="eigeneSendung">Die Netze der eigenen offenen Phase (<see cref="KlangBruecke.EigeneSendung"/>), sonst <c>null</c>; ohne Angabe nie.</param>
    public Sprecheranzeige(IFunkDienst dienst, Func<SprecheranzeigeEinstellungen> einstellungen, Func<long> uhr, Func<uint, bool>? stumm = null,
        Func<IReadOnlyList<ushort>?>? eigeneSendung = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(einstellungen);
        ArgumentNullException.ThrowIfNull(uhr);
        _dienst = dienst;
        _einstellungen = einstellungen;
        _uhr = uhr;
        _stumm = stumm ?? (_ => false);
        _eigeneSendung = eigeneSendung ?? (() => null);
        Bild.Aussehen(einstellungen());
    }

    /// <summary>Was das Fenster zeigt: die Zeilen und wie sie aussehen.</summary>
    public Sprecherbild Bild { get; } = new();

    /// <summary>Es ist etwas zu zeigen (sonst ist das Fenster verborgen, nicht leer gezeigt).</summary>
    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    /// <summary>Die Probezeile steht gerade.</summary>
    public bool ProbeLaeuft => _uhr() < _probeBis;

    /// <summary>
    /// Wer gerade zu hören ist, wie die <see cref="KlangBruecke"/> es meldet (<see cref="KlangBruecke.SprecherGehoert"/>), auf dem
    /// Oberflächen-Thread. Ein neuer Sprecher erscheint sofort; wer schon steht, bleibt an seinem Platz und gilt wieder als gehört.
    /// </summary>
    public void Melden(IReadOnlyList<GehoerterSprecher> gehoert)
    {
        ArgumentNullException.ThrowIfNull(gehoert);
        var e = _einstellungen();
        var jetzt = _uhr();
        if (e.An)
        {
            var lage = _dienst.Lage;
            var ich = lage.Konto?.Id;
            foreach (var s in gehoert)
            {
                if (s.KontoId == ich || _stumm(s.KontoId)) continue;
                if (Kanaltext(lage, s) is not { } kanal) continue;
                Beruehren("k" + s.KontoId.ToString(CultureInfo.InvariantCulture), Anzeigename(lage, s.KontoId), " " + kanal, jetzt);
            }
        }
        Abgleichen(e, jetzt);
    }

    /// <summary>Bildtakt (etwa 30 je Sekunde): die eigene Sendung, Nachhalten, Ausblenden, Nachrücken, Ende der Probe.</summary>
    public void Takt()
    {
        var e = _einstellungen();
        var jetzt = _uhr();
        if (e.An && e.EigeneSendung && _eigeneSendung() is { Count: > 0 } netze && EigenerKanal(_dienst.Lage, netze) is { } kanal)
            Beruehren("du", DuName, " -> " + kanal, jetzt);
        Abgleichen(e, jetzt);
    }

    /// <summary>
    /// Die Probezeile bis <see cref="ProbeMs"/> nach diesem Aufruf, auch wenn die Anzeige aus ist; jedes Schieben an den Reglern der Lage ruft es
    /// wieder, so steht sie, solange geschoben wird, und noch 3 s danach.
    /// </summary>
    public void ProbeZeigen()
    {
        _probeBis = _uhr() + ProbeMs;
        Abgleichen(_einstellungen(), _uhr());
    }

    /// <summary>
    /// Der Text hinter dem Namen ohne Leerzeichen davor, oder <c>null</c>, wenn der Sprecher in keinem gehörten Kanal ankommt: Global "Global";
    /// eine Durchsage an alle "Durchsage an alle", an Netze "Durchsage: " und der erste Kanal des Stempels; in Netzen der erste Kanal des Stempels,
    /// der in einem eigenen Feld mit "hören" liegt (zuerst Global, dann nach Feldnummer), als <see cref="FunkLage.KanalName"/>.
    /// </summary>
    public static string? Kanaltext(FunkLage lage, GehoerterSprecher sprecher)
    {
        ArgumentNullException.ThrowIfNull(lage);
        ArgumentNullException.ThrowIfNull(sprecher);
        switch (sprecher.Art)
        {
            case Spruchart.Global:
                return "Global";
            case Spruchart.Durchsage:
                return sprecher.Netze.Count == 0 ? "Durchsage an alle" : "Durchsage: " + lage.KanalName(sprecher.Netze[0]);
            default:
                foreach (var n in sprecher.Netze)
                {
                    // Ruhe und gesperrte Kanäle hört man nicht (der Server stempelt sie auch nicht; hier nur, falls ein Stand nachhinkt).
                    if (Kanalrechnung.IstRuhe(n) || lage.SperreVon(n) is not null) continue;
                    foreach (var f in lage.Felder)
                        if (f.NetzId == n && f.Hoeren) return lage.KanalName(n);
                }
                return null;
        }
    }

    /// <summary>
    /// Wohin die eigene Stimme geht, für "Du -> ...": "Global", wenn Global dabei ist, sonst der Kanal im kleinsten eigenen Feld (ein Netz ohne Feld
    /// zuletzt), als <see cref="FunkLage.KanalName"/>; <c>null</c> ohne Netz.
    /// </summary>
    public static string? EigenerKanal(FunkLage lage, IReadOnlyList<ushort> netze)
    {
        ArgumentNullException.ThrowIfNull(lage);
        ArgumentNullException.ThrowIfNull(netze);
        if (netze.Count == 0) return null;
        if (netze.Contains(Sprachkanal.GlobalNetz)) return "Global";
        var erstes = netze.OrderBy(n => lage.Felder.FirstOrDefault(f => f.NetzId == n)?.Feld ?? byte.MaxValue).First();
        return lage.KanalName(erstes);
    }

    /// <summary>Der Name eines Sprechers: der Anzeigename aus der Praesenz (wie in "Wer ist da"), ohne Eintrag "Funker n".</summary>
    public static string Anzeigename(FunkLage lage, uint kontoId)
    {
        ArgumentNullException.ThrowIfNull(lage);
        foreach (var f in lage.Funker)
            if (f.KontoId == kontoId) return f.Anzeigename;
        return "Funker " + kontoId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Wie viele Sprecher bei <paramref name="zeilen"/> eingestellten Zeilen und <paramref name="anzahl"/> Sprechern mit Namen dastehen: alle, wenn
    /// sie passen; sonst bei einer Zeile einer, bei mehr eine weniger, und die letzte Zeile sagt "+n weitere".
    /// </summary>
    public static int MitNamen(int zeilen, int anzahl) => anzahl <= zeilen ? anzahl : zeilen >= 2 ? zeilen - 1 : 1;

    private void Beruehren(string schluessel, string name, string rest, long jetzt)
    {
        foreach (var x in _eintraege)
        {
            if (!string.Equals(x.Schluessel, schluessel, StringComparison.Ordinal)) continue;
            x.Name = name;
            x.Rest = rest;
            x.Zuletzt = jetzt;
            return;
        }
        _eintraege.Add(new Eintrag(schluessel, name, rest, jetzt));
    }

    private void Abgleichen(SprecheranzeigeEinstellungen e, long jetzt)
    {
        Bild.Aussehen(e);
        if (!e.An) _eintraege.Clear();
        var halten = Math.Max(e.NachhaltenMs, MeldeLueckeMs);
        var aus = Math.Max(0, e.AusblendenMs);
        _eintraege.RemoveAll(x => jetzt - x.Zuletzt > halten + aus);
        // Nachrücken: Wartet jemand, den die Zeilen nicht fassen, geht ein Gezeigter nach dem Nachhalten sofort, statt erst auszublenden.
        while (_eintraege.Count > MitNamen(e.Zeilen, _eintraege.Count))
        {
            var gezeigt = MitNamen(e.Zeilen, _eintraege.Count);
            var fertig = _eintraege.FindIndex(0, gezeigt, x => jetzt - x.Zuletzt > halten);
            if (fertig < 0) break;
            _eintraege.RemoveAt(fertig);
        }
        var zeilen = new List<(string Schluessel, string Name, string Dahinter, double Deckkraft)>();
        if (jetzt < _probeBis)
        {
            zeilen.Add(("probe", ProbeName, " " + ProbeSatz, 1));
        }
        else
        {
            var gezeigt = MitNamen(e.Zeilen, _eintraege.Count);
            for (var i = 0; i < gezeigt; i++)
            {
                var x = _eintraege[i];
                var ruhig = jetzt - x.Zuletzt;
                var deckkraft = ruhig <= halten ? 1.0 : aus <= 0 ? 0.0 : Math.Clamp(1.0 - ((ruhig - halten) / (double)aus), 0.0, 1.0);
                zeilen.Add((x.Schluessel, x.Name, x.Rest, deckkraft));
            }
            if (_eintraege.Count > gezeigt && e.Zeilen >= 2)
                zeilen.Add(("weitere", "", "+" + (_eintraege.Count - gezeigt).ToString(CultureInfo.InvariantCulture) + " weitere", 1));
        }
        Bild.Setzen(zeilen);
        Sichtbar = Bild.Zeilen.Count > 0;
    }
}

/// <summary>
/// Was die Sprecheranzeige zeigt und wie (seit 0.11.9): die Zeilen und das Aussehen aus den Einstellungen. Das Fenster über dem Spiel und die
/// Vorschau "So sieht sie aus" in den Einstellungen nehmen dieselbe Vorlage (<c>Sprecheranzeige.Bild</c> in Design\Stile.xaml): weiße Schrift
/// (Segoe UI, Systemschrift), Name fett, dahinter normal, ein weicher schwarzer Schatten (Variante B vom 15.10.2026: Unschärfe etwa 3,5 bei
/// Schrift 26, volle Deckkraft, Versatz 1,5); keine Fläche, kein Rahmen, keine Farben.
/// </summary>
public sealed partial class Sprecherbild : ObservableObject
{
    /// <summary>Die Schrift bei 100 % in Einheiten (wie im Bild vom 15.10.2026).</summary>
    public const double SchriftGrund = 26;

    /// <summary>Die Unschärfe des Schattens bei 100 % (Variante B).</summary>
    public const double UnschaerfeGrund = 3.5;

    /// <summary>Der Versatz des Schattens bei 100 % (Variante B: 1 bis 2).</summary>
    public const double VersatzGrund = 1.5;

    /// <summary>Die Zeilen von oben nach unten.</summary>
    public ObservableCollection<Sprecherzeile> Zeilen { get; } = [];

    /// <summary>Schriftgröße in Einheiten: <see cref="SchriftGrund"/> mal der Größe.</summary>
    [ObservableProperty]
    public partial double Schrift { get; private set; } = SchriftGrund;

    /// <summary>Unschärfe des Schattens in Einheiten, mit der Größe.</summary>
    [ObservableProperty]
    public partial double Unschaerfe { get; private set; } = UnschaerfeGrund;

    /// <summary>Versatz des Schattens in Einheiten, mit der Größe.</summary>
    [ObservableProperty]
    public partial double Versatz { get; private set; } = VersatzGrund;

    /// <summary>Deckkraft der Schrift samt Schatten: 1 minus die Durchsichtigkeit.</summary>
    [ObservableProperty]
    public partial double Deckkraft { get; private set; } = 1;

    /// <summary>Luft um die Schrift für den Schatten, damit das Fenster ihn nicht abschneidet.</summary>
    [ObservableProperty]
    public partial Thickness Innenrand { get; private set; } = Rand(UnschaerfeGrund, VersatzGrund);

    /// <summary>
    /// Der Schatten als fertiger, eingefrorener Effekt (schwarz, volle Deckkraft, nach rechts unten, <see cref="Unschaerfe"/> und <see cref="Versatz"/>):
    /// Ein Effekt hat keinen DataContext, darum bindet die Vorlage ihn als Ganzes statt seiner Werte.
    /// </summary>
    [ObservableProperty]
    public partial System.Windows.Media.Effects.DropShadowEffect Schatten { get; private set; } = Effekt(UnschaerfeGrund, VersatzGrund);

    /// <summary>Das Aussehen aus den Einstellungen: Größe und Durchsichtigkeit. Der Text steht immer linksbündig (seit den Schiebereglern für die Lage).</summary>
    public void Aussehen(SprecheranzeigeEinstellungen e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var g = Math.Clamp(e.Groesse, SprecheranzeigeEinstellungen.GroesseMin, SprecheranzeigeEinstellungen.GroesseMax) / 100.0;
        Schrift = SchriftGrund * g;
        if (Math.Abs(Unschaerfe - (UnschaerfeGrund * g)) > 1e-9 || Math.Abs(Versatz - (VersatzGrund * g)) > 1e-9)
        {
            Unschaerfe = UnschaerfeGrund * g;
            Versatz = VersatzGrund * g;
            Schatten = Effekt(Unschaerfe, Versatz);
        }
        Innenrand = Rand(Unschaerfe, Versatz);
        Deckkraft = 1 - (Math.Clamp(e.Durchsichtigkeit, 0, SprecheranzeigeEinstellungen.DurchsichtigkeitMax) / 100.0);
    }

    /// <summary>Setzt die Zeilen; gleiche Folge von Schlüsseln aktualisiert die stehenden, sonst wird neu gefüllt.</summary>
    public void Setzen(IReadOnlyList<(string Schluessel, string Name, string Dahinter, double Deckkraft)> zeilen)
    {
        ArgumentNullException.ThrowIfNull(zeilen);
        var gleich = zeilen.Count == Zeilen.Count;
        for (var i = 0; gleich && i < zeilen.Count; i++)
            gleich = string.Equals(Zeilen[i].Schluessel, zeilen[i].Schluessel, StringComparison.Ordinal);
        if (!gleich)
        {
            var bisher = Zeilen.ToDictionary(z => z.Schluessel, StringComparer.Ordinal);
            Zeilen.Clear();
            foreach (var z in zeilen) Zeilen.Add(bisher.GetValueOrDefault(z.Schluessel) ?? new Sprecherzeile(z.Schluessel));
        }
        for (var i = 0; i < zeilen.Count; i++)
        {
            var z = Zeilen[i];
            z.Name = zeilen[i].Name;
            z.Rest = zeilen[i].Dahinter;
            z.Deckkraft = zeilen[i].Deckkraft;
        }
    }

    /// <summary>Was Bildschirmleser und Tests lesen: die Zeilen untereinander.</summary>
    public string Text => string.Join("\n", Zeilen.Select(z => z.Text));

    private static Thickness Rand(double unschaerfe, double versatz)
    {
        var r = Math.Ceiling(unschaerfe + versatz) + 2;
        return new Thickness(r);
    }

    private static System.Windows.Media.Effects.DropShadowEffect Effekt(double unschaerfe, double versatz)
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            Opacity = 1,
            Direction = 315,
            BlurRadius = unschaerfe,
            ShadowDepth = versatz,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
        };
        e.Freeze();
        return e;
    }
}

/// <summary>Eine Zeile der Sprecheranzeige: der Name fett, dahinter der Rest normal ("Brücke (Schiff 1)" mit Leerzeichen davor), die Deckkraft beim Ausblenden.</summary>
public sealed partial class Sprecherzeile(string schluessel) : ObservableObject
{
    /// <summary>Wer oder was: "k" und die Konto-ID, "du", "weitere" oder "probe".</summary>
    public string Schluessel { get; } = schluessel;

    /// <summary>Fett: der Name (leer bei "+n weitere").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial string Name { get; set; } = "";

    /// <summary>Normal: was dahinter steht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial string Rest { get; set; } = "";

    /// <summary>1 voll, beim Ausblenden bis 0.</summary>
    [ObservableProperty]
    public partial double Deckkraft { get; set; } = 1;

    /// <summary>Die ganze Zeile als Text, etwa "Kobold Brücke (Schiff 3)".</summary>
    public string Text => Name + Rest;
}
