// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Die Kanäle: Einsteigen per Rolle und Schiff in einem Schritt mit Belegung je Schiff (A15) und Vorschau der Netze einer Rolle (A22), die eigenen
/// Felder mit Hören, Ohr (links, beide, rechts, A9) und Lautstärke, und der Pegel je Funker (A4). Bis 0.9.3 die Seite Meine Kanäle; seit 0.10.0
/// (Mischung Q87) steht Einsteigen auf Start (nur mit Einheiten, E10) und alles Übrige unter Einstellungen > Empfangen > Erweitert (E16; VA oder
/// Taste je Kanal stellt man im Funkfeld ein).
/// </summary>
public sealed partial class NetzeViewModel : ObservableObject
{
    private readonly IFunkDienst _dienst;
    private readonly IKlangMaschine _maschine;
    private readonly IOberflaechentakt _takt;
    private readonly Einstellungsablage? _ablage;
    private readonly INetzKlang? _netzKlang;
    private readonly Dictionary<uint, float> _angewandt = [];

    /// <param name="ablage">Merkt das Ohr je Netz; ohne sie gilt es nur bis zum Ende.</param>
    /// <param name="netzKlang">Bekommt Lautstärke und Panorama je Netz.</param>
    /// <param name="funkfeld">
    /// Lautstärke und VA/Taste je Netz (A9, A10) liegen beim Funkfeld; mit ihm zeigt jede Zeile dieselben Regler wie die
    /// Kachel. Die Ansicht Liste hat keine Regler auf den Kacheln, dort geht es nur hier.
    /// </param>
    public NetzeViewModel(IFunkDienst dienst, IKlangMaschine maschine, IOberflaechentakt takt, Einstellungsablage? ablage = null, INetzKlang? netzKlang = null,
        FunkfeldViewModel? funkfeld = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _maschine = maschine;
        _takt = takt;
        _ablage = ablage;
        _netzKlang = netzKlang;
        Funkfeld = funkfeld;
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        if (funkfeld is not null)
        {
            funkfeld.NetzGeaendert += NetzGeaendert;
            // Klangstufe C2b: das gelbe "!" an Dauersendern auch in der Liste "Funker: eigener Pegel je Person".
            funkfeld.Dauersend.Geaendert += DauersenderGeaendert;
        }
        Aktualisieren();
    }

    private void DauersenderGeaendert()
    {
        foreach (var z in Funker) z.Dauersend = Funkfeld?.Dauersend.ZeichenVon(z.KontoId);
    }

    internal FunkfeldViewModel? Funkfeld { get; }

    /// <summary>Lautstärke und VA/Taste von Global; null ohne Global oder ohne Funkfeld.</summary>
    [ObservableProperty]
    public partial NetzRegelung? GlobalRegelung { get; private set; }

    private void NetzGeaendert(ushort netzId)
    {
        // Seit 0.10.0 auch das Ohr: "Zurück auf Werkseinstellung" unter Empfangen > Erweitert stellt es über das Funkfeld zurück, für alle Ch. eines
        // Kanalnetzes (gleicher Schlüssel).
        var schluessel = ClientEinstellungen.Schluessel(netzId);
        foreach (var f in Felder)
        {
            if (f.Regelung?.NetzId == netzId) f.Regelung.Auffrischen();
            if (f.Ohr is { } ohr && ClientEinstellungen.Schluessel(ohr.NetzId) == schluessel) ohr.Auffrischen();
        }
        if (GlobalRegelung?.NetzId == netzId) GlobalRegelung.Auffrischen();
        if (GlobalOhr is { } g && ClientEinstellungen.Schluessel(g.NetzId) == schluessel) g.Auffrischen();
    }

    private readonly Dictionary<ushort, float> _ohneAblage = [];

    /// <summary>Ohr von Global (A9, A23); null, solange es kein Global gibt.</summary>
    [ObservableProperty]
    public partial OhrWahl? GlobalOhr { get; private set; }

    /// <summary>A9: -1 links, 0 beide, +1 rechts.</summary>
    internal float PanoramaVon(ushort netzId) =>
        _ablage is null ? _ohneAblage.GetValueOrDefault(netzId) : _ablage.Aktuell.NetzOderNull(netzId)?.Panorama ?? 0f;

    internal void PanoramaSetzen(ushort netzId, float panorama)
    {
        var db = 0f;
        if (_ablage is null)
        {
            _ohneAblage[netzId] = panorama;
        }
        else
        {
            var n = _ablage.Aktuell.NetzFuer(netzId);
            n.Panorama = panorama;
            db = n.LautstaerkeDb;
            _ablage.Vormerken();
        }
        _netzKlang?.NetzEinstellen(netzId, db, panorama);
        // Seit 0.6.0 gilt das Ohr für alle Ch. eines Kanalnetzes (Sarevoc Q15): Das Funkfeld gibt es an die übrigen Kanäle weiter.
        Funkfeld?.NetzEinstellungGeaendert(netzId);
    }

    public ObservableCollection<RolleInfo> Rollen { get; } = [];

    /// <summary>Die Einheiten des gewählten Reiters (Schiffe, Infanterie oder Orga) mit Belegung.</summary>
    public ObservableCollection<SchiffEintrag> Schiffe { get; } = [];

    public ObservableCollection<FeldZeile> Felder { get; } = [];

