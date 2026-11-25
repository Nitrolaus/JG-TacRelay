// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// "Wer ist da" (A48): ein Knopf im Hauptfenster und Strg+6 öffnen eine Spalte rechts mit allen gerade Eingeloggten, mit Suche. Seit 0.11.5
/// (Sarevoc 13.10.2026: "Ein Name-Button je Zeile, alle untereinander, alle gleich lang; nicht abschneiden ... Keine Kacheln drei je Reihe mehr, keine
/// Gruppierung nach Untergilde"; Sarevoc 14.10.2026: Stummschalten, Statusgrafik und Wippe hinter dem Namen) eine flache Liste, eine Zeile je Person,
/// online zuerst, dann nach Rang (Admin, Führung, Einsatzleitung, alle anderen; die Stufe aus dem Kontenverzeichnis, das die Einsatzleitung beim
/// Öffnen holt, sonst aus den Zeichen), dann nach Name. Je Zeile genau vier Knöpfe (<see cref="WerIstDaZeile"/>): der Name (öffnet mit der
/// Einsatzleitung die Raid-Seite), das Mikrofon (Einsatzleitung schaltet stumm, blutrot solange stumm), der Status (grün in Kanälen der Raid-Seite,
/// gelb in keinem; als eingerichteter Admin im Admin-Setup ein Sprung in die Verwaltung zu diesem Mitglied) und die Wippe (links leiser, rechts
/// lauter, je 2 dB, das eigene Hören). Bis 0.11.4 standen Personenkacheln drei je Reihe, nach Untergilde oder Einheit
/// gruppiert, mit dem gelben "!" der Dauersend-Dämpfung und einer Mitgliedskarte beim Klick. Die Einsatzleitung kann "auch offline"
/// einschalten: dann kommen die Offline-Konten aus Praesenz und Kontenverzeichnis dazu, nach den Angemeldeten. Gezählt werden weiter nur die
/// Angemeldeten.
/// </summary>
public sealed partial class WerIstDaViewModel : ObservableObject
{
    /// <summary>
    /// So lange gilt ein Klick auf das Mikrofon: die Höchstdauer am Server (Fristen.StummMaxSekunden, 4.5 Stummschalten, eine Stunde). Sarevoc
    /// 12.10.2026: "Klick = stumm, Klick = wieder normal, keine Zeitwahl" - stumm bis zum zweiten Klick, bis "Abend beenden" oder bis die Stunde
    /// um ist; länger erlaubt der Server nicht.
    /// </summary>
    public const ushort StummSekunden = 3600;

    /// <summary>Ein Klick auf die Wippe ändert den eigenen Pegel für diese Person um so viel (Sarevoc 14.10.2026: "seitlich, rechts lauter, links leiser").</summary>
    public const double PegelSchritt = 2.0;

    /// <summary>Das Kontenverzeichnis liefert der Server höchstens alle 5 s (FUNKPROTOKOLL 4.8); das Brett holt es getrennt.</summary>
    private static readonly TimeSpan VerzeichnisAbstand = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Von selbst holt die Spalte das Kontenverzeichnis höchstens so oft (seit der Gegenprüfung 0.11.5): wenn jemand angemeldet ist, dessen Stufe sie
    /// nicht kennt. Der Server teilt sich eine Marke je 5 s (Stoß 2) zwischen Wer ist da und Brett; so bleibt dem Brett genug.
    /// </summary>
    private static readonly TimeSpan VerzeichnisVonSelbstAbstand = TimeSpan.FromSeconds(30);

    private readonly IFunkDienst _dienst;
    private readonly Einstellungsablage _ablage;
    private readonly IOberflaechentakt _takt;
    private readonly Func<DateTimeOffset> _jetzt;
    private IReadOnlyList<VerzeichnisEintrag> _verzeichnis = [];
    private DateTimeOffset? _verzeichnisGeholt;
    private DateTimeOffset? _vonSelbstGeholt;
    private bool _holtVerzeichnis;
    private NetzeViewModel? _netze;
    private Action? _raidOeffnen;
    private Action<uint>? _mitgliedZeigen;
    private string? _aktionsFehler;

    /// <summary>Wann die erste laufende Stummschaltung einer Zeile endet (eigene Uhr); dann baut <see cref="Takt"/> neu. Ohne: <c>null</c>.</summary>
    private DateTimeOffset? _naechstesStummEnde;

    /// <param name="jetzt">Uhr für Stummschaltungen und den Mindestabstand des Kontenverzeichnisses (Tests: fest).</param>
    public WerIstDaViewModel(IFunkDienst dienst, Einstellungsablage ablage, IOberflaechentakt takt, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _ablage = ablage;
        _takt = takt;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        _dienst.LageGeaendert += () => _takt.Einreihen(Aktualisieren);
        Aktualisieren();
    }

    /// <summary>
    /// Verdrahtet, was die Zeilen anstoßen (seit 0.11.5; das Hauptfenster-Modell ruft es einmal): die Wippe geht über den Pegel je Person unter
    /// Empfangen (<paramref name="netze"/>, dieselbe Stelle wie der Regler dort), der Name öffnet die Raid-Seite, der Status die Verwaltung bei
    /// einem Mitglied (nur mit dem Verwaltungsteil des Admin-Setups). Ohne <paramref name="netze"/> gibt es keine Wippe.
    /// </summary>
    internal void Anbinden(NetzeViewModel? netze, Action? raidOeffnen, Action<uint>? mitgliedZeigen)
    {
        if (_netze is not null) _netze.PegelGeaendert -= Aktualisieren;
        _netze = netze;
        _raidOeffnen = raidOeffnen;
        _mitgliedZeigen = mitgliedZeigen;
        if (_netze is not null) _netze.PegelGeaendert += Aktualisieren;
        Aktualisieren();
    }

    /// <summary>Die Spalte ist offen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InSpalte))]
    public partial bool Offen { get; set; }

    /// <summary>
    /// Die Liste steht in einem eigenen Fenster statt als Spalte (seit 0.11.5, Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster
    /// ablösbar"). Das Fenster baut Hauptfenster.xaml.cs; wer es schließt, legt die Liste zurück in die Spalte.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InSpalte))]
    [NotifyPropertyChangedFor(nameof(LoesenText))]
    [NotifyPropertyChangedFor(nameof(LoesenHilfe))]
    public partial bool Geloest { get; set; }

