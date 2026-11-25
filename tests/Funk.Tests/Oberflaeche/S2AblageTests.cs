// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>Zeichnet auf, was die Ablage am Funkfeld tut; ein Aktivieren stünde hier als "mit Fokus".</summary>
internal sealed class FunkfeldFensterAttrappe : IFunkfeldFenster
{
    public List<string> Aufrufe { get; } = [];

    public bool Sichtbar { get; private set; }

    public void ZeigenOhneFokus()
    {
        Aufrufe.Add("zeigen ohne Fokus");
        Sichtbar = true;
    }

    public void ZeigenMitFokus()
    {
        Aufrufe.Add("zeigen mit Fokus");
        Sichtbar = true;
    }

    public void Verbergen()
    {
        Aufrufe.Add("verbergen");
        Sichtbar = false;
    }

    public void Schliessen()
    {
        Aufrufe.Add("schließen");
        Sichtbar = false;
    }

    public void NeuAufbauen(bool zeigen)
    {
        Aufrufe.Add("neu bauen, zeigen " + zeigen);
        Sichtbar = zeigen;
    }
}

/// <summary>Das Symbol im Infobereich als Attrappe: Tooltips, Klicks und Menü von Hand.</summary>
internal sealed class AblagesymbolAttrappe : IAblagesymbol
{
    public List<string> Aufrufe { get; } = [];

    public string? Tooltip { get; private set; }

    public bool Angemeldet { get; private set; }

    public event Action? Linksklick;

    public event Action<string>? Gewaehlt;

    public Func<IReadOnlyList<Menuepunkt>>? Menue { get; set; }

    public void Anmelden(string tooltip)
    {
        Aufrufe.Add("anmelden");
        Angemeldet = true;
        Tooltip = tooltip;
    }

    public void TooltipSetzen(string tooltip)
    {
        Aufrufe.Add("tooltip " + tooltip);
        Tooltip = tooltip;
    }

    public void Entfernen()
    {
        Aufrufe.Add("entfernen");
        Angemeldet = false;
    }

    public void Klicken() => Linksklick?.Invoke();

    public void Waehlen(string schluessel) => Gewaehlt?.Invoke(schluessel);

    public void Dispose() => Aufrufe.Add("freigeben");
}

/// <summary>
/// Ausbaustufe S2, Teil A2 (RAID-S2-ENTWURF Abschnitt 5): das Funkfeld in der Ablage. Taste und Symbol legen ab und holen zurück, nie mit
/// Fokus; das X legt ab statt zu schließen; das Programmende schließt wirklich und entfernt das Symbol; ein Neuaufbau für eine
/// Farbstellung bleibt abgelegt; der Tooltip wechselt höchstens einmal je Sekunde und nur bei Änderung; "Heute als Funker" steht nur
/// für Admins im Menü; der erste Hinweis kommt einmal; in der Ablage rechnet der Bildtakt keine Pegel. Dazu Lage und Stil der Meldezeile.
/// </summary>
internal static class S2AblageTests
{
    private sealed class Teile : IDisposable
    {
        public Teile(FunkDienstAttrappe? dienst = null, bool beimStartZeigen = true)
        {
            A = new Aufbau(dienst: dienst ?? MitRaidabend());
            A.Ablage.Aktuell.Funkfeld.BeimStartZeigen = beimStartZeigen;
            Zeile = new Meldezeile(() => Ms);
            Funker = new FunkermodusViewModel(A.Dienst, A.Ablage, A.Takt);
            Ablage = new FunkfeldAblage(Fenster, Symbol, A.Ablage, Zeile, A.Dienst, () => Ms, Funker);
        }

        public Aufbau A { get; }

        public long Ms { get; set; } = 10_000;

        public FunkfeldFensterAttrappe Fenster { get; } = new();

        public AblagesymbolAttrappe Symbol { get; } = new();

        public Meldezeile Zeile { get; }

        public FunkermodusViewModel Funker { get; }

        public FunkfeldAblage Ablage { get; }

        public void Dispose() => A.Dispose();
    }