    public ObservableCollection<FunkerZeile> Funker { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SchiffNoetig), nameof(NetzVorschau))]
    [NotifyCanExecuteChangedFor(nameof(EinsteigenCommand))]
    public partial RolleInfo? GewaehlteRolle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetzVorschau))]
    [NotifyCanExecuteChangedFor(nameof(EinsteigenCommand))]
    public partial SchiffEintrag? GewaehltesSchiff { get; set; }

    public bool SchiffNoetig => GewaehlteRolle?.BrauchtSchiff == true;

    /// <summary>A22: welche Netze die Rolle mitbringt, auf der gewählten Einheit mit deren echten Namen ("Crew 2").</summary>
    public string NetzVorschau
    {
        get
        {
            if (GewaehlteRolle is not { } r) return "";
            var einheit = r.BrauchtSchiff ? _lage.SchiffVon(GewaehltesSchiff?.Id) : null;
            var echt = einheit is null ? [] : r.NetzeAuf(einheit).Select(n => _lage.NetzVon(n)?.Name).OfType<string>().ToList();
            return echt.Count > 0 ? string.Join(", ", echt) : string.Join(", ", r.Netze);
        }
    }

    /// <summary>Die Karte Einsteigen steht da, nur wo die Flotte Einheiten hat; ohne sie wählt man die Rolle im Funkfeld (Entwurf 5.5).</summary>
    [ObservableProperty]
    public partial bool ZeigtEinsteigen { get; private set; }

    [ObservableProperty]
    public partial string AktuellerPlatz { get; private set; } = "noch nirgends eingestiegen";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EinsteigenCommand))]
    public partial bool Selbstwahl { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EinsteigenCommand))]
    public partial bool Verbunden { get; private set; }

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    // ---- Stufe 2: Einsteigen mit Einheiten (A39), "wie letztes Mal" (A21), Sperre mit Grund (F9) ----

    /// <summary>Schiffe, Infanterie, Orga und ohne Einheit, je mit Anzahl; nur, was es gibt.</summary>
    public ObservableCollection<ArtReiter> Reiter { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OhneEinheit), nameof(MitEinheit))]
    public partial ArtReiter? GewaehlterReiter { get; set; }

    /// <summary>Der Reiter "Ohne Einheit" ist gewählt: keine Einheitenliste, nur die Rollen.</summary>
    public bool OhneEinheit => GewaehlterReiter is { Art: null };

    public bool MitEinheit => GewaehlterReiter is { Art: not null };

    /// <summary>Die Plätze der gewählten Einheit (oder die Rollen ohne Einheit) mit Belegung, wie der Server zählt (K1).</summary>
    public ObservableCollection<PlatzWahl> Plaetze { get; } = [];

    [ObservableProperty]
    public partial PlatzWahl? GewaehlterPlatz { get; set; }

    /// <summary>"Wie letztes Mal: Schiff 2, Pilot", solange es Einheit und Rolle noch gibt; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? WieLetztesMalText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WieLetztesMalCommand))]
    public partial bool WieLetztesMalFrei { get; private set; }

    /// <summary>Warum Einsteigen gerade nicht geht oder nur für Nachzügler (Name der Einsatzleitung), sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? SperrGrund { get; private set; }

    private FunkLage _lage = FunkLage.Leer;
    private (ushort? SchiffId, ushort? RolleId)? _eigenerPlatz;

    [RelayCommand(CanExecute = nameof(WieLetztesMalFrei))]
    private async Task WieLetztesMal()
    {
        var text = WieLetztesMalText;
        var fehler = await _dienst.WieLetztesMalAsync().ConfigureAwait(true);
        Hinweis = fehler ?? text?.Replace("Wie letztes Mal: ", "Wieder auf ", StringComparison.Ordinal) + ".";
    }

    [RelayCommand(CanExecute = nameof(KannEinsteigen))]
    private async Task EinsteigenAsync()
    {
        if (GewaehlteRolle is not { } r) return;
        var schiff = r.BrauchtSchiff ? GewaehltesSchiff?.Id : null;
        var fehler = await _dienst.EinsteigenAsync(r.Id, schiff).ConfigureAwait(true);
        Hinweis = fehler ?? "Eingestiegen als " + r.Name + (schiff is null ? "." : " auf " + GewaehltesSchiff!.Name + ".");
    }

    private static string Schluessel(RolleInfo r) =>
        string.Join("\u001F", r.Id, r.Name, r.BrauchtSchiff, r.SelbstWaehlbar, string.Join(",", r.Netze));

    private bool KannEinsteigen() =>
        Verbunden && !Gesperrt(_lage) && GewaehlteRolle is { SelbstWaehlbar: true } r && (!r.BrauchtSchiff || GewaehltesSchiff is not null)
        && !Belegt(r, GewaehltesSchiff?.Id) && !(_lage.RolleId == r.Id && _lage.SchiffId == (r.BrauchtSchiff ? GewaehltesSchiff?.Id : null));

    /// <summary>
    /// Gesperrte Selbstwahl (6.9, F9): nicht für die Einsatzleitung (ZUWEISEN) und bei "Wechsel sperren" nicht für Nachzügler ohne
    /// Platz. Ob der Urheber noch ZUWEISEN hat, weiß nur der Server; im Zweifel sagt er es beim Einsteigen.
    /// </summary>
    private static bool Gesperrt(FunkLage l) => !l.Selbstwahl && l.Konto?.Einsatzleitung != true && !(l.Nachzuegler && l.RolleId is null);

    /// <summary>Ob der Platz voll ist (auf der Einheit oder ohne Einheit); ein Platz, den es auf der Einheit nicht gibt, auch.</summary>
    private bool Belegt(RolleInfo r, ushort? schiff)
    {
        if (!r.BrauchtSchiff) return r.Kapazitaet is { } k && r.Belegt >= k;
        return _lage.SchiffVon(schiff)?.PlatzVon(r.Id) is not { } p || p.Belegt >= p.Kapazitaet;
    }

    partial void OnGewaehlterReiterChanged(ArtReiter? value) => EinheitenZeigen();

    partial void OnGewaehltesSchiffChanged(SchiffEintrag? value) => PlaetzeZeigen();

    partial void OnGewaehlterPlatzChanged(PlatzWahl? value)
    {
        if (value is not null && GewaehlteRolle?.Id != value.Rolle.Id) GewaehlteRolle = Rollen.FirstOrDefault(r => r.Id == value.Rolle.Id) ?? value.Rolle;
    }

    /// <summary>Die Einheiten des gewählten Reiters; die gewählte bleibt, wenn sie dazugehört.</summary>
    private void EinheitenZeigen()
    {
        var art = GewaehlterReiter?.Art;
        var vorher = GewaehltesSchiff?.Id;
        Sammlung.Abgleichen(Schiffe, art is null ? [] : _lage.Schiffe.Where(s => s.Art == art), s => s.Id, s => s.Id, s => new SchiffEintrag(s), (z, s) => z.Setzen(s));
        foreach (var s in Schiffe) s.RolleSetzen(GewaehlteRolle);
        GewaehltesSchiff = Schiffe.FirstOrDefault(s => s.Id == vorher);
        PlaetzeZeigen();
    }

    /// <summary>Die Plätze der gewählten Einheit, oder ohne Einheit die Rollen ohne Einheit; je mit Belegung und Grund.</summary>
    private void PlaetzeZeigen()
    {
        var l = _lage;
        var soll = new List<(RolleInfo Rolle, string Text, bool Voll, string? Grund)>();
        if (GewaehlterReiter is { Art: null })
        {
            foreach (var r in l.Rollen.Where(r => !r.BrauchtSchiff))
            {
                var voll = r.Kapazitaet is { } k && r.Belegt >= k;
                soll.Add((r, r.Name + " " + (r.Kapazitaet is { } kap ? r.Belegt.ToString(CultureInfo.CurrentCulture) + "/" + kap.ToString(CultureInfo.CurrentCulture) : r.Belegt.ToString(CultureInfo.CurrentCulture)),
                    voll && !(l.SchiffId is null && l.RolleId == r.Id), Grund(l, r, null, voll)));
            }
        }
        else if (l.SchiffVon(GewaehltesSchiff?.Id) is { } einheit)
        {
            foreach (var p in einheit.Plaetze.OrderBy(p => l.RolleVon(p.RolleId)?.Reihenfolge ?? int.MaxValue))
            {
                if (l.RolleVon(p.RolleId) is not { } r) continue;
                var eigener = l.SchiffId == einheit.Id && l.RolleId == r.Id;
                soll.Add((r, string.Create(CultureInfo.CurrentCulture, $"{r.Name} {p.Belegt}/{p.Kapazitaet}"), p.Voll && !eigener, Grund(l, r, einheit.Id, p.Voll)));
            }
        }
        Sammlung.Abgleichen(Plaetze, soll, x => x.Rolle.Id, z => z.Rolle.Id, x => new PlatzWahl(x.Rolle, x.Text, x.Voll, x.Grund), (z, x) => z.Setzen(x.Rolle, x.Text, x.Voll, x.Grund));
        var gewaehlt = Plaetze.FirstOrDefault(p => p.Rolle.Id == GewaehlteRolle?.Id);
        if (!ReferenceEquals(gewaehlt, GewaehlterPlatz)) GewaehlterPlatz = gewaehlt;
        EinsteigenCommand.NotifyCanExecuteChanged();
    }

    private static string? Grund(FunkLage l, RolleInfo r, ushort? schiff, bool voll) =>
        l.RolleId == r.Id && l.SchiffId == schiff ? "dein Platz"
        : voll ? "voll"
        : !r.SelbstWaehlbar ? "teilt die Einsatzleitung zu"
        : null;

    internal async Task HoerenSetzenAsync(FeldZeile zeile, bool hoeren)
    {
        var fehler = await _dienst.HoerenSetzenAsync(zeile.NetzId, hoeren).ConfigureAwait(true);
        if (fehler is not null)
        {
            Hinweis = zeile.Netz + ": " + fehler;
            Aktualisieren();
        }
    }

    internal async Task PegelSetzenAsync(FunkerZeile zeile, float db)
    {
        var fehler = await PegelAnwendenAsync(zeile.KontoId, db).ConfigureAwait(true);
        if (fehler is not null) Hinweis = zeile.Name + ": " + fehler;
    }

    // ---- Pegel je Person (A4) für die Wippe in "Wer ist da" (seit 0.11.5) ----

    /// <summary>
    /// Der eigene Pegel je Person in dB, wie ihn der Regler unter Empfangen > Funker zeigt (begrenzt auf <see cref="FunkerZeile.MinDb"/> bis
    /// <see cref="FunkerZeile.MaxDb"/>): zuletzt hier gesetzt, sonst vom Server gespeichert. Eine Stelle für Regler und Wippe, damit beide dasselbe zeigen.
    /// </summary>
    private readonly Dictionary<uint, double> _pegelStand = [];

    /// <summary>
    /// Was die Lage zuletzt als gespeicherten Pegel brachte (<see cref="FunkLage.FunkerPegel"/>). Der Server schickt die Pegel nur bei der Anmeldung
    /// (PegelVoreinstellungen, 4.5), nicht nach jedem PegelSetzen; bis 0.11.4 setzte darum jeder Push einen eben verstellten Pegel auf den Wert der
    /// Anmeldung zurück, an der Klangmaschine und am Regler. Seit 0.11.5 gilt ein Wert der Lage nur, wenn er sich dort geändert hat.
    /// </summary>
    private readonly Dictionary<uint, float> _pegelGesehen = [];

    /// <summary>Ein Pegel je Person hat sich geändert (Regler oder Wippe); "Wer ist da" zeigt ihn im Hilfetext der Wippe.</summary>
    internal event Action? PegelGeaendert;

    /// <summary>Der eigene Pegel für <paramref name="kontoId"/> in dB, wie ihn der Regler zeigt; 0 ohne Eintrag.</summary>
    internal double PegelVon(uint kontoId) => _pegelStand.GetValueOrDefault(kontoId);

    /// <summary>
    /// Setzt den eigenen Pegel für eine Person (Wippe in "Wer ist da", seit 0.11.5) über dieselbe Stelle wie der Regler: begrenzt und auf 0,5 dB
    /// gerundet wie er, die Klangmaschine sofort, am Konto gespeichert (PegelSetzen, 4.5); die Zeile unter Empfangen > Funker zeigt ihn gleich mit.
    /// Liefert den Fehlertext des Servers oder <c>null</c>.
    /// </summary>
    internal Task<string?> PegelSetzenAsync(uint kontoId, double db)
    {
        var neu = FunkerZeile.Begrenzt(db);
        Funker.FirstOrDefault(z => z.KontoId == kontoId)?.PegelZeigen(neu);
        return PegelAnwendenAsync(kontoId, (float)neu);
    }

    /// <summary>Ein gesetzter Pegel: Stand, Klangmaschine, Meldung an die Wippe, dann der Server.</summary>
    private Task<string?> PegelAnwendenAsync(uint kontoId, float db)
    {
        _pegelStand[kontoId] = db;
        _maschine.PegelFuerFunker(kontoId, db);
        _angewandt[kontoId] = db;
        PegelGeaendert?.Invoke();
        return _dienst.PegelSetzenAsync(kontoId, db);
    }

    /// <summary>
    /// Die gespeicherten Pegel aus der Lage (A4), nur wenn sie sich dort geändert haben (<see cref="_pegelGesehen"/>): an die Klangmaschine
    /// unverändert, für Regler und Wippe begrenzt.
    /// </summary>
    private void PegelAusDerLage(FunkLage l)
    {
        var geaendert = false;
        foreach (var (konto, db) in l.FunkerPegel)
        {
            if (_pegelGesehen.TryGetValue(konto, out var gesehen) && gesehen.Equals(db)) continue;
            _pegelGesehen[konto] = db;
            if (float.IsNaN(db)) continue;
            _pegelStand[konto] = Math.Clamp((double)db, FunkerZeile.MinDb, FunkerZeile.MaxDb);
            geaendert = true;
            if (_angewandt.TryGetValue(konto, out var alt) && Math.Abs(alt - db) < 0.01f) continue;
            _angewandt[konto] = db;
            _maschine.PegelFuerFunker(konto, db);
        }
        if (geaendert) PegelGeaendert?.Invoke();
    }

    partial void OnGewaehlteRolleChanged(RolleInfo? value)
    {
        foreach (var s in Schiffe) s.RolleSetzen(value);
        var platz = Plaetze.FirstOrDefault(p => p.Rolle.Id == value?.Id);
        if (!ReferenceEquals(platz, GewaehlterPlatz)) GewaehlterPlatz = platz;
    }

    internal void Aktualisieren()
    {
        var l = _dienst.Lage;
        _lage = l;
        ZeigtEinsteigen = l.Schiffe.Count > 0;
        Verbunden = l.IstAktiv;
        Selbstwahl = l.Selbstwahl;
        // Ändert sich der eigene Platz (selbst oder durch die Einsatzleitung), folgt die Auswahl ihm; sonst bleibt, was du gewählt hast.
        var eigener = (l.SchiffId, l.RolleId);
        var gewechselt = _eigenerPlatz is { } bisher && bisher != eigener;
        _eigenerPlatz = eigener;
        var rolleVorher = gewechselt ? l.RolleId : GewaehlteRolle?.Id ?? l.RolleId;
        var schiffVorher = gewechselt ? l.SchiffId : GewaehltesSchiff?.Id ?? l.SchiffId;

        // Nur neu füllen, wenn sich die Rollen wirklich geändert haben: Jeder Praesenz-Push baut eine neue Lage, und ein
        // Leeren der Liste setzt die Auswahl der ComboBox zurück.
        if (!Rollen.Select(Schluessel).SequenceEqual(l.Rollen.Select(Schluessel)))
        {
            Rollen.Clear();
            foreach (var r in l.Rollen) Rollen.Add(r);
        }
        GewaehlteRolle = Rollen.FirstOrDefault(r => r.Id == rolleVorher);

        var reiter = new List<ArtReiter>();
        foreach (var (art, titel) in new[] { (Einheitsart.Schiff, "Schiffe"), (Einheitsart.Trupp, "Infanterie"), (Einheitsart.Orga, "Orga") })
        {
            var n = l.Schiffe.Count(s => s.Art == art);
            if (n > 0) reiter.Add(new ArtReiter(art, titel + " (" + n.ToString(CultureInfo.CurrentCulture) + ")"));
        }
        if (l.Rollen.Any(r => !r.BrauchtSchiff)) reiter.Add(new ArtReiter(null, "Ohne Einheit"));
        if (!Reiter.SequenceEqual(reiter))
        {
            var art = GewaehlterReiter?.Art;
            Reiter.Clear();
            foreach (var r in reiter) Reiter.Add(r);
            if (GewaehlterReiter is not null) GewaehlterReiter = Reiter.FirstOrDefault(r => r.Art == art);
        }
        var eigeneArt = l.SchiffVon(l.SchiffId)?.Art;
        // Auf eine Einheit folgt der Reiter ihrer Art; eine Rolle ohne Einheit lässt den Reiter stehen.
        var zuEigenem = eigeneArt is null ? null : Reiter.FirstOrDefault(r => r.Art == eigeneArt);
        if (GewaehlterReiter is null || (gewechselt && zuEigenem is not null) || !Reiter.Contains(GewaehlterReiter))
            GewaehlterReiter = zuEigenem ?? Reiter.FirstOrDefault(r => r.Art is not null) ?? Reiter.FirstOrDefault();

        var art2 = GewaehlterReiter?.Art;
        Sammlung.Abgleichen(Schiffe, art2 is null ? [] : l.Schiffe.Where(s => s.Art == art2), s => s.Id, s => s.Id, s => new SchiffEintrag(s), (z, s) => z.Setzen(s));
        foreach (var s in Schiffe) s.RolleSetzen(GewaehlteRolle);
        GewaehltesSchiff = Schiffe.FirstOrDefault(s => s.Id == schiffVorher);
        PlaetzeZeigen();
        OnPropertyChanged(nameof(NetzVorschau));

        var rolle = l.RolleVon(l.RolleId)?.Name;
        var schiffName = l.SchiffVon(l.SchiffId)?.Name;
        AktuellerPlatz = rolle is null ? "noch nirgends eingestiegen" : rolle + (schiffName is null ? "" : " auf " + schiffName) + Herkunft(l);
        SperrGrund = l.Selbstwahl || l.Konto?.Einsatzleitung == true ? null
            : l.Nachzuegler && l.RolleId is null
                ? "Wechsel gesperrt durch die Einsatzleitung" + (l.SelbstwahlDurch is { } d and not 0 ? " (" + l.HauptnameVon(d) + ")" : "")
                  + ". Du hast noch keinen Platz und kannst einmal selbst einsteigen."
                : FunkClientDienst.Sperrtext(l);
        WieLetztesMalSetzen(l);

        Sammlung.Abgleichen(Felder, l.Felder, f => f.Feld, z => z.Feld,
            f => new FeldZeile(this, f, l), (z, f) => z.Setzen(f, l));
        if (l.Global is { } g)
        {
            if (GlobalOhr?.NetzId != g.Id) GlobalOhr = new OhrWahl(this, g.Id, g.Name);
            else GlobalOhr.NetzSetzen(g.Name);
            if (Funkfeld is not null && GlobalRegelung?.NetzId != g.Id) GlobalRegelung = new NetzRegelung(Funkfeld, g.Id, g.Name);
            else GlobalRegelung?.NetzSetzen(g.Name);
        }
        else
        {
            GlobalOhr = null;
            GlobalRegelung = null;
        }
        // A4: gespeicherte Pegel an die Maschine, sobald sie bekannt sind oder sich in der Lage ändern; vor den Zeilen, damit die Regler sie zeigen.
        PegelAusDerLage(l);
        Sammlung.Abgleichen(Funker, l.Funker.Where(f => l.Konto is null || f.KontoId != l.Konto.Id), f => f.KontoId, z => z.KontoId,
            f => new FunkerZeile(this, f, l), (z, f) => z.Setzen(f, l));
    }

    /// <summary>
    /// Wie der eigene Platz zustande kam: selbst gewählt oder eingeteilt von der Einsatzleitung (Teilnehmersicht). Ein Server bis 0.10.0
    /// meldet eine Einteilung nach dem Login als WIEDERHERGESTELLT mit <c>durch</c>; das zählt seit 0.11.0 wie VERSCHIEBEN.
    /// </summary>
    private static string Herkunft(FunkLage l) => l.Quelle switch
    {
        MitgliedschaftsQuelle.Selbst or MitgliedschaftsQuelle.Rolle or MitgliedschaftsQuelle.Satz => " (selbst gewählt)",
        MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung when l.QuelleDurch is { } d && d != l.Konto?.Id => ", eingeteilt von " + l.HauptnameVon(d),
        MitgliedschaftsQuelle.Wiederhergestellt when l.QuelleDurch is { } d && d != 0 && d != l.Konto?.Id => ", eingeteilt von " + l.HauptnameVon(d),
        MitgliedschaftsQuelle.Wiederhergestellt => " (wie vor der Trennung)",
        _ => "",
    };

    /// <summary>"Wie letztes Mal" (A21): nur, wenn Einheit und Rolle noch existieren und es nicht der jetzige Platz ist.</summary>
    private void WieLetztesMalSetzen(FunkLage l)
    {
        if (l.LetzterStand is not { RolleId: { } r } ls || l.RolleVon(r) is not { } rolle || (ls.SchiffId is { } s && l.SchiffVon(s) is null)
            || (ls.SchiffId == l.SchiffId && ls.RolleId == l.RolleId))
        {
            WieLetztesMalText = null;
            WieLetztesMalFrei = false;
            return;
        }
        var besetzt = Belegt(rolle, ls.SchiffId);
        WieLetztesMalText = "Wie letztes Mal: " + WerIstDaZeile.Platztext(l, ls.SchiffId, r) + (besetzt ? " (gerade besetzt)" : "");
        WieLetztesMalFrei = l.IstAktiv && !besetzt && !Gesperrt(l);
    }
}