    /// <summary>Die Spalte im Hauptfenster ist zu sehen: offen und nicht gelöst.</summary>
    public bool InSpalte => Offen && !Geloest;

    public string LoesenText => Geloest ? "andocken" : "lösen";

    public string LoesenHilfe => Geloest ? "Legt Wer ist da zurück als Spalte ins Hauptfenster; Schließen des Fensters tut dasselbe."
        : "Löst Wer ist da als eigenes Fenster vom Hauptfenster, das du frei hinschieben kannst; Schließen legt es zurück.";

    /// <summary>"Wer ist da (23)" für den Knopf.</summary>
    [ObservableProperty]
    public partial string KnopfText { get; private set; } = "Wer ist da";

    /// <summary>
    /// Der Knopf in schmalen Fenstern (seit 0.10.0, E23, unter 900 Breite): nur die Zahl neben dem Zeichen, etwa "23"; getrennt oder ohne
    /// jemanden online leer, dann steht nur das Zeichen da. Der Name bleibt im Hilfetext und für Bildschirmleser.
    /// </summary>
    [ObservableProperty]
    public partial string KnopfKurz { get; private set; } = "";

    [ObservableProperty]
    public partial int Anzahl { get; private set; }

    [ObservableProperty]
    public partial string Suche { get; set; } = "";

    /// <summary>Die Personen, eine Zeile je Person, in der Reihenfolge der Liste; eine Zeile je Konto für die ganze Lebensdauer.</summary>
    public ObservableCollection<WerIstDaZeile> Zeilen { get; } = [];

    /// <summary>Niemand zu sehen: nicht verbunden, oder die Suche findet nichts.</summary>
    [ObservableProperty]
    public partial string? Leer { get; private set; }

    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    /// <summary>
    /// "auch offline" (0.11.0, nur für die Einsatzleitung): zeigt auch, wer offline ist, nach den Angemeldeten. Wird nicht gespeichert; endet
    /// die Einsatzleitung, geht es aus.
    /// </summary>
    [ObservableProperty]
    public partial bool AuchOffline { get; set; }

    /// <summary>Der Schalter "auch offline" steht nur da, solange das eigene Konto einteilen darf (ZUWEISEN, auch aus einem Auftrag).</summary>
    [ObservableProperty]
    public partial bool AuchOfflineSichtbar { get; private set; }

