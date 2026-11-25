// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Die Raid-Seite seit 0.11.2 (Sarevoc 13.10.2026, ZUORDNUNG-ENTWURF "Stammbaum nach rechts"): links alle Namen (live oben, offline darunter,
/// höchstes Abzeichen zuerst), oben eine Zeile Reiter (seit 0.11.3: ganz links die Rollenwahl wie im Funkfeld, daneben die Kanäle der Rolle),
/// darunter der Stammbaum des offenen Kanals: Kanal, rechts daneben seine Ch., ganz rechts die
/// Leute. Einteilen heißt einen Namen auf einen Ch. ziehen (hört und sendet dort, ein Ch. je Kanal); im Baum auf einen anderen Ch. ziehen
/// setzt um, nach links ziehen nimmt heraus. Strg+Z nimmt jeden dieser Züge zurück. Sonst nichts: kein Stumm, keine Plätze, kein Schloss.
/// </summary>
public sealed partial class BrettViewModel
{
    /// <summary>So lange schwebt ein Name über einem zugeklappten Kanal, bis er aufklappt (wie im Explorer).</summary>
    private static readonly TimeSpan AufklappenNach = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Die Farbe der Kanäle im Stammbaum (Rahmen am Kanal, Linien zu seinen Ch.): Orange für alle (Sarevoc 13.10.2026: "mehr als drei Farben in dem
    /// Baum brauchen wir nicht; Weiß ist das Unterste, dann Orange für übergeordnete Kanäle"). Bis dahin hatte jeder Kanal eine eigene Farbe.
    /// Weiß (die Linien zu den Leuten) und Grau (leere Ch.) setzt die Ansicht.
    /// </summary>
    private static readonly Brush Kanalfarbe = Pinsel(0xE8, 0x91, 0x3A);

    /// <summary>So viele Kanäle einer Rolle stehen als Reiter da (Sarevoc: "da reichen derzeit 5").</summary>
    private const int ReiterJeRolle = 5;

    private readonly Dictionary<ushort, BaumKanal> _baumKanaele = [];
    private readonly Dictionary<ushort, BaumCh> _baumCh = [];
    private readonly Dictionary<ushort, BaumRolle> _baumRollen = [];
    private ushort? _offenerKanal;
    private AblagezielBasis? _beruehrt;
    private DateTimeOffset _beruehrtSeit;

    /// <summary>Links oben: wer live ist, höchstes Abzeichen zuerst, dann Name.</summary>
    public ObservableCollection<BrettKarte> NamenLive { get; } = [];

    /// <summary>Links unter der Linie: wer offline ist (zum Vorab-Einteilen), ebenso sortiert.</summary>
    public ObservableCollection<BrettKarte> NamenOffline { get; } = [];

    /// <summary>Die Rollenwahl, der erste Reiter ganz links: die Rollen wie im Funkfeld ("1 Standard (Gast)", "2 Flottenführung" ...).</summary>
    public ObservableCollection<BaumRolle> Rollenwahl { get; } = [];

    /// <summary>Die Rolle der Rollenwahl; sie bestimmt die Reiter daneben.</summary>
    [ObservableProperty]
    public partial BaumRolle? GewaehlteRolle { get; private set; }

    /// <summary>Die Reiter rechts neben der Rollenwahl: die ersten Kanäle der Rolle, in der Reihenfolge ihrer Felder.</summary>
    public ObservableCollection<BaumKanal> Reiter { get; } = [];

    /// <summary>Unter den Reitern: der Kanal des offenen Reiters mit seinen Ch. und Leuten.</summary>
    public ObservableCollection<BaumKanal> Baum { get; } = [];

    private static SolidColorBrush Pinsel(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    /// <summary>Admin, Führung, Einsatzleitung, dann alle anderen (Sarevocs Reihenfolge in allen Listen).</summary>
    internal static int Rang(BrettKarte k)
    {
        ArgumentNullException.ThrowIfNull(k);
        if (k.IstAdmin) return 0;
        // Nur die Zeichen (vor dem ersten "|"); seit 0.11.7 stehen dahinter die Abzeichen, an denen kein Rang hängt.
        var zeichen = (k.ZeichenStand ?? "").Split('|')[0].Split(',');
        if (zeichen.Contains(Zeichensatz.Fuehrung)) return 1;
        return zeichen.Contains(Zeichensatz.Einsatzleitung) ? 2 : 3;
    }

    private static List<BrettKarte> NachRang(List<BrettKarte> k)
    {
        k.Sort((a, b) => Rang(a).CompareTo(Rang(b)) is var r and not 0 ? r : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return k;
    }

    /// <summary>Die Leute an einem Ch.: live zuerst, dann Abzeichen, dann Name; die erste und die letzte tragen es für die Linie.</summary>
    private static List<KanalPerson> Gereiht(List<KanalPerson>? leute, Brush? farbe)
    {
        if (leute is null or { Count: 0 }) return [];
        leute.Sort((a, b) => (a.Karte.Online ? 0 : 1).CompareTo(b.Karte.Online ? 0 : 1) is var o and not 0 ? o
            : Rang(a.Karte).CompareTo(Rang(b.Karte)) is var r and not 0 ? r
            : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return [.. leute.Select((p, i) => p with { Erster = i == 0, Letzter = i == leute.Count - 1, Farbe = farbe })];
    }

    private static void Fuellen<T>(ObservableCollection<T> ziel, List<T> soll)
    {
        if (ziel.SequenceEqual(soll)) return;
        ziel.Clear();
        foreach (var x in soll) ziel.Add(x);
    }

    // ---------------------------------------------------------------- Abgleich

    /// <summary>Die Kanäle des Baums: alle Netze außer Global und den Einheitsnetzen (wie <see cref="Sondernetze"/>), als Netz-IDs.</summary>
    internal static HashSet<ushort> Baumnetze(FunkLage l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return [.. l.Netze.Where(n => !n.IstGlobal && n.SchiffId is null).Select(n => n.Id)];
    }

    /// <summary>
    /// In wie vielen Kanälen des Baums (<paramref name="baum"/>, aus <see cref="Baumnetze"/>) jemand mit diesen Feldern sitzt, Ruhe eingeschlossen; ein Ch.
    /// zählt für sein Kanalnetz. Die Zahl am Namen links und seit der Gegenprüfung 0.11.5 der Status in "Wer ist da" (grün ab 1).
    /// </summary>
    internal static int Kanalzahl(IEnumerable<FeldInfo> felder, IReadOnlySet<ushort> baum)
    {
        ArgumentNullException.ThrowIfNull(felder);
        ArgumentNullException.ThrowIfNull(baum);
        return felder.Select(x => Listennetz(x.NetzId)).Where(baum.Contains).Distinct().Count();
    }

    /// <summary>Links: live und offline getrennt, je nach Rang und Name.</summary>
    private void NamenAbgleichen(FunkLage l)
    {
        // Sarevoc 13.10.2026: "Wir müssen links die Übersicht behalten; es wäre denkbar, links beim Namen noch eine Zahl anzugeben, 1 heißt in einem
        // Kanal usw." Gezählt werden die Kanäle des Baums (ohne Global und Einheitsnetze), in denen die Person ein Feld hat, Ruhe eingeschlossen.
        var baumNetze = Baumnetze(l);
        var zahlen = new Dictionary<uint, int>();
        foreach (var f in l.Funker)
        {
            if (f.Details is { } d) zahlen[f.KontoId] = Kanalzahl(d.Felder, baumNetze);
        }
        foreach (var (id, k) in _karten) k.Kanalzahl = zahlen.GetValueOrDefault(id);
        var live = l.Funker.Where(f => f.Online).Select(f => f.KontoId).ToHashSet();
        Angleichen(NamenLive, NachRang([.. _karten.Values.Where(k => k.Online && live.Contains(k.KontoId))]));
        Angleichen(NamenOffline, NachRang([.. _karten.Values.Where(k => !(k.Online && live.Contains(k.KontoId)))]));
    }

    /// <summary>
    /// Reiter und Stammbaum aus der Lage (Sarevoc 13.10.2026 nach 0.11.2: "eine Zeile; der erste Reiter ist wie im Funkfeld, der hat das Dropdown-Menü,
    /// das die Reiter daneben bestimmt, und da reichen derzeit 5"; "das erste Register ist das Haupt-Dropdown wie am Funkfeld").
    /// Die Rollenwahl listet die Rollen wie das
    /// Funkfeld; daneben stehen die ersten fünf Kanäle der gewählten Rolle (ohne Global und Einheitsnetze); darunter der Stammbaum des offenen
    /// Kanals: Ruhe, die besetzten Ch., "weitere Ch.", die Leute. Gerechnet werden alle Kanäle, damit die Objekte über Pushes und Wechsel
    /// erhalten bleiben. Ohne Rollen stehen alle Kanäle als Reiter da. Die Reihe für Gruppen und Fraktionen kommt in einem eigenen Schritt.
    /// </summary>
    private void BaumAbgleichen(FunkLage l)
    {
        var netze = l.Netze.Where(n => !n.IstGlobal && n.SchiffId is null).ToList();
        var ziele = Sondernetze.ToDictionary(z => z.NetzId);
        var leute = new Dictionary<ushort, List<KanalPerson>>();
        foreach (var f in l.Funker)
        {
            if (f.Details is not { } d || !_karten.TryGetValue(f.KontoId, out var karte)) continue;
            HashSet<ushort>? rollenNetze = null;
            foreach (var x in d.Felder)
            {
                // Auch Ruhe: ein Platz ohne Sendefunktion, die Person ist im Kanal eingeteilt (Sarevoc 13.10.2026).
                if (!ziele.TryGetValue(Listennetz(x.NetzId), out var z)) continue;
                rollenNetze ??= RollenNetze(l, f.SchiffId, f.RolleId);
                var zusatz = x.Fest && !AusDerRolle(l, f.SchiffId, f.RolleId, rollenNetze, x);
                if (!leute.TryGetValue(x.NetzId, out var liste)) leute[x.NetzId] = liste = [];
                var kanal = l.KanalName(x.NetzId);
                liste.Add(new KanalPerson(karte, z, x.Feld, x.NetzId, kanal, x.Hoeren, x.Senden, x.Fest, zusatz)
                {
                    Text = karte.Name,
                    Beschreibung = karte.Name + " in " + kanal + (karte.Online ? "" : ", offline")
                        + ". Auf einen anderen Ch. ziehen setzt um, nach links ziehen nimmt heraus.",
                });
            }
        }

        var kanaele = new List<BaumKanal>();
        foreach (var n in netze)
        {
            if (!_baumKanaele.TryGetValue(n.Id, out var k)) _baumKanaele[n.Id] = k = new BaumKanal(n.Id) { Brett = this };
            k.Name = n.Name;
            k.IstKanalnetz = n.IstKanalnetz;
            k.Farbe = Kanalfarbe;
            var besetzt = false;
            if (n is { IstKanalnetz: true, Kanaele: { } namen })
            {
                // Sarevoc 13.10.2026: Meist sitzt nur in wenigen Ch. jemand; gezeigt werden Ruhe (immer) und die besetzten Ch., die leeren stehen
                // hinter "weitere Ch." und klappen beim Ziehen auf, wenn man kurz darauf bleibt (oder per Klick).
                var alle = new List<BaumCh>();
                for (var c = 0; c < namen.Count && c <= Kanalrechnung.ChMax; c++)
                {
                    var id = Kanalrechnung.Id(n.Id, (byte)c);
                    if (!_baumCh.TryGetValue(id, out var ch)) _baumCh[id] = ch = new BaumCh(k, id) { Brett = this };
                    ch.Name = namen[c];
                    var hier = Gereiht(leute.GetValueOrDefault(id), k.Farbe);
                    ch.Besetzt = hier.Count > 0;
                    besetzt |= ch.Besetzt;
                    Fuellen(ch.Leute, hier);
                    alle.Add(ch);
                }
                var chs = alle.Where(ch => ch.IstRuhe || ch.Besetzt || k.AlleCh).ToList();
                var versteckt = alle.Count - chs.Count;
                if (versteckt > 0 || k.AlleCh)
                {
                    k.Weitere ??= new BaumCh(k, 0) { Brett = this, IstWeitere = true };
                    k.Weitere.Name = k.AlleCh ? "leere Ch. zu" : versteckt.ToString(System.Globalization.CultureInfo.CurrentCulture) + " weitere Ch.";
                    chs.Add(k.Weitere);
                }
                for (var c = 0; c < chs.Count; c++)
                {
                    chs[c].Erster = c == 0;
                    chs[c].Letzter = c == chs.Count - 1;
                }
                Angleichen(k.Ch, chs);
                Fuellen(k.Leute, []);
            }
            else
            {
                var hier = Gereiht(leute.GetValueOrDefault(n.Id), k.Farbe);
                besetzt = hier.Count > 0;
                Fuellen(k.Leute, hier);
                Angleichen(k.Ch, []);
            }
            // Der Kanal steht allein unter den Reitern und ist deshalb immer aufgeklappt (seit 0.11.3); live folgt er jedem Push.
            k.Offen = true;
            k.Besetzt = besetzt;
            kanaele.Add(k);
        }

        var rollen = new List<BaumRolle>();
        foreach (var r in l.Rollen.OrderBy(r => r.Reihenfolge).ThenBy(r => r.Id))
        {
            if (!_baumRollen.TryGetValue(r.Id, out var b)) _baumRollen[r.Id] = b = new BaumRolle(r.Id);
            // Beschriftung wie die Rollenliste im Funkfeld: Nummer davor, wenn der Name keine trägt.
            b.Name = r.Name.Length > 0 && char.IsAsciiDigit(r.Name[0]) && r.Name.Contains(' ', StringComparison.Ordinal)
                ? r.Name : r.Reihenfolge.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + r.Name;
            b.Netze = r.NetzeAuf(null);
            rollen.Add(b);
        }
        Angleichen(Rollenwahl, rollen);
        if (GewaehlteRolle is null || !rollen.Contains(GewaehlteRolle)) GewaehlteRolle = rollen.FirstOrDefault();
        List<BaumKanal> reiter = GewaehlteRolle is { } g
            ? [.. g.Netze.Select(id => kanaele.FirstOrDefault(k => k.NetzId == id)).OfType<BaumKanal>().Take(ReiterJeRolle)]
            : kanaele;
        Angleichen(Reiter, reiter);
        if (_offenerKanal is not { } o || reiter.All(k => k.NetzId != o)) _offenerKanal = reiter.FirstOrDefault()?.NetzId;
        foreach (var k in kanaele) k.Aktiv = k.NetzId == _offenerKanal;
        Angleichen(Baum, [.. reiter.Where(k => k.Aktiv)]);
    }

    // ---------------------------------------------------------------- Reiter und Aufklappen

    /// <summary>Die Rollenwahl ganz links: rechts daneben stehen dann die Kanäle dieser Rolle.</summary>
    [RelayCommand]
    private void RolleWaehlen(BaumRolle? rolle)
    {
        if (rolle is null || ReferenceEquals(rolle, GewaehlteRolle)) return;
        GewaehlteRolle = rolle;
        if (Sichtbar) BaumAbgleichen(_lage);
    }

    /// <summary>Klick auf einen Kanal-Reiter: darunter sein Stammbaum.</summary>
    [RelayCommand]
    private void ReiterWaehlen(BaumKanal? kanal)
    {
        if (kanal is not null) KanalOeffnen(kanal);
    }

    /// <summary>Öffnet einen Kanal-Reiter (Klick, oder ein Name schwebt beim Ziehen darüber).</summary>
    internal void KanalOeffnen(BaumKanal kanal)
    {
        if (kanal.Aktiv || !Reiter.Contains(kanal)) return;
        _offenerKanal = kanal.NetzId;
        if (Sichtbar) BaumAbgleichen(_lage);
    }

    /// <summary>Ein Name schwebt über "weitere Ch.": nach <see cref="AufklappenNach"/> zeigen sich die leeren Ch.</summary>
    internal void WeitereBeruehrt(BaumCh weitere)
    {
        if (weitere.Kanal.AlleCh || !Verweilt(weitere)) return;
        weitere.Kanal.AlleCh = true;
        weitere.Kanal.DurchZiehen = true;
        if (Sichtbar) BaumAbgleichen(_lage);
    }

    /// <summary>Ob die Maus beim Ziehen schon <see cref="AufklappenNach"/> über <paramref name="ziel"/> steht (gong meldet das laufend).</summary>
    private bool Verweilt(AblagezielBasis ziel)
    {
        var jetzt = _jetzt();
        if (!ReferenceEquals(_beruehrt, ziel))
        {
            _beruehrt = ziel;
            _beruehrtSeit = jetzt;
            return false;
        }
        return jetzt - _beruehrtSeit >= AufklappenNach;
    }

    /// <summary>Klick auf "weitere Ch.": die leeren Ch. zeigen oder wieder verbergen.</summary>
    [RelayCommand]
    private void WeitereUmschalten(BaumCh? weitere)
    {
        if (weitere is not { IstWeitere: true }) return;
        weitere.Kanal.AlleCh = !weitere.Kanal.AlleCh;
        weitere.Kanal.DurchZiehen = false;
        if (Sichtbar) BaumAbgleichen(_lage);
    }

    /// <summary>Nach dem Ziehen: Was nur fürs Ziehen aufgeklappt wurde (die leeren Ch.), klappt wieder zu.</summary>
    private void NachDemZiehen()
    {
        var geaendert = false;
        foreach (var k in _baumKanaele.Values.Where(k => k.DurchZiehen))
        {
            k.DurchZiehen = false;
            k.AlleCh = false;
            geaendert = true;
        }
        if (geaendert && Sichtbar) BaumAbgleichen(_lage);
    }

    // ---------------------------------------------------------------- Bewerten und Ablegen im Baum

    /// <summary>Wie eine Ablage im Baum wirken würde: schon dort, gesperrt oder passt (mit Satz).</summary>
    private Ablagebewertung BaumBewerten(BrettKarte karte, KanalPerson? von, IAblageziel ziel)
    {
        if (BaumZiel(ziel) is not { } z) return ziel is BaumKanal k ? new(Ablagewirkung.Keine, "auf einen Ch. von " + k.Name + " ziehen") : Ablagebewertung.Nichts;
        var d = _lage.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details;
        if (d?.Felder.Any(f => f.NetzId == z.Id) == true) return new(Ablagewirkung.Keine, karte.Name + " ist schon in " + z.Ort);
        if (d?.SperreVon(z.Id) is not null) return new(Ablagewirkung.GehtNicht, karte.Name + " ist in " + z.Ort + " gesperrt");
        return new(Ablagewirkung.Passt, (von is null ? karte.Name + " nach " : karte.Name + " von " + von.Kanal + " nach ") + z.Ort);
    }

    /// <summary>Kanal-ID, Kanalnetz (bei einem Ch.) und Ortsname eines Ziels im Baum; ein Kanalnetz selbst ist kein Ziel.</summary>
    private static (ushort Id, ushort? Kanalnetz, string Ort)? BaumZiel(IAblageziel ziel) => ziel switch
    {
        BaumCh { IstWeitere: true } => null,
        BaumCh c => (c.KanalId, c.Kanal.NetzId, c.Kanal.Name + " (" + c.Name + ")"),
        BaumKanal { IstKanalnetz: false } k => (k.NetzId, null, k.Name),
        _ => null,
    };

    /// <summary>
    /// Legt einen Namen im Baum ab: hat die Person schon ein Feld in diesem Kanalnetz (auch auf Ruhe), zieht genau dieses Feld auf den neuen
    /// Ch. (ein Ch. je Kanal; von der Ruhe aus wird er Sendeziel, wenn sie sonst nirgends sendet); kommt sie aus dem Baum, zieht ihr Feld um;
    /// sonst kommt der Kanal in ein freies Feld. Immer hören und senden, fest.
    /// </summary>
    private Task<bool> BaumAblegenAsync(BrettKarte karte, KanalPerson? von, IAblageziel ziel)
    {
        if (BaumZiel(ziel) is not { } z) return Task.FromResult(false);
        var felder = _lage.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details?.Felder ?? [];
        var gleiches = z.Kanalnetz is { } n ? felder.FirstOrDefault(f => Listennetz(f.NetzId) == n && f.Feld != von?.Feld) : null;
        // Ruhe ist ein Platz ohne Sendefunktion (Sarevoc 13.10.2026); sonst immer hören und senden.
        var senden = !Kanalrechnung.IstRuhe(z.Id);
        var patches = new List<FeldPatch>();
        if (gleiches is not null)
        {
            if (von is not null) patches.Add(new FeldPatch { Feld = von.Feld, Leeren = true });
            patches.Add(new FeldPatch { Feld = gleiches.Feld, NetzId = z.Id, Hoeren = true, Senden = senden, Fest = true });
        }
        else
        {
            patches.Add(new FeldPatch { Feld = von?.Feld, NetzId = z.Id, Hoeren = true, Senden = senden, Fest = true });
        }
        return BaumAusfuehrenAsync(karte, patches, karte.Name + " -> " + z.Ort);
    }

    /// <summary>
    /// Schickt Felder einer Person (ein <c>felderPatch</c>) und merkt sich ihre Felder davor für Strg+Z; der Platz bleibt, wie er ist.
    /// </summary>
    private async Task<bool> BaumAusfuehrenAsync(BrettKarte karte, List<FeldPatch> patches, string verlauf)
    {
        var l = _lage;
        var vorher = l.Funker.FirstOrDefault(f => f.KontoId == karte.KontoId)?.Details?.Felder ?? [];
        var antwort = await AusfuehrenAsync([new Aenderung(karte, null, new Verschiebung { KontoId = karte.KontoId, FelderPatch = patches }, verlauf)],
            l.ZustandsVersion, merken: false).ConfigureAwait(true);
        if (antwort.Ok) _zuege.Push(new Zug([], antwort.Wert!.ZustandsVersion, [(karte.KontoId, vorher)]));
        return antwort.Ok;
    }

    /// <summary>
    /// Strg+Z für einen Zug im Baum: die Felder der Person wie davor, als <c>felderPatch</c> mit Feldnummern (erst leeren, was neu ist, dann
    /// setzen, was anders war).
    /// </summary>
    private async Task BaumZurueckAsync(Zug zug)
    {
        var l = _lage;
        var aenderungen = new List<Aenderung>();
        foreach (var (konto, vorher) in zug.Felder!)
        {
            if (Karte(konto) is not { } k) continue;
            var jetzt = l.Funker.FirstOrDefault(f => f.KontoId == konto)?.Details?.Felder ?? [];
            var patches = new List<FeldPatch>();
            foreach (var f in jetzt.Where(f => vorher.All(v => v.Feld != f.Feld))) patches.Add(new FeldPatch { Feld = f.Feld, Leeren = true });
            foreach (var v in vorher.Where(v => !jetzt.Contains(v)))
                patches.Add(new FeldPatch { Feld = v.Feld, NetzId = v.NetzId, Hoeren = v.Hoeren, Senden = v.Senden, Fest = v.Fest });
            if (patches.Count > 0) aenderungen.Add(new Aenderung(k, null, new Verschiebung { KontoId = konto, FelderPatch = patches }, k.Name + " zurück"));
        }
        if (aenderungen.Count == 0) return;
        var a = await AusfuehrenAsync(aenderungen, zug.Version, merken: false).ConfigureAwait(true);
        if (a.Code == Fehlercode.Veraltet) Meldung = "Nicht zurückgenommen: " + aenderungen[0].Karte.Name + " wurde danach umgestellt.";
    }
}