/// <summary>Ein Reiter beim Einsteigen: Schiffe, Infanterie, Orga oder ohne Einheit (<see cref="Art"/> <c>null</c>).</summary>
public sealed record ArtReiter(Einheitsart? Art, string Titel);

/// <summary>Ein Platz zum Einsteigen: Rolle auf der gewählten Einheit (oder ohne Einheit) mit Belegung und, wenn er nicht geht, warum.</summary>
public sealed partial class PlatzWahl : ObservableObject
{
    internal PlatzWahl(RolleInfo rolle, string text, bool voll, string? grund) => Setzen(rolle, text, voll, grund);

    [ObservableProperty]
    public partial RolleInfo Rolle { get; private set; }

    /// <summary>"Gunner 1/2".</summary>
    [ObservableProperty]
    public partial string Text { get; private set; }

    [ObservableProperty]
    public partial bool Voll { get; private set; }

    /// <summary>"voll", "dein Platz", "teilt die Einsatzleitung zu"; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Grund { get; private set; }

    [MemberNotNull(nameof(Rolle), nameof(Text))]
    internal void Setzen(RolleInfo rolle, string text, bool voll, string? grund)
    {
        Rolle = rolle;
        Text = text;
        Voll = voll;
        Grund = grund;
    }

    public override string ToString() => Text;
}

