// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.ClientKern;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Oberfläche nach der Integration: Frage beim ersten Start "Headset oder Lautsprecher?" (Punkt 6), gesperrtes Mikrofon
/// mit "Erneut versuchen" und Hintergrundfehler der Maschine (Punkt 2), Global auf Taste, aber umschaltbar (A20, A10),
/// und schnelles Weiterschalten des Sendenetzes (A7) ohne Warten auf den Server.
/// </summary>
internal static class OberflaecheIntegrationTests
{
    [Test]
    public static void Erststart_HeadsetOderLautsprecher_EinmalGefragtUndGemerkt()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out _, out _, out var audio, out _);
        Pruefe.Wahr(h.Audio.FrageOffen, "beim ersten Start offen (Karte im Hauptfenster)");
        Pruefe.Wahr(a.Maschine.Echounterdrueckung, "bis zur Antwort bleibt die Echounterdrückung an (A14)");

        audio.HeadsetWaehlenCommand.Execute(null);
        Pruefe.Falsch(audio.FrageOffen, "beantwortet, die Karte verschwindet");
        Pruefe.Wahr(audio.Headset && !audio.Lautsprecher, "Headset gewählt");
        Pruefe.Falsch(a.Maschine.Echounterdrueckung, "Headset: Echounterdrückung aus");
        Pruefe.Falsch(audio.Echounterdrueckung, "auch in der Anzeige");

        a.Ablage.Sichern();
        var gelesen = a.Speicher.Laden();
        Pruefe.Gleich(Hoerweise.Headset, gelesen.Audio.Hoerweise, "gemerkt");
        using var zweiter = new Aufbau(einstellungen: gelesen);
        var audio2 = zweiter.Audio();
        Pruefe.Falsch(audio2.FrageOffen, "beim nächsten Start nicht wieder gefragt");
        Pruefe.Falsch(zweiter.Maschine.Echounterdrueckung, "und gleich ohne Echounterdrückung");

        audio2.Lautsprecher = true;
        Pruefe.Wahr(zweiter.Maschine.Echounterdrueckung && audio2.Echounterdrueckung, "im Bereich Audio umgestellt: Lautsprecher, an");
        audio2.Echounterdrueckung = false;
        Pruefe.Wahr(audio2.Lautsprecher && !zweiter.Maschine.Echounterdrueckung, "von Hand bleibt sie trotzdem abschaltbar");
    }

    [Test]
    public static async Task Mikrofon_Gesperrt_DeutlicheWarnungUndErneutVersuchen()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        a.Maschine.AufnahmeFehlerBeimStart = "Windows verweigert den Zugriff auf das Mikrofon.";
        await audio.StartenCommand.ExecuteAsync(null);
        Pruefe.Wahr(audio.Laeuft, "Audio läuft trotzdem (Wiedergabe)");
        Pruefe.Wahr(audio.HatGeraeteWarnung, "Warnung sichtbar");
        Pruefe.Wahr(audio.GeraeteWarnung!.Contains("verweigert", StringComparison.Ordinal) && audio.GeraeteWarnung.Contains("trotzdem zu hören", StringComparison.Ordinal),
            "Grund und dass man die Netze hört: " + audio.GeraeteWarnung);
        audio.WiederholenCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.Wiederholungen, "Erneut versuchen erreicht die Maschine");
        Pruefe.Falsch(audio.HatGeraeteWarnung, "Mikrofon wieder da: Warnung weg");

        a.Maschine.FehlerMelden(new InvalidOperationException("Ein Audiostrom ist stehen geblieben: spk-1"));
        Pruefe.Wahr(audio.LetzterFehler?.EndsWith("Ein Audiostrom ist stehen geblieben: spk-1", StringComparison.Ordinal) == true,
            "Hintergrundfehler der Maschine in der Oberfläche: " + audio.LetzterFehler);
    }

    [Test]
    public static void Global_A20_TasteVoreingestellt_AberUmschaltbar()
    {
        var lage = FunkDienstAttrappe.Beispiel();
        lage = lage with { Netze = [.. lage.Netze.Select(n => n.IstGlobal ? n with { NurTaste = false, SprachaktivierungStandard = true } : n)] };
        using var a = new Aufbau(lage);
        var f = a.Funkfeld();
        var global = f.Kacheln[^1];
        Pruefe.Falsch(global.NurTaste, "der Server legt Global nicht auf nur Taste fest");
        Pruefe.Falsch(global.Sprachaktivierung, "Voreinstellung Taste (A20), auch wenn der Server VA vorschlägt");
        Pruefe.Wahr(global.KannSprachaktivierung, "umschaltbar wie jedes Netz (A10)");
        global.Sprachaktivierung = true;
        Pruefe.Wahr(global.Sprachaktivierung, "auf VA gestellt");
        Pruefe.Gleich(Ausloesung.Sprachaktivierung, a.Ablage.Aktuell.NetzOderNull(1)!.Ausloesung, "gemerkt");
    }

    [Test]
    public static async Task Weiterschalten_Schnell_JederDruckZaehltUndVaFolgtSofort()
    {
        // Gegenprüfung: Die Kacheln (und damit A10) zogen erst mit der Antwort des Servers nach. Ein zweites Numpad +
        // vor der Antwort landete im selben Netz, und bis dahin galt die Sprachaktivierung des alten Netzes weiter.
        var takt = new WarteschlangenTakt();
        using var a = new Aufbau(takt: takt);
        var dienst = new LangsamerDienst(a.Dienst);
        using var f = new FunkfeldViewModel(dienst, a.Maschine, a.Verlauf, a.Ablage, takt, () => a.Uhr, a.Bruecke);
        string Sendenetz() => string.Join(",", f.Kacheln.Where(k => k.IstSenden).Select(k => k.Name));
        Pruefe.Gleich("Crew", Sendenetz(), "Ausgang");
        Pruefe.Wahr(a.Maschine.Sprachaktivierung, "Crew steht auf VA");

        var erster = f.WeiterschaltenAsync(1);
        Pruefe.Gleich("Brücke", Sendenetz(), "sofort umgeschaltet, ohne auf den Server zu warten");
        var zweiter = f.WeiterschaltenAsync(1);
        Pruefe.Gleich("Global", Sendenetz(), "der zweite Druck zählt");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "Global steht auf Taste: gleich ohne Sprachaktivierung (A10)");

        dienst.Antworten();
        await Task.WhenAll(erster, zweiter);
        takt.Abarbeiten();
        Pruefe.Gleich("Global", Sendenetz(), "nach der Antwort unverändert");
        Pruefe.Gleich("Senden [3] | Senden [] +Global", string.Join(" | ", a.Dienst.Aufrufe.Where(x => x.StartsWith("Senden", StringComparison.Ordinal))),
            "zwei Schritte gemeldet");
    }

    [Test]
    public static async Task Dienst_Sendeauswahl_GiltSofortInDerLage()
    {
        // Gegenprüfung, Gegenstück im FunkClientDienst: Er baute die Lage erst nach der Antwort neu. C5 hält die siebte
        // Meldung in schneller Folge bis zu 250 ms zurück; so lange zeigte die Lage die alte Auswahl.
        var identitaet = Geraeteidentitaet.Fluechtig();
        var client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = new DelegatAufbau((_, _) => throw new AufbauException(AufbauFehler.Netz, "Attrappe")),
            Identitaet = identitaet,
        });
        await using var dienst = new FunkClientDienst(() => new FunkAnbindung(client, identitaet, null, null, null));
        await dienst.VerbindenAsync();
        for (byte feld = 0; feld < 6; feld++) _ = await dienst.SendeauswahlSetzenAsync([feld], false);
        var siebte = dienst.SendeauswahlSetzenAsync([9], false);
        Pruefe.Falsch(siebte.IsCompleted, "die siebte Meldung wartet auf den Topf (C5)");
        Pruefe.Gleich("9", string.Join(",", dienst.Lage.SendeFelder), "die Lage zeigt die neue Auswahl sofort");
    }
}