    /// <summary>Strg+6 und der Knopf im Hauptfenster: öffnen oder schließen.</summary>
    [RelayCommand]
    private async Task Umschalten()
    {
        Offen = !Offen;
        if (Offen) await NachladenAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Schliessen() => Offen = false;

    /// <summary>Als eigenes Fenster lösen oder zurück in die Spalte legen.</summary>
    [RelayCommand]
    private void Loesen() => Geloest = !Geloest;

    /// <summary>
    /// Baut die Liste neu auf (beim Öffnen und mit "neu"; seit 0.11.8 ohne die Untergilden, die gibt es nicht mehr); mit der
    /// Einsatzleitung holt es auch das Kontenverzeichnis, höchstens alle 5 s: für "auch offline" und seit der Gegenprüfung 0.11.5
    /// für die Stufe jedes Kontos (Rang und Mikrofon).
    /// Den Rollenkatalog braucht die Spalte seit 0.11.5 nicht mehr (er stand auf der Mitgliedskarte).
    /// </summary>
    [RelayCommand]
    public async Task NachladenAsync()
    {
        if (!_dienst.Lage.IstAktiv) return;
        Fehler = null;
        _aktionsFehler = null;
        if (AuchOfflineSichtbar) await VerzeichnisHolenAsync().ConfigureAwait(true);
        Aktualisieren();
    }

    /// <summary>
    /// Das Kontenverzeichnis (0x0038, ZUWEISEN): beim Öffnen, mit "neu" und beim Einschalten von "auch offline", nie öfter als alle 5 s (der Server
    /// erlaubt eins je 5 s mit Spielraum 2, das Brett holt es getrennt). Es bringt die Offline-Konten und seit der Gegenprüfung 0.11.5 die Stufe
    /// jedes Kontos: Aus den Zeichen allein fehlte sie bei jedem, der sein Zeichen nicht zeigt, und ein solcher Admin stand unter "alle anderen",
    /// sein Mikrofon war für eine Einsatzleitung ohne Admin-Stufe klickbar, und der Server lehnte ab. Geht es schief, bleibt die alte Liste; bei
    /// "auch offline" sagt <see cref="Fehler"/> warum, und klappt es danach, geht diese Meldung weg (seit 0.11.0, Befund der Prüfung).
    /// </summary>
    internal async Task VerzeichnisHolenAsync()
    {
        var jetzt = _jetzt();
        if (_verzeichnisGeholt is { } zuletzt && (jetzt - zuletzt).Duration() < VerzeichnisAbstand) return;
        _verzeichnisGeholt = jetzt;
        _holtVerzeichnis = true;
        try
        {
            var a = await _dienst.KontenVerzeichnisAsync().ConfigureAwait(true);
            if (a.Ok)
            {
                _verzeichnis = a.Wert!;
                OfflineFehlerLoeschen();
            }
            else if (OfflineGezeigt())
            {
                Fehler = OfflineFehler + a.Fehler;
            }
        }
        finally
        {
            _holtVerzeichnis = false;
        }
    }

    /// <summary>
    /// Ist jemand angemeldet, dessen Stufe die Spalte nicht kennt (er kam nach dem letzten Holen), holt sie das Verzeichnis von selbst, höchstens
    /// alle 30 s und nur offen und mit der Einsatzleitung; bis dahin sagt sein Mikrofon, dass sie den Rang nicht kennt.
    /// </summary>
    private void VerzeichnisVonSelbst(bool stufeFehlt)
    {
        if (!stufeFehlt || !Offen || !AuchOfflineSichtbar || _holtVerzeichnis) return;
        var jetzt = _jetzt();
        if (_vonSelbstGeholt is { } zuletzt && (jetzt - zuletzt).Duration() < VerzeichnisVonSelbstAbstand) return;
        _vonSelbstGeholt = jetzt;
        _ = VonSelbstHolenAsync();
    }

    private async Task VonSelbstHolenAsync()
    {
        await VerzeichnisHolenAsync().ConfigureAwait(true);
        Aktualisieren();
    }

    /// <summary>
    /// Bildtakt (seit der Gegenprüfung 0.11.5): Eine Stummschaltung endet zur Zeit des Servers, ohne dass er etwas schickt. Ist die erste laufende
    /// abgelaufen, baut die Spalte neu; sonst blieb das Mikrofon blutrot, und ein Klick schickte ein Aufheben, das der Server mit "unbekannt"
    /// ablehnt. Wie <see cref="MikroViewModel.Takt"/> im Funkfeld.
    /// </summary>
    public void Takt()
    {
        if (_naechstesStummEnde is { } ende && _jetzt() >= ende) Aktualisieren();
    }

    /// <summary>So beginnt ein Fehler beim Holen des Kontenverzeichnisses.</summary>
    private const string OfflineFehler = "Auch offline: ";

    /// <summary>Nur der Fehler des Kontenverzeichnisses geht weg, einer eines Knopfs bleibt stehen.</summary>
    private void OfflineFehlerLoeschen()
    {
        if (Fehler?.StartsWith(OfflineFehler, StringComparison.Ordinal) == true) Fehler = null;
    }

    private bool OfflineGezeigt() => AuchOffline && AuchOfflineSichtbar;

    partial void OnAuchOfflineChanged(bool value)
    {
        if (value && AuchOfflineSichtbar && _dienst.Lage.IstAktiv)
        {
            _ = OfflineEinschaltenAsync();
            return;
        }
        // Aus: Ein Fehler beim Holen der Offline-Konten betrifft nichts mehr, was zu sehen ist.
        if (!value) OfflineFehlerLoeschen();
        Aktualisieren();
    }

    private async Task OfflineEinschaltenAsync()
    {
        await VerzeichnisHolenAsync().ConfigureAwait(true);
        Aktualisieren();
    }

    partial void OnSucheChanged(string value) => Aktualisieren();

    /// <summary>
    /// Baut Liste und Knopftext aus der Lage; im Oberflächentakt. Die Lage kommt mit jedem Push neu, auch mit jeder Sprechanzeige, im Raid
    /// mehrmals je Sekunde. Jedes Konto behält deshalb seine Zeile (<see cref="Sammlung.Abgleichen"/>), und eine Zeile ändert nur, was sich an ihr
    /// ändert; sonst bauten die Knöpfe der offenen Spalte bei jedem Funkspruch neu, der Fokus spränge weg, und ein Klick zwischen Drücken und
    /// Loslassen ginge verloren (Gegenprüfung Stufe 1). Seit 0.11.5 gilt das auch für die Wippe: Ein Klick ändert nur ihren Wert.
    /// </summary>
    internal void Aktualisieren()
    {
        var l = _dienst.Lage;
        AuchOfflineSichtbar = l is { IstAktiv: true, Konto.Einsatzleitung: true };
        // Endet die Einsatzleitung (Auftrag abgelaufen, abgegeben, getrennt), geht "auch offline" aus.
        if (!AuchOfflineSichtbar && AuchOffline)
        {
            AuchOffline = false;
            return;
        }
        var online = l.IstAktiv ? l.Funker.Where(f => f.Online).ToList() : [];
        Anzahl = online.Count;
        KnopfText = online.Count == 0 ? "Wer ist da" : "Wer ist da (" + online.Count.ToString(CultureInfo.CurrentCulture) + ")";
        KnopfKurz = online.Count == 0 ? "" : online.Count.ToString(CultureInfo.CurrentCulture);
        // Die Stufe: die eigene aus dem Konto, die anderer aus dem Kontenverzeichnis (nur mit der Einsatzleitung), sonst aus den Zeichen der
        // Praesenz (Kranz: Admin, Winkel: Führung). Wer sein Zeichen nicht zeigt, hat ohne Verzeichnis keine bekannte Stufe.
        var stufen = new Dictionary<uint, Stufe>();
        foreach (var e in _verzeichnis) stufen.TryAdd(e.KontoId, e.Stufe);
        var zeichen = new Dictionary<uint, IReadOnlyList<string>>();
        foreach (var f in l.Funker) zeichen.TryAdd(f.KontoId, f.Zeichen);
        Stufe? StufeVon(uint konto) =>
            l.Konto?.Id == konto ? l.Konto.Stufe
            : stufen.TryGetValue(konto, out var st) ? st
            : zeichen.TryGetValue(konto, out var z) ? WerIstDaZeile.StufeAusZeichen(z)
            : null;
        var alle = online.Select(f => WerIstDaEintrag.Aus(f, StufeVon(f.KontoId))).ToList();
        if (OfflineGezeigt() && l.IstAktiv)
        {
            // Offline aus der Praesenz (mit Platz oder Reservierung, samt Zeichen), dazu aus dem Verzeichnis, wer dort fehlt.
            var praesent = l.Funker.Select(f => f.KontoId).ToHashSet();
            alle.AddRange(l.Funker.Where(f => !f.Online).Select(f => WerIstDaEintrag.Aus(f, StufeVon(f.KontoId))));
            alle.AddRange(_verzeichnis.Where(e => !praesent.Contains(e.KontoId)).Select(e => WerIstDaEintrag.Aus(e)));
        }
        var s = Suche.Trim();
        var gezeigt = alle.Where(e => s.Length == 0 || e.Passt(s))
            .OrderBy(e => e.Online ? 0 : 1).ThenBy(e => e.Rang).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.KontoId).ToList();
        var umfeld = new WerIstDaUmfeld(l, _jetzt(), _ablage.Aktuell.Anzeige.ZeichenNebenNamen, _netze,
            NameKlickbar: _raidOeffnen is not null && BrettViewModel.Gilt(l),
            // Seit der Gegenprüfung 0.11.5 nur ein eingerichteter Admin: Ein vorgesehenes, gesperrtes oder halb eingerichtetes Konto sieht zwar den
            // Bereich Verwaltung (Konto.Verwaltung), vergibt dort aber nichts (Sarevoc: wer kein Raidleiter ist, sieht den Status nur als Anzeige).
            VerwaltungKlickbar: _mitgliedZeigen is not null && l is { IstAktiv: true, Konto.Admin: AdminStatus.Aktiv },
            StufeVon: StufeVon, Kanaele: BrettViewModel.Baumnetze(l));
        Sammlung.Abgleichen(Zeilen, gezeigt, e => e.KontoId, z => z.KontoId,
            e =>
            {
                var z = new WerIstDaZeile(this, e.KontoId);
                z.Setzen(e, umfeld);
                return z;
            },
            (z, e) => z.Setzen(e, umfeld));
        _naechstesStummEnde = Zeilen.Min(z => z.StummEnde);
        Leer = !l.IstAktiv ? "Nicht verbunden."
            : alle.Count == 0 ? "Gerade ist niemand da."
            : gezeigt.Count == 0 ? "Die Suche findet niemanden."
            : null;
        VerzeichnisVonSelbst(online.Any(f => f.KontoId != l.Konto?.Id && !stufen.ContainsKey(f.KontoId)));
    }