/// <summary>Ein Schiff in der Auswahl, mit Belegung insgesamt und für die gewählte Rolle.</summary>
public sealed partial class SchiffEintrag : ObservableObject
{
    private SchiffInfo _schiff;
    private RolleInfo? _rolle;

    public SchiffEintrag(SchiffInfo schiff)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        _schiff = schiff;
    }

    public ushort Id => _schiff.Id;

    public string Name => _schiff.Name;

    /// <summary>Etwa "3/7 belegt, Gunner 1/3".</summary>
    public string Belegung
    {
        get
        {
            var gesamt = string.Create(CultureInfo.CurrentCulture, $"{_schiff.Belegt}/{_schiff.Kapazitaet} belegt");
            if (_rolle is null) return gesamt;
            var p = _schiff.Plaetze.FirstOrDefault(x => x.RolleId == _rolle.Id);
            return p is null
                ? gesamt + ", kein Platz für " + _rolle.Name
                : gesamt + string.Create(CultureInfo.CurrentCulture, $", {_rolle.Name} {p.Belegt}/{p.Kapazitaet}");
        }
    }

    /// <summary>Kein freier Platz für die gewählte Rolle.</summary>
    public bool Voll
    {
        get
        {
            if (_rolle is null || !_rolle.BrauchtSchiff) return false;
            var p = _schiff.Plaetze.FirstOrDefault(x => x.RolleId == _rolle.Id);
            return p is null || p.Belegt >= p.Kapazitaet;
        }
    }

    internal void Setzen(SchiffInfo s)
    {
        _schiff = s;
        OnPropertyChanged(string.Empty);
    }

    internal void RolleSetzen(RolleInfo? r)
    {
        _rolle = r;
        OnPropertyChanged(nameof(Belegung));
        OnPropertyChanged(nameof(Voll));
    }

    public override string ToString() => Name;
}