    [Test]
    public static void Taste_LegtAbUndHoltZurueck_NieMitFokus()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        Pruefe.Wahr(t.Symbol.Angemeldet, "Symbol im Infobereich, solange das Programm läuft");
        Pruefe.Wahr(t.Fenster.Sichtbar && !t.Ablage.Abgelegt, "beim Start gezeigt (Einstellung an)");
        t.Ablage.Umschalten();
        Pruefe.Wahr(t.Ablage.Abgelegt && !t.Fenster.Sichtbar, "abgelegt");
        t.Ablage.Umschalten();
        t.Symbol.Klicken();
        t.Symbol.Klicken();
        Pruefe.Gleich("zeigen ohne Fokus | verbergen | zeigen ohne Fokus | verbergen | zeigen ohne Fokus", string.Join(" | ", t.Fenster.Aufrufe),
            "Taste und Linksklick: zeigen nur ohne Fokus, nie Aktivieren");
        t.Ablage.Zeigen(mitFokus: true);
        Pruefe.Gleich("zeigen mit Fokus", t.Fenster.Aufrufe[^1], "aus dem Hauptfenster (Strg+F) darf er nach vorn");
    }

    [Test]
    public static void Start_OhneBeimStartZeigen_IstAbgelegt_OhneHinweisUeberDemSpiel()
    {
        using var t = new Teile(beimStartZeigen: false);
        t.Ablage.Starten();
        Pruefe.Wahr(t.Ablage.Abgelegt && t.Fenster.Aufrufe.Count == 0, "aus heißt abgelegt; das Fenster entsteht erst beim Zeigen");
        Pruefe.Falsch(t.Zeile.Sichtbar, "beim Start kein Hinweis");
        t.Ablage.Zeigen();
        t.Ablage.Ablegen();
        // Bis 0.11.1 stand beim ersten Ablegen einmal "Funkfeld abgelegt: Taste oder Symbol (^) holt es" in der Meldezeile. Seit 0.11.2 erscheint über
        // dem Spiel nur, was Meldezeile.Erlaubt nennt (Sarevoc 13.10.2026); der Hinweis zur Ablage fällt still weg, auch beim ersten Mal.
        Pruefe.Falsch(t.Zeile.Sichtbar, "erstes Ablegen: kein Hinweis über dem Spiel");
        Pruefe.Wahr(t.A.Ablage.Aktuell.Funkfeld.AblageHinweisGezeigt, "gemerkt wie bisher");
        t.Ablage.Zeigen();
        t.Ablage.Ablegen();
        Pruefe.Falsch(t.Zeile.Sichtbar, "auch danach nichts");
    }

    [Test]
    public static void X_LegtAb_ProgrammendeSchliesstWirklichUndEntferntDasSymbol()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        Pruefe.Wahr(t.Ablage.SchliessenAngefragt(), "das X wird abgefangen");
        Pruefe.Wahr(t.Ablage.Abgelegt && t.Fenster.Aufrufe[^1] == "verbergen", "und legt ab");
        t.Ablage.ProgrammEnde();
        Pruefe.Gleich("schließen", t.Fenster.Aufrufe[^1], "das Funkfeld schließt wirklich");
        Pruefe.Falsch(t.Symbol.Angemeldet, "das Symbol ist fort (NIM_DELETE)");
        Pruefe.Falsch(t.Ablage.SchliessenAngefragt(), "am Programmende wird nichts mehr abgefangen");
    }

    [Test]
    public static void Neuaufbau_FuerEineFarbstellung_BleibtAbgelegt()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        t.Ablage.NeuAufbauen();
        Pruefe.Gleich("neu bauen, zeigen True", t.Fenster.Aufrufe[^1], "sichtbar bleibt sichtbar");
        t.Ablage.Ablegen();
        t.Ablage.NeuAufbauen();
        Pruefe.Gleich("neu bauen, zeigen False", t.Fenster.Aufrufe[^1], "abgelegt bleibt abgelegt");
        Pruefe.Wahr(t.Ablage.Abgelegt, "abgelegt");
    }

    [Test]
    public static void Tooltip_NurBeiWechsel_HoechstensEinmalJeSekunde()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        Pruefe.Gleich("JG TacRelay - Funkfeld sichtbar - Senden: Orga", t.Symbol.Tooltip, "Tooltip beim Anmelden");
        var vorher = t.Symbol.Aufrufe.Count;
        t.Ablage.Takt();
        Pruefe.Gleich(vorher, t.Symbol.Aufrufe.Count, "ohne Wechsel nichts");
        t.Ablage.Ablegen();
        t.Ms += 200;
        t.Ablage.Takt();
        Pruefe.Gleich(vorher, t.Symbol.Aufrufe.Count, "Wechsel, aber weniger als 1 s seit dem letzten: noch nicht");
        t.Ms += 900;
        t.Ablage.Takt();
        Pruefe.Gleich("JG TacRelay - Funkfeld abgelegt - Senden: Orga", t.Symbol.Tooltip, "nach 1 s neu");
        t.A.Dienst.LageSetzen(l => l with { SendeFelder = [] });
        t.Ms += 100;
        t.Ablage.Takt();
        t.Ms += 1000;
        t.Ablage.Takt();
        Pruefe.Gleich("JG TacRelay - Funkfeld abgelegt - kein Sendekanal", t.Symbol.Tooltip, "Sendenetz im Tooltip");
        t.A.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        t.Ms += 1000;
        t.Ablage.Takt();
        Pruefe.Gleich("JG TacRelay - Funkfeld abgelegt - nicht verbunden", t.Symbol.Tooltip, "getrennt");
    }

    [Test]
    public static async Task Menue_HeuteAlsFunkerNurFuerAdmins_UndDieEintraegeWirken()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        var beendet = 0;
        var haupt = 0;
        t.Ablage.Beenden = () => beendet++;
        t.Ablage.HauptfensterZeigen = () => haupt++;
        // Seit 0.10.0: "Mikro stumm" an zweiter Stelle, wie die Taste (verdrahtet wie in App.OnStartup).
        using var ff = t.A.Funkfeld();
        ff.Meldezeile = t.Zeile;
        Pruefe.Gleich("ablegen:Funkfeld ablegen|haupt:Hauptfenster|beenden:Beenden", string.Join("|", t.Ablage.Menue().Select(m => m.Schluessel + ":" + m.Text)),
            "ohne den Weg zum Mikro kein Eintrag dafür");
        t.Ablage.MikroStumm = () => ff.Mikro.Stumm;
        t.Ablage.MikroUmschalten = ff.Mikro.TasteGedrueckt;
        Pruefe.Gleich("ablegen:Funkfeld ablegen|mikro:Mikro stumm|haupt:Hauptfenster|beenden:Beenden", string.Join("|", t.Ablage.Menue().Select(m => m.Schluessel + ":" + m.Text)),
            "Mustermann ist kein Admin: kein Funker-Eintrag; Mikro stumm an zweiter Stelle");
        Pruefe.Wahr(t.Symbol.Menue is not null, "das Symbol fragt das Menü beim Rechtsklick ab");
        t.Symbol.Waehlen("mikro");
        // Seit 0.11.2 ohne Satz über dem Spiel (Meldezeile.Erlaubt; bis 0.11.1 stand dort MikroViewModel.StummMeldung).
        Pruefe.Wahr(t.A.Maschine.MikroStumm && !t.Zeile.Sichtbar, "Mikro stumm wirkt wie die Taste, ohne Satz in der Meldezeile: " + t.Zeile.Text);
        Pruefe.Gleich("mikro:Mikro wieder an", t.Ablage.Menue()[1].Schluessel + ":" + t.Ablage.Menue()[1].Text, "stumm: Mikro wieder an");
        t.Symbol.Waehlen("mikro");
        Pruefe.Falsch(t.A.Maschine.MikroStumm, "wieder an");
        t.Symbol.Waehlen("ablegen");
        t.Symbol.Waehlen("haupt");
        t.Symbol.Waehlen("beenden");
        Pruefe.Wahr(t.Ablage.Abgelegt && haupt == 1 && beendet == 1, "ablegen, Hauptfenster, Beenden");
        Pruefe.Gleich("zeigen:Funkfeld zeigen", t.Ablage.Menue()[0].Schluessel + ":" + t.Ablage.Menue()[0].Text, "abgelegt: zeigen");

        t.A.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(Nordwind, "Nordwind", "nordwind", Stufe.Admin, [Recht.Zuweisen]) });
        Pruefe.Wahr(t.Ablage.Menue().Any(m => m is { Schluessel: "funker", Text: "Heute als Funker" }), "Admin: Heute als Funker");
        var funkerZeigen = 0;
        t.Ablage.FunkermodusZeigen = () => funkerZeigen++;
        t.Symbol.Waehlen("funker");
        await Task.Yield();
        Pruefe.Wahr(funkerZeigen == 1 && t.Funker.HatRueckfrage && !t.A.Dienst.Aufrufe.Contains("Funkermodus an"),
            "erst die Rückfrage mit den Folgen, im Hauptfenster unter Ich");
        await t.Funker.JaCommand.ExecuteAsync(null);
        Pruefe.Wahr(t.A.Dienst.Aufrufe.Contains("Funkermodus an"), "eingeschaltet nach Ja");
        Pruefe.Wahr(t.Ablage.Menue().Any(m => m is { Schluessel: "admin", Text: "Wieder Admin" }), "danach: Wieder Admin");
        t.Symbol.Waehlen("admin");
        await Task.Yield();
        Pruefe.Wahr(t.A.Dienst.Aufrufe.Contains("Funkermodus aus"), "wieder Admin über das Menü");
    }

    [Test]
    public static void Funkfeld_InDerAblage_RechnetKeinePegel()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var kachel = f.Kacheln.First(k => k.SprecherAktiv);
        f.Abgelegt = true;
        a.Uhr += 1000;
        f.Takt();
        Pruefe.Gleich(0.0, kachel.PegelAnteil, "abgelegt: kein Pegel, obwohl dort jemand spricht");
        f.Abgelegt = false;
        a.Uhr += 1000;
        f.Takt();
        Pruefe.Gleich(0.5, kachel.PegelAnteil, "wieder sichtbar: der Takt rechnet");
    }

    [Test]
    public static void Meldezeile_StilUndLage()
    {
        var stil = Meldezeilenlage.Stil(Meldezeilenlage.WsExAppwindow);
        Pruefe.Wahr((stil & Meldezeilenlage.WsExNoactivate) != 0, "nie aktiviert");
        Pruefe.Wahr((stil & Meldezeilenlage.WsExTransparent) != 0, "durchklickbar");
        Pruefe.Wahr((stil & Meldezeilenlage.WsExToolwindow) != 0 && (stil & Meldezeilenlage.WsExAppwindow) == 0, "nicht in Alt+Tab und Taskleiste");
        Pruefe.Wahr((stil & Meldezeilenlage.WsExLayered) != 0, "geschichtet");

        // Seit 0.11.1 an einem festen Platz des Monitors (bis 0.11.0 über dem Funkfeld, darunter ohne Platz oben); die neun Stellen und die Wahl des
        // Monitors prüft OberflaecheMeldezeilePlatzTests, hier nur die Voreinstellung: oben Mitte, 8 Einheiten vom Rand, im ganzen Monitorrechteck.
        var monitor = new Bildschirm(new Pixelrechteck(0, 0, 2560, 1440), new Pixelrechteck(0, 0, 2560, 1400), 1, true);
        Pruefe.Gleich((1030, 8), Meldezeilenlage.Fest(FunkfeldEinstellungen.MeldezeileStelleAbWerk, monitor, 500, 40), "ab Werk oben Mitte");
        Pruefe.Gleich((2052, 1392), Meldezeilenlage.Fest(Meldezeilenstelle.UntenRechts, monitor, 500, 40), "unten rechts: bis an den Rand des Monitors, nicht des Arbeitsbereichs");
    }
}