    // ---------------------------------------------------------------- Was die Knöpfe einer Zeile tun

    /// <summary>Der Name: die Raid-Seite öffnen (nur mit der Einsatzleitung, wenn es sie gibt; sonst nichts).</summary>
    internal void RaidOeffnen()
    {
        if (BrettViewModel.Gilt(_dienst.Lage)) _raidOeffnen?.Invoke();
    }

    /// <summary>Der Status im Admin-Setup: die Verwaltung, Seite Mitglieder, dieses Konto gewählt (nur als eingerichteter Admin).</summary>
    internal void MitgliedZeigen(WerIstDaZeile zeile)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        if (_dienst.Lage.Konto?.Admin == AdminStatus.Aktiv) _mitgliedZeigen?.Invoke(zeile.KontoId);
    }

    /// <summary>
    /// Das Mikrofon (Sarevoc 14.10.2026): Klick schaltet die Person in allen Netzen stumm (Netz 0) für <see cref="StummSekunden"/>; ist sie stumm, hebt
    /// der Klick jede laufende Stummschaltung auf (0 Sekunden, je Netz eine Anfrage, meist nur Netz 0). Was es danach ist, zeigt die Lage. Vorher
    /// rechnet die Spalte mit der Uhr von jetzt neu, damit eine eben abgelaufene Stummschaltung nicht als laufend gilt.
    /// </summary>
    internal async Task MikroAsync(WerIstDaZeile zeile)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        Aktualisieren();
        if (!zeile.MikroKlickbar) return;
        string? fehler = null;
        if (zeile.StummNetze.Count == 0)
        {
            fehler = await _dienst.StummschaltenAsync(zeile.KontoId, 0, StummSekunden).ConfigureAwait(true);
        }
        else
        {
            foreach (var netz in zeile.StummNetze.ToList())
                fehler ??= await _dienst.StummschaltenAsync(zeile.KontoId, netz, 0).ConfigureAwait(true);
        }
        AktionGemeldet(fehler is null ? null : zeile.Name + ": " + fehler);
        Aktualisieren();
    }

    /// <summary>Die Wippe: den eigenen Pegel für diese Person um <paramref name="schritt"/> dB, begrenzt auf -30 bis +12 wie der Regler unter Empfangen.</summary>
    internal async Task PegelUmAsync(WerIstDaZeile zeile, double schritt)
    {
        ArgumentNullException.ThrowIfNull(zeile);
        if (_netze is not { } netze || zeile.Eigene) return;
        var alt = netze.PegelVon(zeile.KontoId);
        // An der Grenze ändert ein Klick nichts und schickt nichts (die Hälfte ist dann ohnehin gesperrt).
        if (Math.Abs(FunkerZeile.Begrenzt(alt + schritt) - alt) < 0.01) return;
        var fehler = await netze.PegelSetzenAsync(zeile.KontoId, alt + schritt).ConfigureAwait(true);
        AktionGemeldet(fehler is null ? null : zeile.Name + ": " + fehler);
    }

    /// <summary>Ein Fehler eines Knopfs steht oben; der nächste gelungene Klick nimmt ihn wieder weg, andere Fehler bleiben.</summary>
    private void AktionGemeldet(string? fehler)
    {
        if (fehler is not null)
        {
            Fehler = _aktionsFehler = fehler;
            return;
        }
        if (_aktionsFehler is not null && Fehler == _aktionsFehler) Fehler = null;
        _aktionsFehler = null;
    }
}

/// <summary>
/// Die Farbe der Statusgrafik einer Zeile (Sarevoc 14.10.2026: "grün, dass der Funker in Channels zugeteilt ist, in gelb, dass der Funker nicht in
/// Channels ist"); grau, wenn es die Lage nicht weiß. Kanäle sind seit der Gegenprüfung 0.11.5 dieselben wie auf der Raid-Seite (die Zahl am
/// Namen links): alle Netze außer Global und den Einheitsnetzen (<see cref="BrettViewModel.Baumnetze"/>), Ruhe eingeschlossen.
/// </summary>
public enum Kanalstand
{
    /// <summary>Die Lage weiß es nicht: Die Felder anderer sieht nur die Einsatzleitung, und nur bei Konten in der Praesenz.</summary>
    Unbekannt,

    /// <summary>Mindestens ein Feld in einem Kanal der Raid-Seite (nicht Global, kein Einheitsnetz).</summary>
    Eingeteilt,

    /// <summary>Kein Feld in einem Kanal der Raid-Seite (höchstens Global und Einheitsnetze).</summary>
    OhneKanal,
}