/// <summary>Ein eigenes Feld unter Einstellungen > Empfangen (bis 0.9.3 Meine Kanäle): der Kanal mit Ch., sein Zustand, Hören, Ohr und Lautstärke.</summary>
public sealed partial class FeldZeile : ObservableObject
{
    private readonly NetzeViewModel _netze;
    private bool _hoeren;

    internal FeldZeile(NetzeViewModel netze, FeldInfo f, FunkLage l)
    {
        _netze = netze;
        Feld = f.Feld;
        Setzen(f, l);
    }

    public byte Feld { get; }

    public string FeldText => (Feld + 1).ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial ushort NetzId { get; private set; }

    /// <summary>Der Name des Kanals, bei einem Kanal mit Ch. des Kanalnetzes ("Brücke"); für Zugriffsnamen.</summary>
    [ObservableProperty]
    public partial string Netz { get; private set; } = "";

    /// <summary>Der ganze Kanal wie im Funkfeld, mit der Bezeichnung des Ch.: "Brücke (Schiff 3)".</summary>
    [ObservableProperty]
    public partial string Kanal { get; private set; } = "";

    /// <summary>Das Wort zur Farbe wie auf der Kachel: SENDET, HÖRT, AUS, RUHE oder GESPERRT.</summary>
    [ObservableProperty]
    public partial string ZustandText { get; private set; } = "";