/// <summary>
/// Wie der echte Dienst: Die Sendeauswahl gilt in der Lage sofort, die Antwort des Servers kommt erst mit
/// <see cref="Antworten"/>. Alles andere reicht er an die Attrappe durch.
/// </summary>
internal sealed class LangsamerDienst(FunkDienstAttrappe innen) : IFunkDienst
{
    private readonly TaskCompletionSource<string?> _antwort = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FunkLage Lage => innen.Lage;

    public event Action? LageGeaendert
    {
        add => innen.LageGeaendert += value;
        remove => innen.LageGeaendert -= value;
    }

    public void Antworten() => _antwort.TrySetResult(null);

    public event Action<FunkEreignis>? Ereignis
    {
        add => innen.Ereignis += value;
        remove => innen.Ereignis -= value;
    }

    public Task<Dienstantwort<Funk.Protokoll.Steuerung.FunkermodusAntwort>> FunkermodusAsync(bool an) => innen.FunkermodusAsync(an);

    public Task<Dienstantwort<Funk.Protokoll.Steuerung.PhaseSchaltenAntwort>> PhaseSchaltenAsync(byte nr, byte erwartet) => innen.PhaseSchaltenAsync(nr, erwartet);

    public Task<string?> FeldSetzenAsync(byte feld, ushort? netzId) => innen.FeldSetzenAsync(feld, netzId);