/// <summary>Eine Person aus Praesenz oder Kontenverzeichnis, wie sie in "Wer ist da" eingeht (ohne Anzeige). <see cref="Stufe"/> ist <c>null</c>, wenn unbekannt.</summary>
internal sealed record WerIstDaEintrag(uint KontoId, string Name, string? ScName, ushort? SchiffId, ushort? RolleId, PlatzStatus? Platz,
    bool Online, IReadOnlyList<string> Zeichen, FunkerDetails? Details, Stufe? Stufe, int Rang)
{
    /// <summary>Die sichtbaren Abzeichen aus der Praesenz (seit 0.11.7); der Rang hängt weiter nur an den <see cref="Zeichen"/>.</summary>
    public IReadOnlyList<string> Abzeichen { get; init; } = [];

    /// <summary>Das Abzeichen, das die Person vorn zeigt (seit 0.11.7), sonst <c>null</c>.</summary>
    public string? AbzeichenVorn { get; init; }

    public static WerIstDaEintrag Aus(FunkerInfo f, Stufe? stufe) =>
        new(f.KontoId, f.Anzeigename, Abweichend(f.ScName, f.Anzeigename), f.SchiffId, f.RolleId, f.Platz, f.Online, f.Zeichen,
            f.Details, stufe, WerIstDaZeile.RangVon(f.Zeichen, stufe))
        {
            Abzeichen = f.Abzeichen,
            AbzeichenVorn = f.AbzeichenVorn,
        };

    /// <summary>Ein Konto aus dem Kontenverzeichnis (für "auch offline"): offline, ohne Zeichen und Felder (das Verzeichnis trägt keine).</summary>
    public static WerIstDaEintrag Aus(VerzeichnisEintrag e)
    {
        var name = e.DiscordName ?? e.ScName ?? e.Name;
        return new(e.KontoId, name, Abweichend(e.ScName, name), e.SchiffId, e.RolleId, e.Platz, false, [], null,
            e.Stufe, WerIstDaZeile.RangVon([], e.Stufe));
    }

    private static string? Abweichend(string? sc, string name) => sc is { } s && !string.Equals(s, name, StringComparison.Ordinal) ? s : null;

    /// <summary>
    /// Suche über Discord-Name und SC-Name, auch Teile ("max" findet "Mustermann/Max"). Der Name ist der Discord-Name, wenn es einen gibt. Das
    /// Kürzel der Untergilde ist seit 0.11.8 weg.
    /// </summary>
    public bool Passt(string suche) =>
        Name.Contains(suche, StringComparison.CurrentCultureIgnoreCase)
        || ScName?.Contains(suche, StringComparison.CurrentCultureIgnoreCase) == true;
}

/// <summary>
/// Was alle Zeilen beim Abgleich gleich brauchen: Lage, Uhr, Zeichen neben Namen, Pegel je Person, wohin Name und Status springen, die Stufe eines
/// Kontos (<c>null</c>: unbekannt; für das Aufheben fremder Stummschaltungen) und die Kanäle der Raid-Seite (für den Status).
/// </summary>
internal sealed record WerIstDaUmfeld(FunkLage Lage, DateTimeOffset Jetzt, bool ZeichenZeigen, NetzeViewModel? Netze, bool NameKlickbar, bool VerwaltungKlickbar,
    Func<uint, Stufe?> StufeVon, IReadOnlySet<ushort> Kanaele);

/// <summary>
/// Eine Person in "Wer ist da" (seit 0.11.5, Sarevoc 14.10.2026), von links nach rechts: der Name-Knopf (Abzeichen mit Online-Punkt und Name, alle
/// gleich breit; der Name kürzt nie, ein breiter steht in kleinerer Schrift), das Mikrofon, der Status und die Wippe. Jedes Konto behält seine
/// Zeile; was sich ändert, meldet sie einzeln (Fokus und Mausklick bleiben, auch während eines Funkspruchs). Bis 0.11.4 eine Personenkachel mit
/// Mitgliedskarte.
/// </summary>
public sealed partial class WerIstDaZeile : ObservableObject
{
    private readonly WerIstDaViewModel _liste;

    internal WerIstDaZeile(WerIstDaViewModel liste, uint kontoId)
    {
        _liste = liste;
        KontoId = kontoId;
    }

    public uint KontoId { get; }

    /// <summary>Discord-Name mit Schrägstrich, sonst Hauptname (Antwort 3).</summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    /// <summary>Der eindeutige SC-Name, wenn er vom Namen abweicht; schützt vor gleichnamigen Discord-Namen (steht im Hilfetext).</summary>
    [ObservableProperty]
    public partial string? ScName { get; private set; }

    /// <summary>Angemeldet: grüner Online-Punkt am Abzeichen; offline ein hohler Ring und der Name gedämpft (nur mit "auch offline").</summary>
    [ObservableProperty]
    public partial bool Online { get; private set; }

    /// <summary>
    /// Das Abzeichen vorn (seit 0.11.7: das gewählte, sonst das erste sichtbare; Kranz, Stern, Rang oder Tätigkeit); <c>null</c> ohne Zeichen oder
    /// wenn Zeichen neben Namen aus sind.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatAbzeichen))]
    public partial ImageSource? Abzeichen { get; private set; }

    public bool HatAbzeichen => Abzeichen is not null;

    /// <summary>Der erste Buchstabe des Namens, groß: steht ohne Abzeichen im Kreis.</summary>
    [ObservableProperty]
    public partial string Anfang { get; private set; } = "?";

    /// <summary>Alle sichtbaren Zeichen und Abzeichen (seit 0.11.7); der Hilfetext am Abzeichen nennt sie alle.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Zeichenbild> Zeichen { get; private set; } = [];

    [ObservableProperty]
    public partial string Platz { get; private set; } = "";

    /// <summary>Die eigene Zeile: kein Stummschalten seiner selbst, keine Wippe.</summary>
    [ObservableProperty]
    public partial bool Eigene { get; private set; }

    /// <summary>Admin 0, Führung 1, Einsatzleitung 2, alle anderen 3 (Sarevocs Reihenfolge wie links auf der Raid-Seite).</summary>
    [ObservableProperty]
    public partial int Rang { get; private set; }

    /// <summary>Alles in einem Satz, für Vorleser und Hilfetext: Name, SC-Name, Platz, Zeichen, online.</summary>
    [ObservableProperty]
    public partial string Beschreibung { get; private set; } = "";

    // ---- 1. Name ----

    /// <summary>Der Name öffnet die Raid-Seite: nur mit der Einsatzleitung (Bereich Raid, Strg+5); sonst ist er nur Anzeige.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RaidOeffnenCommand))]
    public partial bool NameKlickbar { get; private set; }

    [ObservableProperty]
    public partial string NameHilfe { get; private set; } = "";

    // ---- 2. Mikrofon ----

    /// <summary>Gerade stumm geschaltet (eine laufende Stummschaltung, meist in allen Netzen): das Mikrofon mit Querstrich in der roten Schrift der Farbstellung (Pinsel.RotSchrift).</summary>
    [ObservableProperty]
    public partial bool Stumm { get; private set; }