    /// <summary>Der Kanal ist in der Sendeauswahl (SENDET).</summary>
    [ObservableProperty]
    public partial bool Sendet { get; private set; }

    [ObservableProperty]
    public partial bool Senden { get; private set; }

    [ObservableProperty]
    public partial bool Gewaehlt { get; private set; }

    [ObservableProperty]
    public partial bool Fest { get; private set; }

    /// <summary>A9: auf welchem Ohr dieses Netz spielt.</summary>
    [ObservableProperty]
    public partial OhrWahl? Ohr { get; private set; }

    /// <summary>A9, A10: Lautstärke und VA/Taste, dieselben wie auf der Kachel; null ohne Funkfeld.</summary>
    [ObservableProperty]
    public partial NetzRegelung? Regelung { get; private set; }

    public bool Hoeren
    {
        get => _hoeren;
        set
        {
            if (_hoeren == value) return;
            _hoeren = value;
            OnPropertyChanged();
            _ = _netze.HoerenSetzenAsync(this, value);
        }
    }

    internal void Setzen(FeldInfo f, FunkLage l)
    {
        NetzId = f.NetzId;
        Netz = l.NetzVon(f.NetzId)?.Name ?? l.KanalName(f.NetzId);
        Kanal = l.KanalName(f.NetzId);
        if (Ohr?.NetzId != f.NetzId) Ohr = new OhrWahl(_netze, f.NetzId, Netz);
        else Ohr.NetzSetzen(Netz);
        if (_netze.Funkfeld is not null && Regelung?.NetzId != f.NetzId) Regelung = new NetzRegelung(_netze.Funkfeld, f.NetzId, Netz);
        else Regelung?.NetzSetzen(Netz);
        Senden = f.Senden;
        Fest = f.Fest;
        Gewaehlt = l.SendeFelder.Contains(f.Feld);
        var ruhe = Kanalrechnung.IstRuhe(f.NetzId);
        var gesperrt = l.SperreVon(f.NetzId) is not null;
        var zustand = ruhe ? KachelZustand.Ruhe : gesperrt ? KachelZustand.Gesperrt : Gewaehlt && f.Senden ? KachelZustand.Senden : f.Hoeren ? KachelZustand.Hoeren : KachelZustand.Aus;
        Sendet = zustand == KachelZustand.Senden;
        ZustandText = KachelViewModel.Text(zustand);
        if (_hoeren != f.Hoeren)
        {
            _hoeren = f.Hoeren;
            OnPropertyChanged(nameof(Hoeren));
        }
    }
}

