// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Was die Meldezeile jedem Funker sagt (RAID-S2-ENTWURF Abschnitte 3 und 4, Spielersicht), auch wenn das Funkfeld abgelegt ist:
/// eine eigene Umstellung durch die Leitung ("Eingeteilt von Kobold: Schiff 3, Gunner") oder durch eine Phase ("Phase Entern (Kobold):
/// sendest in Trupp Alpha"), der Entzug des Senderechts (ZIEL_ENTZOGEN und C4, bisher nirgends ausgewertet), das Sendenetz nach einer
/// Taste und Melden, solange gehalten (beides nur in der Ablage, sonst zeigt es das Funkfeld selbst), und warum Melden nicht geht.
/// Seit 0.11.0 (Sarevoc, 12.10.2026: "maximal einzeilig") kurze Sätze für eine Zeile: Platz und Rolle ohne Sendekanal (den zeigen die
/// Kacheln darunter, den Platz auch der Hilfetext oben links), Änderungen ohne "du ... jetzt", der Urheber in Klammern.
/// <para>
/// Eine Umstellung erkennt die Klasse an einer neuen <c>kontoVersion</c> der eigenen Mitgliedschaften; der erste Stand nach dem
/// Verbinden, eigene Änderungen (Quelle SELBST, ROLLE, SATZ), eine selbst geschaltete Phase (seit 0.11.1) und Pushes ohne neue
/// Mitgliedschaften melden nichts. Auf dem Oberflächen-Thread (Lage und Ereignisse kommen über den Takt).
/// </para>
/// <para>
/// Seit 0.11.0 (Zuordnung, Punkt 3) sagt sie beim Login an, wer einen vorab eingeteilt hat: "Eingeteilt von Ferox: Schiff 1, Gunner"
/// (12 s; vor der einzeiligen Meldezeile auch mit Kanälen und Sendekanal). Geprüft wird nach einer Trennung erst der abgeglichene Stand
/// (<see cref="FunkLage.Abgeglichen"/>); bis dahin können Felder und Quelle noch aus der vorigen Sitzung stammen, und auch die
/// Umstellungsmeldung schweigt. Je Programmlauf kommt dieselbe Einteilung nur einmal, und eine online gesehene gar nicht. Erkannt wird
/// sie an (durch, Einheit, Rolle), ohne Platz an (durch, feste Felder und Ch.), nicht an der kontoVersion: Phasenwechsel und eigene
/// Änderungen erhöhen sie, ohne dass jemand neu eingeteilt hätte (Befund der Prüfung 0.11.0).
/// </para>
/// <para>
/// Alles hier kommt vom Server (ein Push mit neuer kontoVersion oder ein Ereignis) und erscheint jedes Mal (<c>Meldezeile.Zeigen</c> mit
/// <c>immer</c>, Befund der Prüfung 0.11.1): Ein Stand, der binnen 5 min zurückkehrt (Gunner, Pilot, wieder Gunner; Phase vor, zurück, wieder
/// vor; ein zweiter Entzug im selben Kanal), ist eine neue Nachricht, und spammen kann hier nichts. Entprellt sind nur die Hinweise, die von
/// selbst entstehen (Funkfeld, Klangmaschine).
/// </para>
/// </summary>
public sealed class Teilnehmermeldungen : IDisposable
{
    public const string UmstellungSchluessel = "umstellung";
    public const string EntzugSchluessel = "entzug";
    public const string SendenetzSchluessel = "sendenetz";
    public const string MeldenSchluessel = "melden";
    public const string AnsageSchluessel = "ansage";

    private readonly IFunkDienst _dienst;
    private readonly Meldezeile _zeile;
    private readonly IOberflaechentakt _takt;
    private Stand? _stand;
    private (byte Nr, string Phase, string Satz)? _ohneUrheber;

    /// <summary>Nach einer Trennung (nicht beim Bau mit schon aktiver Lage): Der erste abgeglichene Stand prüft die Ansage beim Login.</summary>
    private bool _neueSitzung;

    /// <summary>
    /// Schon angesagte oder online gesehene Einteilungen (<see cref="Einteilung"/>); übersteht das Trennen, nicht den Programmlauf.
    /// </summary>
    private readonly HashSet<(uint Durch, ushort? SchiffId, ushort? RolleId, string Felder)> _angesagt = [];

    /// <summary>Was zuletzt galt: Kontoversion, Platz, Felder und Sendenetz.</summary>
    private sealed record Stand(long KontoVersion, ushort? SchiffId, ushort? RolleId, IReadOnlyList<FeldInfo> Felder, string? Sendenetz);