    /// <summary>Die Lage weiß, ob die Person stumm ist: die eigene Zeile, sonst nur die Einsatzleitung für Konten in der Praesenz.</summary>
    [ObservableProperty]
    public partial bool StummBekannt { get; private set; }

    /// <summary>
    /// Raidleiter oder höher, die es dürfen (ZUWEISEN: Einsatzleitung, Admin; einen Admin nur ein Admin, dafür muss die Stufe bekannt sein; eine
    /// fremde Stummschaltung hebt nur auf, wer es darf, <see cref="DarfAufheben"/>), nicht in der eigenen Zeile.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MikroCommand))]
    public partial bool MikroKlickbar { get; private set; }

    [ObservableProperty]
    public partial string MikroHilfe { get; private set; } = "";

    /// <summary>Was der Hilfetext sagt, solange das Mikrofon nur Anzeige ist (Zustand und warum).</summary>
    [ObservableProperty]
    public partial string MikroGesperrt { get; private set; } = "";

    [ObservableProperty]
    public partial string MikroAnsage { get; private set; } = "";

    /// <summary>Die Netze der laufenden Stummschaltungen (0 alle); ein Klick hebt sie auf.</summary>
    internal IReadOnlyList<ushort> StummNetze { get; private set; } = [];

    /// <summary>Wann die erste laufende Stummschaltung endet (eigene Uhr), sonst <c>null</c>; danach rechnet <see cref="WerIstDaViewModel.Takt"/> neu.</summary>
    internal DateTimeOffset? StummEnde { get; private set; }

    // ---- 3. Status ----

    [ObservableProperty]
    public partial Kanalstand Kanalstand { get; private set; }

    /// <summary>Die Zahl im grünen Status: in wie vielen Kanälen der Raid-Seite die Person sitzt, höchstens 9; leer ohne Kanal oder unbekannt.</summary>
    [ObservableProperty]
    public partial string KanalzahlText { get; private set; } = "";

    /// <summary>Im Admin-Setup (Verwaltungsteil) als eingerichteter Admin (AdminStatus AKTIV): ein Klick zeigt das Mitglied in der Verwaltung.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(AbzeichenOeffnenCommand))]
    public partial bool StatusKlickbar { get; private set; }

    /// <summary>
    /// Das Abzeichen vor dem Namen (Sarevoc 14.10.2026: "erst Abzeichen, dann Name; wenn ein Admin auf das Abzeichen klickt, kommt er zu den
    /// Einstellungen, wo Abzeichen vergeben werden"): klickbar wie der Status, ein Klick zeigt das Mitglied in der Verwaltung.
    /// </summary>
    [ObservableProperty]
    public partial string AbzeichenHilfe { get; private set; } = "";

    [ObservableProperty]
    public partial string AbzeichenGesperrt { get; private set; } = "";