/// <summary>Ein anderer Funker mit eigenem Pegel (A4).</summary>
public sealed partial class FunkerZeile : ObservableObject
{
    private readonly NetzeViewModel _netze;
    private double _pegelDb;

    internal FunkerZeile(NetzeViewModel netze, FunkerInfo f, FunkLage l)
    {
        _netze = netze;
        KontoId = f.KontoId;
        Setzen(f, l);
    }

    public uint KontoId { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial bool Online { get; private set; }

    [ObservableProperty]
    public partial string Platz { get; private set; } = "";

    /// <summary>Das gelbe "!" der Auto Dauersend-Dämpfung an diesem Funker (Klangstufe C2b), sonst <c>null</c>; wie im Funkfeld und in "Wer ist da".</summary>
    [ObservableProperty]
    public partial DauersendZeichen? Dauersend { get; internal set; }

    /// <summary>Grenzen des Reglers in dB (4.5 erlaubt auf dem Server -60 bis +12; der Regler zeigt nur diesen Teil).</summary>
    public const double MinDb = -30.0;

    public const double MaxDb = 12.0;

    /// <summary>-30 bis +12 dB in Schritten von 0,5 (4.5 erlaubt -60 bis +12). NaN lässt den Wert stehen.</summary>
    public double PegelDb
    {
        get => _pegelDb;
        set
        {
            if (double.IsNaN(value)) return;
            var neu = Begrenzt(value);
            if (Math.Abs(neu - _pegelDb) < 0.01) return;
            PegelZeigen(neu);
            _ = _netze.PegelSetzenAsync(this, (float)neu);
        }
    }

    public string PegelText => _pegelDb.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";

    /// <summary>Ein Wert für den Regler: auf 0,5 dB gerundet und auf <see cref="MinDb"/> bis <see cref="MaxDb"/> begrenzt (auch für die Wippe in "Wer ist da").</summary>
    internal static double Begrenzt(double db) => Math.Clamp(Math.Round(db * 2) / 2, MinDb, MaxDb);

    /// <summary>Zeigt einen Pegel, der anderswo gesetzt wurde (Wippe in "Wer ist da", Lage), ohne ihn noch einmal zu senden.</summary>
    internal void PegelZeigen(double db)
    {
        if (Math.Abs(db - _pegelDb) < 0.01) return;
        _pegelDb = db;
        OnPropertyChanged(nameof(PegelDb));
        OnPropertyChanged(nameof(PegelText));
    }

    internal void Setzen(FunkerInfo f, FunkLage l)
    {
        // Vom Server kommt der Pegel, den ein anderer Client oder eine ältere Fassung gesetzt hat (bis -60 dB, auch Unsinn): Der Regler
        // zeigt nur MinDb bis MaxDb, ein Wert darüber hinaus liefe als Knauf ins Leere. Zur Anzeige begrenzt, die Maschine bekommt den
        // gespeicherten Wert unverändert. Seit 0.11.5 aus dem Stand des Modells (NetzeViewModel.PegelVon), nicht mehr aus jeder Lage: Die
        // trägt nur den Wert der Anmeldung und setzte einen eben verstellten Regler beim nächsten Push zurück.
        PegelZeigen(_netze.PegelVon(f.KontoId));
        Name = f.Name;
        Online = f.Online;
        Dauersend = _netze.Funkfeld?.Dauersend.ZeichenVon(f.KontoId);
        var rolle = l.Rollen.FirstOrDefault(r => r.Id == f.RolleId)?.Name;
        var schiff = l.Schiffe.FirstOrDefault(s => s.Id == f.SchiffId)?.Name;
        Platz = !f.Online ? "offline" : rolle is null ? "ohne Platz" : rolle + (schiff is null ? "" : ", " + schiff);
    }
}

/// <summary>
/// A9, A10 unter Einstellungen > Empfangen (bis 0.9.3 Bereich Netze): Lautstärke und VA/Taste eines Netzes. Die Werte gehören dem Funkfeld (dieselben wie auf der
/// Kachel); ändert sich einer dort, frischt der Bereich die Zeile auf (<see cref="FunkfeldViewModel.NetzGeaendert"/>).
/// </summary>
public sealed partial class NetzRegelung : ObservableObject
{
    private readonly FunkfeldViewModel _feld;