    public Teilnehmermeldungen(IFunkDienst dienst, Meldezeile zeile, IOberflaechentakt takt)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(zeile);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _zeile = zeile;
        _takt = takt;
        _dienst.LageGeaendert += LageGeaendert;
        _dienst.Ereignis += Ereignis;
        Pruefen();
    }

    /// <summary>Das Funkfeld liegt in der Ablage: Sendenetz und Melden stehen dann in der Meldezeile.</summary>
    public bool Abgelegt
    {
        get;
        set
        {
            field = value;
            if (!value) _zeile.Loesen(MeldenSchluessel);
        }
    }

    /// <summary>Nach "Sendenetz vor" oder "zurück" per Taste: in der Ablage 2 s das neue Sendenetz (eine Taste ohne Bild: jedes Mal).</summary>
    public void SendenetzGewechselt()
    {
        if (!Abgelegt) return;
        var netz = Sendenetz(_dienst.Lage);
        _zeile.Zeigen(SendenetzSchluessel, netz is null ? "Kein Sendekanal" : "Sendekanal: " + netz, Meldeart.Hinweis, Meldezeiten.SendenetzMs, immer: true);
    }

    /// <summary>Melden läuft (Melde-Taste gehalten): in der Ablage, solange gehalten.</summary>
    public void MeldenBegonnen(string netz)
    {
        if (Abgelegt) _zeile.Halten(MeldenSchluessel, "Melden: an " + netz, Meldeart.Hinweis, senden: true);
    }

    public void MeldenBeendet() => _zeile.Loesen(MeldenSchluessel);

    /// <summary>Melden ging nicht: immer, 3 s, bei jedem Druck (die Antwort auf die Melde-Taste).</summary>
    public void MeldenGehtNicht(string text) => _zeile.Zeigen(MeldenSchluessel, text, Meldeart.Fehler, Meldezeiten.MeldenGehtNichtMs, immer: true);

    public void Dispose()
    {
        _dienst.LageGeaendert -= LageGeaendert;
        _dienst.Ereignis -= Ereignis;
    }

    private void LageGeaendert() => _takt.Einreihen(Pruefen);

    private void Ereignis(FunkEreignis e) => _takt.Einreihen(() => Melden(e));

    private void Melden(FunkEreignis e)
    {
        if (e is not SendenetzEntzogen s) return;
        var l = _dienst.Lage;
        var netze = string.Join(", ", s.Netze.Select(l.KanalName));
        // Seit 0.11.0 der Urheber in Klammern wie bei einer Phase (bis dahin " - verschoben durch ...").
        var wer = s.Durch is { } d && d != 0 && d != l.Konto?.Id ? " (" + l.HauptnameVon(d) + ")" : "";
        _zeile.Zeigen(EntzugSchluessel, "Nicht gesendet: kein Senderecht mehr in " + netze + wer, Meldeart.Fehler, Meldezeiten.EntzugMs, immer: true);
    }

    private void Pruefen()
    {
        var l = _dienst.Lage;
        if (!l.IstAktiv || l.Konto is null)
        {
            _stand = null;
            _ohneUrheber = null;
            _neueSitzung = true;
            return;
        }
        NameNachtragen(l);
        var neu = new Stand(l.KontoVersion, l.SchiffId, l.RolleId, l.Felder, Sendenetz(l));
        var alt = _stand;
        _stand = neu;
        if (_neueSitzung)
        {
            // Vor dem Abgleich nur mitführen: Felder und Quelle können noch aus der vorigen Sitzung stammen (FunkLage.cs, Abgeglichen).
            if (!l.Abgeglichen) return;
            _neueSitzung = false;
            Ansagen(l, neu);
            return;
        }
        // Jede online gesehene Einteilung durch andere gilt als gelesen, auch im Phasenzweig unten: Nach einer kurzen Trennung kommt sie
        // nicht als Ansage wieder (seit 0.11.0).
        if ((alt is null || neu.KontoVersion != alt.KontoVersion) && Einteilung(l, neu) is { } gesehen) _angesagt.Add(gesehen);
        // Der erste Stand nach dem Verbinden und Pushes ohne neue Mitgliedschaften (etwa jede Sprechanzeige) melden nichts.
        if (alt is null || neu.KontoVersion <= alt.KontoVersion) return;
        if (l.UmstellungPhase is { } nr)
        {
            var phase = l.PhaseVon(nr)?.Name ?? "Nummer " + nr.ToString(System.Globalization.CultureInfo.CurrentCulture);
            var rest = Aenderung(alt, neu, l) ?? "Kanäle umgestellt";
            if (l.LaufendePhase is { Durch: var d and not 0 } p && p.Nr == nr)
            {
                _ohneUrheber = null;
                // Seit 0.11.1 (Sarevoc: eigene Aktionen erzeugen keine Meldung): Wer die Phase selbst geschaltet hat, liest nichts; die Phasentaste
                // sagt "Phase ... läuft", das Brett zeigt es, und das Funkfeld zeigt die eigenen Kanäle.
                if (d == l.Konto.Id) return;
                _zeile.Zeigen(UmstellungSchluessel, "Phase " + phase + " (" + l.HauptnameVon(d) + "): " + rest, Meldeart.Hinweis, Meldezeiten.UmstellungMs, immer: true);
                return;
            }
            // Die Mitgliedschaften kommen vor 0x4004: Wer geschaltet hat, trägt NameNachtragen ein, sobald der Push da ist.
            _ohneUrheber = (nr, phase, rest);
            _zeile.Zeigen(UmstellungSchluessel, "Phase " + phase + ": " + rest, Meldeart.Hinweis, Meldezeiten.UmstellungMs, immer: true);
            return;
        }
        if (l.Quelle is not (MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung) || l.QuelleDurch is not { } durch || durch == l.Konto.Id) return;
        var von = l.HauptnameVon(durch);
        if (alt.SchiffId != neu.SchiffId || alt.RolleId != neu.RolleId)
        {
            // Seit 0.11.0 ohne "Du sendest in ...": eine Zeile, den Sendekanal zeigen die Kacheln (wie bei der Ansage beim Login).
            var platz = neu.RolleId is null ? "ohne Platz" : Modelle.WerIstDaZeile.Platztext(l, neu.SchiffId, neu.RolleId) ?? "neuer Platz";
            // Befund der Prüfung (0.11.0): Ansage und Umstellung beginnen jetzt beide mit "Eingeteilt von"; eine noch stehende Ansage nennt
            // den alten Platz und ginge neben der neuen Zeile nicht mehr auseinander. Der neue Platz ersetzt sie.
            _zeile.Loesen(AnsageSchluessel);
            _zeile.Zeigen(UmstellungSchluessel, "Eingeteilt von " + von + ": " + platz, Meldeart.Hinweis, Meldezeiten.UmstellungMs, immer: true);
            return;
        }
        if (!Gleich(alt.Felder, neu.Felder) || alt.Sendenetz != neu.Sendenetz)
            _zeile.Zeigen(UmstellungSchluessel, "Kanäle geändert von " + von + (Aenderung(alt, neu, l) is { } was ? ": " + was : ""), Meldeart.Hinweis,
                Meldezeiten.UmstellungMs, immer: true);
    }

    /// <summary>
    /// Die Ansage beim Login (seit 0.11.0, Zuordnung Punkt 3): Hat jemand anders einen eingeteilt (VERSCHIEBEN oder AUFSTELLUNG mit
    /// <c>durch</c>; ein Server bis 0.10.0 meldet eine Reservierung als WIEDERHERGESTELLT mit <c>durch</c>), steht 12 s, von wem und der
    /// Platz: "Eingeteilt von Ferox: Schiff 1, Gunner"; ohne Platz "Kanäle eingeteilt von Ferox". Die hörenden Kanäle und den Sendekanal
    /// nennt sie nicht mehr (Sarevoc, 12.10.2026: eine Zeile); sie stehen in den Kacheln darunter, der Platz auch im Hilfetext oben links. Ohne
    /// Platz, ohne festes Feld und ohne Ch. außer der Ruhe ist das der Standard (etwa "zurück auf Standard" an einem Server bis 0.10.0):
    /// dann nichts. Je Programmlauf einmal je <see cref="Einteilung"/>, und nicht, wenn sie online schon zu sehen war.
    /// </summary>
    private void Ansagen(FunkLage l, Stand neu)
    {
        if (Einteilung(l, neu) is not { } einteilung || !_angesagt.Add(einteilung)) return;
        var von = l.HauptnameVon(einteilung.Durch);
        var platz = neu.RolleId is null ? null : Modelle.WerIstDaZeile.Platztext(l, neu.SchiffId, neu.RolleId) ?? "neuer Platz";
        var text = platz is not null ? "Eingeteilt von " + von + ": " + platz : "Kanäle eingeteilt von " + von;
        _zeile.Zeigen(AnsageSchluessel, text, Meldeart.Hinweis, Meldezeiten.AnsageMs, immer: true);
    }

    /// <summary>
    /// Wer einen wohin eingeteilt hat: (durch, Einheit, Rolle), ohne Platz (durch, feste Felder und Felder mit Ch.); <c>null</c>, wenn
    /// niemand anders eingeteilt hat (VERSCHIEBEN, AUFSTELLUNG oder von einem Server bis 0.10.0 WIEDERHERGESTELLT, je mit <c>durch</c>)
    /// oder ohne Platz nur der Standard bleibt. Die kontoVersion zählt nicht: Eine Phase oder eine eigene Änderung ist keine neue Einteilung.
    /// </summary>
    private static (uint Durch, ushort? SchiffId, ushort? RolleId, string Felder)? Einteilung(FunkLage l, Stand s)
    {
        var eingeteilt = l.Quelle is MitgliedschaftsQuelle.Verschieben or MitgliedschaftsQuelle.Aufstellung or MitgliedschaftsQuelle.Wiederhergestellt;
        if (!eingeteilt || l.QuelleDurch is not { } durch || durch == 0 || durch == l.Konto?.Id) return null;
        if (s.RolleId is not null) return (durch, s.SchiffId, s.RolleId, "");
        var felder = string.Join(",", s.Felder.Where(f => f.Fest || l.ChVon(f.NetzId) is > Kanalrechnung.Ruhe).OrderBy(f => f.Feld)
            .Select(f => f.Feld.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + f.NetzId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return felder.Length == 0 ? null : (durch, null, null, felder);
    }

    /// <summary>
    /// Eine Umstellung durch eine Phase kam ohne Urheber an (die Mitgliedschaften waren vor 0x4004 da, gemessen am echten Server):
    /// Kommt der Push für dieselbe Phase mit Namen, steht er nachgetragen da, solange die Meldung noch steht. Ein Rückfall oder die
    /// Konsole (durch 0) bringt keinen Namen; dann bleibt der Satz, wie er ist. Stellt sich heraus, dass man selbst geschaltet hat, geht die
    /// Meldung fort (seit 0.11.1, eigene Aktionen erzeugen keine Meldung).
    /// </summary>
    private void NameNachtragen(FunkLage l)
    {
        if (_ohneUrheber is not { } offen) return;
        if (!_zeile.Zeigt(UmstellungSchluessel))
        {
            _ohneUrheber = null;
            return;
        }
        if (l.LaufendePhase is not { Durch: var d and not 0 } p || p.Nr != offen.Nr) return;
        _ohneUrheber = null;
        if (d == l.Konto?.Id)
        {
            _zeile.Loesen(UmstellungSchluessel);
            return;
        }
        _zeile.Zeigen(UmstellungSchluessel, "Phase " + offen.Phase + " (" + l.HauptnameVon(d) + "): " + offen.Satz, Meldeart.Hinweis, Meldezeiten.UmstellungMs, immer: true);
    }

    /// <summary>
    /// Was sich für einen änderte: das Sendenetz ("sendest in Crew 3", "kein Sendekanal"), sonst neue Hörnetze ("hörst Orga"), sonst
    /// <c>null</c> (nur umgestellt). Seit 0.11.0 ohne "du ... jetzt" (bis dahin "du sendest jetzt in ...", "du hörst jetzt ...").
    /// </summary>
    private static string? Aenderung(Stand alt, Stand neu, FunkLage l)
    {
        if (neu.Sendenetz != alt.Sendenetz) return neu.Sendenetz is { } s ? "sendest in " + s : "kein Sendekanal";
        var vorher = alt.Felder.Where(f => f.Hoeren).Select(f => f.NetzId).ToHashSet();
        var dazu = neu.Felder.Where(f => f.Hoeren && !vorher.Contains(f.NetzId)).Select(f => l.KanalName(f.NetzId)).ToList();
        return dazu.Count > 0 ? "hörst " + string.Join(", ", dazu) : null;
    }

    private static bool Gleich(IReadOnlyList<FeldInfo> a, IReadOnlyList<FeldInfo> b) => a.OrderBy(f => f.Feld).SequenceEqual(b.OrderBy(f => f.Feld));

    /// <summary>Das Sendenetz für Sätze ("Crew 3", "Crew 3, Global"), <c>null</c> ohne.</summary>
    private static string? Sendenetz(FunkLage l)
    {
        var netze = l.SendeFelder.Select(f => l.FeldVon(f) is { } feld ? l.KanalName(feld.NetzId) : null).OfType<string>().ToList();
        if (l.GlobalSenden && l.Global is { } g) netze.Add(g.Name);
        return netze.Count == 0 ? null : string.Join(", ", netze);
    }
}