    [ObservableProperty]
    public partial string AbzeichenAnsage { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusHilfe { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusGesperrt { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusAnsage { get; private set; } = "";

    // ---- 4. Wippe ----

    /// <summary>Die Wippe steht in jeder Zeile außer der eigenen (es ist das eigene Hören).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LeiserCommand), nameof(LauterCommand))]
    public partial bool HatWippe { get; private set; }

    /// <summary>Der eigene Pegel für diese Person in dB, wie der Regler unter Empfangen > Funker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PegelText))]
    [NotifyCanExecuteChangedFor(nameof(LeiserCommand), nameof(LauterCommand))]
    public partial double PegelDb { get; private set; }

    [ObservableProperty]
    public partial string LeiserHilfe { get; private set; } = "";

    [ObservableProperty]
    public partial string LauterHilfe { get; private set; } = "";

    [ObservableProperty]
    public partial string LeiserGesperrt { get; private set; } = "";

    [ObservableProperty]
    public partial string LauterGesperrt { get; private set; } = "";

    [ObservableProperty]
    public partial string LeiserAnsage { get; private set; } = "";

    [ObservableProperty]
    public partial string LauterAnsage { get; private set; } = "";

    /// <summary>"+2,0 dB", wie unter Empfangen > Funker.</summary>
    public string PegelText => PegelDb.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";

    private bool KannLeiser() => HatWippe && PegelDb > FunkerZeile.MinDb + 0.01;

    private bool KannLauter() => HatWippe && PegelDb < FunkerZeile.MaxDb - 0.01;

    [RelayCommand(CanExecute = nameof(NameKlickbar))]
    private void RaidOeffnen() => _liste.RaidOeffnen();

    [RelayCommand(CanExecute = nameof(MikroKlickbar))]
    private Task Mikro() => _liste.MikroAsync(this);

    [RelayCommand(CanExecute = nameof(StatusKlickbar))]
    private void Status() => _liste.MitgliedZeigen(this);

    [RelayCommand(CanExecute = nameof(StatusKlickbar))]
    private void AbzeichenOeffnen() => _liste.MitgliedZeigen(this);

    /// <summary>Links auf der Wippe; nicht gesperrt während der Anfrage, damit schnelle Klicks alle zählen.</summary>
    [RelayCommand(CanExecute = nameof(KannLeiser))]
    private void Leiser() => _ = _liste.PegelUmAsync(this, -WerIstDaViewModel.PegelSchritt);

    [RelayCommand(CanExecute = nameof(KannLauter))]
    private void Lauter() => _ = _liste.PegelUmAsync(this, WerIstDaViewModel.PegelSchritt);

    /// <summary>Admin 0, Führung 1, Einsatzleitung 2, alle anderen 3: aus den Zeichen der Praesenz und, wo bekannt, der Stufe.</summary>
    internal static int RangVon(IReadOnlyList<string> zeichen, Stufe? stufe) =>
        stufe == Stufe.Admin || zeichen.Contains(Zeichensatz.Admin) ? 0
        : stufe == Stufe.Raidleiter || zeichen.Contains(Zeichensatz.Fuehrung) ? 1
        : zeichen.Contains(Zeichensatz.Einsatzleitung) ? 2
        : 3;

    /// <summary>Die Stufe, die ein Zeichen verrät: der Kranz einen Admin, die Winkel die Führung; sonst <c>null</c> (unbekannt, nicht FUNKER).</summary>
    internal static Stufe? StufeAusZeichen(IReadOnlyList<string> zeichen) =>
        zeichen.Contains(Zeichensatz.Admin) ? Stufe.Admin : zeichen.Contains(Zeichensatz.Fuehrung) ? Stufe.Raidleiter : null;

    /// <summary>Wie hoch eine Stufe steht (FUNKER 0, RAIDLEITER 1, ADMIN 2); die Aufzählung des Protokolls steht in der Gegenrichtung.</summary>
    private static int Hoehe(Stufe? stufe) => stufe switch
    {
        Stufe.Admin => 2,
        Stufe.Raidleiter => 1,
        _ => 0,
    };

    /// <summary>
    /// Darf <paramref name="ich"/> eine laufende Stummschaltung von <paramref name="ziel"/> aufheben? Wie der Server (Zustandsschreiber,
    /// DarfStummAufheben, 6.10): wer sie gesetzt hat, sonst nie der Betroffene selbst, ein Admin immer, sonst nur eine höhere Stufe als die des
    /// Setzenden. Kennt die Spalte dessen Stufe nicht, gilt sie wie beim Server ein gelöschter Setzender als Admin (seit der Gegenprüfung 0.11.5).
    /// </summary>
    internal static bool DarfAufheben(KontoInfo ich, uint ziel, uint durch, Func<uint, Stufe?> stufeVon)
    {
        ArgumentNullException.ThrowIfNull(ich);
        ArgumentNullException.ThrowIfNull(stufeVon);
        if (durch == ich.Id) return true;
        if (ziel == ich.Id) return false;
        if (ich.Stufe == Stufe.Admin) return true;
        return stufeVon(durch) is { } setzer && Hoehe(ich.Stufe) > Hoehe(setzer);
    }

    /// <summary>Übernimmt Stand und Rechte aus Eintrag und Lage; meldet nur, was sich ändert.</summary>
    internal void Setzen(WerIstDaEintrag e, WerIstDaUmfeld u)
    {
        var l = u.Lage;
        Name = e.Name;
        ScName = e.ScName;
        Online = e.Online;
        // Die Bilder kommen je Aufruf in einer neuen Liste; gemeldet wird nur, wenn sich die Zeichen ändern (sonst bei jedem Funkspruch).
        var zeichen = u.ZeichenZeigen ? Zeichensatz.Sichtbare(e.Zeichen, e.Abzeichen) : [];
        if (!zeichen.SequenceEqual(Zeichen)) Zeichen = zeichen;
        // Seit 0.11.7 das Abzeichen, das die Person vorn zeigt; ohne Wahl das erste sichtbare (Automatik, Ränge, Tätigkeiten).
        Abzeichen = Zeichensatz.Vorn(Zeichen, e.AbzeichenVorn)?.Bild;
        Anfang = Name.Length == 0 ? "?" : StringInfo.GetNextTextElement(Name).ToUpper(CultureInfo.CurrentCulture);
        Platz = Platztext(l, e.SchiffId, e.RolleId) is { } p ? e.Platz == PlatzStatus.Reserviert ? p + " (reserviert)" : p : "noch nicht eingeteilt";
        Eigene = l.Konto?.Id == KontoId;
        Rang = e.Rang;
        Beschreibung = Name + (ScName is null ? "" : ", SC " + ScName) + ", " + Platz
            + (Zeichen.Count == 0 ? "" : ", " + string.Join(", ", Zeichen.Select(z => z.Titel))) + (Online ? ", online" : ", offline");

        NameKlickbar = u.NameKlickbar;
        NameHilfe = Beschreibung + (NameKlickbar ? ". Klick öffnet die Funkfeld-Zuteilung." : ".");

        MikroSetzen(e, u);
        StatusSetzen(e, u);
        WippeSetzen(u);
    }

    private void MikroSetzen(WerIstDaEintrag e, WerIstDaUmfeld u)
    {
        var l = u.Lage;
        var ich = l.Konto;
        // Laufende Stummschaltungen (Netz, bis wann in eigener Zeit, durch wen): die eigenen aus Mitgliedschaften, die anderer aus den Details der
        // Einsatzleitung.
        IReadOnlyList<(ushort Netz, DateTimeOffset Bis, uint Durch)>? laufend = Eigene
            ? [.. l.StummschaltungenAn(u.Jetzt).Select(s => (s.NetzId, s.Bis - l.ZeitVersatz, s.Durch))]
            : e.Details is { } d
                ? [.. d.Stumm.Where(s => s.Bis > (u.Jetzt + l.ZeitVersatz).ToUnixTimeMilliseconds())
                    .OrderBy(s => s.NetzId).Select(s => (s.NetzId, DateTimeOffset.FromUnixTimeMilliseconds(s.Bis) - l.ZeitVersatz, s.Durch))]
                : null;
        StummBekannt = laufend is not null;
        Stumm = laufend is { Count: > 0 };
        StummNetze = laufend is null ? [] : [.. laufend.Select(s => s.Netz)];
        StummEnde = laufend is { Count: > 0 } ? laufend.Min(s => s.Bis) : null;
        var wer = Eigene ? "Du bist" : Name + " ist";
        var zustand = laufend is null
            ? "Ob " + Name + " stumm geschaltet ist, " + (ich?.Einsatzleitung == true ? "weiß Wer ist da ohne Sitzung nicht." : "sieht nur die Einsatzleitung.")
            : laufend.Count == 0
                ? wer + " nicht stumm geschaltet."
                : wer + " stumm geschaltet " + string.Join(", ", laufend.Select(s => (s.Netz switch
                {
                    0 => "",
                    1 => "in Global ",
                    _ => "in " + l.KanalName(s.Netz) + " ",
                }) + "bis " + s.Bis.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture))) + ".";
        // Seit der Gegenprüfung 0.11.5 klickbar nur, wo der Server es zulässt: Ob jemand Admin ist, sagt die Stufe aus dem Kontenverzeichnis (die
        // Zeichen fehlen bei jedem, der sein Zeichen nicht zeigt), und eine fremde Stummschaltung hebt nur auf, wer es darf (DarfAufheben).
        var fremd = laufend?.Where(s => ich is not null && !DarfAufheben(ich, KontoId, s.Durch, u.StufeVon)).Select(s => (uint?)s.Durch).FirstOrDefault();
        string? grund = Eigene ? "Dich selbst schaltest du hier nicht stumm."
            : ich is not { Einsatzleitung: true } || !l.IstAktiv ? "Stummschalten darf nur die Einsatzleitung (Admin oder Auftrag für den Abend)."
            : Rang == 0 && ich.Stufe != Stufe.Admin ? "Einen Admin schaltet nur ein Admin stumm."
            : e.Stufe is null && ich.Stufe != Stufe.Admin ? "Den Rang von " + Name + " kennt Wer ist da gerade nicht; \"neu\" holt ihn."
            : laufend is null ? "Ohne Sitzung lässt es sich hier nicht schalten."
            : fremd is { } durch ? "Aufheben darf nur, wer es gesetzt hat (" + l.HauptnameVon(durch) + "), eine höhere Stufe oder ein Admin."
            : null;
        MikroKlickbar = grund is null;
        MikroHilfe = zustand + (grund is not null ? ""
            : Stumm ? " Klick hebt es auf."
            : " Klick schaltet " + Name + " in allen Kanälen stumm, bis du es aufhebst oder den Abend beendest (höchstens eine Stunde).");
        MikroGesperrt = grund is null ? "" : zustand + " " + grund;
        MikroAnsage = MikroKlickbar ? (Stumm ? Name + " wieder hörbar machen" : Name + " stumm schalten")
            : Name + (laufend is null ? ": stumm unbekannt" : Stumm ? ": stumm" : ": nicht stumm");
    }

    private void StatusSetzen(WerIstDaEintrag e, WerIstDaUmfeld u)
    {
        var l = u.Lage;
        IReadOnlyList<FeldInfo>? felder = Eigene ? l.Felder : e.Details?.Felder;
        // Wie die Zahl am Namen auf der Raid-Seite (seit der Gegenprüfung 0.11.5): ein Feld in einem ihrer Kanäle, also nicht Global und kein
        // Einheitsnetz; bis dahin zählte jedes Netz außer Global, und wer nur auf einem Schiff saß, war hier grün und dort ohne Kanal.
        var zahl = felder is null ? 0 : BrettViewModel.Kanalzahl(felder, u.Kanaele);
        Kanalstand = felder is null ? Kanalstand.Unbekannt
            : zahl > 0 ? Kanalstand.Eingeteilt
            : Kanalstand.OhneKanal;
        // Sarevoc 14.10.2026: "Bei dem grünen Punkt ändern wir den Punkt in eine Zahl, 1 bedeutet in einem Kanal und so weiter; mehr als 9 müssen
        // nicht sein." Eine Rolle hat höchstens neun Felder.
        KanalzahlText = zahl > 0 ? Math.Min(zahl, 9).ToString(CultureInfo.CurrentCulture) : "";
        var wer = Eigene ? "Du bist" : Name + " ist";
        var zustand = Kanalstand switch
        {
            Kanalstand.Eingeteilt => wer + (KanalzahlText == "1" ? " in einem Kanal eingeteilt." : " in " + KanalzahlText + " Kanälen eingeteilt."),
            Kanalstand.OhneKanal => wer + " in keinem Kanal.",
            _ => "Ob " + Name + " in Kanälen eingeteilt ist, " + (l.Konto?.Einsatzleitung == true ? "weiß Wer ist da ohne Sitzung nicht." : "sieht nur die Einsatzleitung."),
        };
        StatusKlickbar = u.VerwaltungKlickbar;
        StatusHilfe = zustand + (StatusKlickbar ? " Klick öffnet die Verwaltung bei diesem Mitglied." : "");
        StatusGesperrt = zustand;
        var zeichen = Zeichen.Count == 0 ? Name + " trägt kein Abzeichen." : Name + ": " + string.Join(", ", Zeichen.Select(z => z.Titel)) + ".";
        var online = Online ? " Online." : " Offline.";
        AbzeichenHilfe = zeichen + online + (StatusKlickbar ? " Klick öffnet die Verwaltung bei diesem Mitglied." : "");
        AbzeichenGesperrt = zeichen + online;
        AbzeichenAnsage = Name + ", Abzeichen" + (Zeichen.Count == 0 ? ": keins" : ": " + string.Join(", ", Zeichen.Select(z => z.Titel))) + (Online ? ", online" : ", offline");
        StatusAnsage = Name + Kanalstand switch
        {
            Kanalstand.Eingeteilt => KanalzahlText == "1" ? ": in einem Kanal" : ": in " + KanalzahlText + " Kanälen",
            Kanalstand.OhneKanal => ": in keinem Kanal",
            _ => ": Kanäle unbekannt",
        };
    }

    private void WippeSetzen(WerIstDaUmfeld u)
    {
        HatWippe = u.Netze is not null && !Eigene;
        PegelDb = HatWippe ? u.Netze!.PegelVon(KontoId) : 0;
        var jetzt = Name + " bei dir: " + PegelText + ".";
        LeiserHilfe = jetzt + " Klick: 2 dB leiser, bis " + FunkerZeile.MinDb.ToString("0", CultureInfo.CurrentCulture) + " dB. Nur für dich.";
        LauterHilfe = jetzt + " Klick: 2 dB lauter, bis +" + FunkerZeile.MaxDb.ToString("0", CultureInfo.CurrentCulture) + " dB. Nur für dich.";
        LeiserGesperrt = jetzt + " Leiser geht nicht.";
        LauterGesperrt = jetzt + " Lauter geht nicht.";
        LeiserAnsage = Name + " leiser";
        LauterAnsage = Name + " lauter";
    }

    /// <summary>"Schiff 2, Pilot" aus NetzListe und Praesenz, oder <c>null</c> ohne Platz.</summary>
    internal static string? Platztext(FunkLage l, ushort? schiffId, ushort? rolleId)
    {
        var schiff = schiffId is { } s ? l.Schiffe.FirstOrDefault(x => x.Id == s)?.Name : null;
        var rolle = rolleId is { } r ? l.Rollen.FirstOrDefault(x => x.Id == r)?.Name : null;
        return (schiff, rolle) switch
        {
            (null, null) => null,
            (null, { } ro) => ro,
            ({ } sc, null) => sc,
            ({ } sc, { } ro) => sc + ", " + ro,
        };
    }
}