    internal NetzRegelung(FunkfeldViewModel feld, ushort netzId, string netz)
    {
        _feld = feld;
        NetzId = netzId;
        Netz = netz;
    }

    public ushort NetzId { get; }

    [ObservableProperty]
    public partial string Netz { get; private set; }

    public double LautstaerkeDb
    {
        get => _feld.LautstaerkeVon(NetzId);
        set => _feld.LautstaerkeSetzen(NetzId, value);
    }

    public string LautstaerkeText => FunkfeldViewModel.LautstaerkeText(LautstaerkeDb);

    /// <summary>A10; mit dem Klangmodul Sprachaktivierung aus (0.9.0) Taste und gesperrt, die Wahl je Kanal bleibt gemerkt.</summary>
    public bool Sprachaktivierung
    {
        get => _feld.SprachaktivierungModulAn && _feld.SprachaktivierungVon(NetzId);
        set => _feld.SprachaktivierungSetzen(NetzId, value);
    }

    public bool KannSprachaktivierung => !_feld.NurTasteVon(NetzId) && _feld.SprachaktivierungModulAn;

    public string AusloesungText => Sprachaktivierung ? "VA" : "Taste";

    internal void NetzSetzen(string netz)
    {
        Netz = netz;
        Auffrischen();
    }

    /// <summary>Alle Werte neu melden (nach einer Änderung im Funkfeld oder einer neuen Lage).</summary>
    internal void Auffrischen()
    {
        OnPropertyChanged(nameof(LautstaerkeDb));
        OnPropertyChanged(nameof(LautstaerkeText));
        OnPropertyChanged(nameof(Sprachaktivierung));
        OnPropertyChanged(nameof(KannSprachaktivierung));
        OnPropertyChanged(nameof(AusloesungText));
    }
}

/// <summary>
/// A9: auf welchem Ohr ein Netz spielt, als drei Knöpfe Links, Beide, Rechts (Standard beide). Gemerkt je Netz und
/// sofort an den Klang.
/// </summary>
public sealed partial class OhrWahl : ObservableObject
{
    private readonly NetzeViewModel _netze;

    internal OhrWahl(NetzeViewModel netze, ushort netzId, string netz)
    {
        _netze = netze;
        NetzId = netzId;
        Netz = netz;
    }

    public ushort NetzId { get; }

    [ObservableProperty]
    public partial string Netz { get; private set; }

    public bool Links
    {
        get => Wert < -0.5f;
        set => Waehlen(value, -1f);
    }

    public bool Beide
    {
        get => Wert is >= -0.5f and <= 0.5f;
        set => Waehlen(value, 0f);
    }

    public bool Rechts
    {
        get => Wert > 0.5f;
        set => Waehlen(value, 1f);
    }

    private float Wert => _netze.PanoramaVon(NetzId);

    internal void NetzSetzen(string netz) => Netz = netz;

    /// <summary>Die Seite wurde anderswo gesetzt (seit 0.10.0: "Zurück auf Werkseinstellung" unter Empfangen > Erweitert): neu melden.</summary>
    internal void Auffrischen()
    {
        OnPropertyChanged(nameof(Links));
        OnPropertyChanged(nameof(Beide));
        OnPropertyChanged(nameof(Rechts));
    }

    /// <summary>Ein Knopf wurde gewählt; das Abwählen der anderen durch die Gruppe ändert nichts.</summary>
    private void Waehlen(bool gewaehlt, float panorama)
    {
        if (!gewaehlt || Math.Abs(Wert - panorama) < 0.01f) return;
        _netze.PanoramaSetzen(NetzId, panorama);
        OnPropertyChanged(nameof(Links));
        OnPropertyChanged(nameof(Beide));
        OnPropertyChanged(nameof(Rechts));
    }
}