    public Task<string?> RolleWaehlenAsync(ushort rolleId, IReadOnlyList<Funk.Protokoll.Steuerung.ChWunsch> ch) => innen.RolleWaehlenAsync(rolleId, ch);

    public Task<string?> KanalSperreAsync(uint kontoId, ushort netzId, bool sperren) => innen.KanalSperreAsync(kontoId, netzId, sperren);

    public Task<Dienstantwort<IReadOnlyList<KanalsperreInfo>>> SperrenFuerAlleAsync() => innen.SperrenFuerAlleAsync();

    public Task<string?> MeldungSendenAsync(Funk.Protokoll.Steuerung.MeldungSendenAnfrage anfrage) => innen.MeldungSendenAsync(anfrage);

    public Task<string?> BereitMeldenAsync(uint nummer) => innen.BereitMeldenAsync(nummer);

    public long ServerJetztMs() => innen.ServerJetztMs();

    public Task<string?> SendeauswahlSetzenAsync(IReadOnlyList<byte> felder, bool mitGlobal)
    {
        _ = innen.SendeauswahlSetzenAsync(felder, mitGlobal);
        return _antwort.Task;
    }

    public Task VerbindenAsync() => innen.VerbindenAsync();

    public Task TrennenAsync() => innen.TrennenAsync();

    public Task<string?> HoerenSetzenAsync(ushort netzId, bool hoeren) => innen.HoerenSetzenAsync(netzId, hoeren);

    public Task<string?> EinsteigenAsync(ushort rolleId, ushort? schiffId) => innen.EinsteigenAsync(rolleId, schiffId);

    public Task<string?> PegelSetzenAsync(uint kontoId, float db) => innen.PegelSetzenAsync(kontoId, db);

    public Task<Dienstantwort<Funk.Protokoll.Steuerung.RollenkatalogAntwort>> RollenkatalogAsync() => innen.RollenkatalogAsync();

    public Task<string?> RollenkatalogErgaenzenAsync(IReadOnlyList<Funk.Protokoll.Steuerung.Rollenkatalogeintrag> eintraege, long version) =>
        innen.RollenkatalogErgaenzenAsync(eintraege, version);

    public Task<Dienstantwort<Funk.Protokoll.Steuerung.MitgliedskarteAntwort>> MitgliedskarteAsync(uint kontoId) => innen.MitgliedskarteAsync(kontoId);

    public Task<string?> LieblingsrollenSetzenAsync(IReadOnlyList<ushort> rollen, ushort? ersteWahl) => innen.LieblingsrollenSetzenAsync(rollen, ersteWahl);

    public Task<string?> ProfilSetzenAsync(Funk.Protokoll.Steuerung.ProfilSetzenAnfrage anfrage) => innen.ProfilSetzenAsync(anfrage);

    public Task<string?> WieLetztesMalAsync() => innen.WieLetztesMalAsync();

    public Task<Dienstantwort<Funk.Protokoll.Steuerung.VerschiebenAntwort>> VerschiebenAsync(IReadOnlyList<Funk.Protokoll.Steuerung.Verschiebung> aenderungen, long? basisVersion = null) =>
        innen.VerschiebenAsync(aenderungen, basisVersion);

    public Task<string?> StummschaltenAsync(uint kontoId, ushort netzId, ushort sekunden) => innen.StummschaltenAsync(kontoId, netzId, sekunden);

    public Task<Dienstantwort<ushort>> PlaetzeFreigebenAsync() => innen.PlaetzeFreigebenAsync();

    public Task<string?> SelbstwahlSetzenAsync(bool an, bool nachzuegler = true) => innen.SelbstwahlSetzenAsync(an, nachzuegler);

    public Task<Dienstantwort<IReadOnlyList<Funk.Protokoll.Steuerung.VerzeichnisEintrag>>> KontenVerzeichnisAsync() => innen.KontenVerzeichnisAsync();

    public Task<string?> AuftragAbgebenAsync() => innen.AuftragAbgebenAsync();

    public ValueTask DisposeAsync() => innen.DisposeAsync();
}
