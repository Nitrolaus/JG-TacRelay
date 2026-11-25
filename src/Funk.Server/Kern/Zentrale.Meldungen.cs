// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Meldesystem (0.12.0, Sarevoc 11.10. und 16.10.2026, M1-M24, mit der Textmeldung): MeldungSenden (0x0026) und BereitMelden (0x0027), die Pushes Meldestand
/// (0x4005, an alle aktiven Sitzungen) und BereitStand (0x4006, an die, die senden dürfen), der Takt (Bereit-Abfrage nach 2 min zu)
/// und die Nachlieferung nach Welcome (M20). Der Stand lebt nur im Speicher (<see cref="Meldesystem"/>).
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>Der laufende Stand des Meldesystems; nur in der Arbeitsschleife.</summary>
    internal Meldesystem Melde { get; } = new();

    /// <summary>
    /// Serverzeit des Meldesystems in Unix-ms: die Uhr der Zentrale, im Betrieb die Systemuhr wie <c>PingAntwort.serverZeit</c>, an der
    /// die Clients ihren Uhrabgleich ausrichten. Tests stellen sie vor (Ende der Bereit-Abfrage nach 2 min).
    /// </summary>
    internal long MeldeJetzt() => Uhr.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>
    /// Ob das Konto gerade Meldungen senden darf (<see cref="S.Melderecht"/>, M1, M15-M18): Haken aus dem Mitgliederbuch, Stufe aus dem
    /// Kontenbuch, laufender Auftrag und "Heute als Funker" aus dem Lagezustand.
    /// </summary>
    internal bool DarfMelden(uint kontoId)
    {
        if (!Konten.Stand.Konten.TryGetValue(kontoId, out var k) || k.IstAntrag || k.Status != I.KontoStatus.Aktiv) return false;
        var jetzt = Zeit.LageMs();
        var lage = Schreiber.Aktuell.Konten.GetValueOrDefault(kontoId);
        var auftrag = lage?.Beauftragung is { } b && b.GiltZu(jetzt);
        var funker = lage?.AlsFunkerZu(jetzt) == true;
        return S.Melderecht.Darf(Mitglieder.Stand.DatenVon(kontoId).MeldungenSenden, Abbildung.Stufe(k.Stufe), auftrag, funker);
    }

    /// <summary>
    /// MeldungSenden (0x0026): Recht, dann die Regeln des Meldesystems; danach Meldestand an alle aktiven Sitzungen und bei einer
    /// Bereit-Abfrage der Bereit-Stand. Je Sender höchstens eine in 2 s prüft schon die Steuerbremse (Ratengruppe Meldung).
    /// </summary>
    private Antwort MeldungSenden(Verbindung v, S.MeldungSendenAnfrage a)
    {
        if (!DarfMelden(v.KontoId))
            return Antwort.Fehl(S.Fehlercode.KeinRecht, "Meldungen senden nur mit Ernennung durch einen Admin und als Führung, Admin oder Einsatzleitung");
        var e = Melde.Senden(v.KontoId, a, MeldeJetzt());
        if (e.Fehler is { } f) return Antwort.Fehl(f, e.Grund);
        MeldeFolgen(e);
        if (e.Push is not null)
            Log.Info("Meldung " + a.Art + (a.Motiv is { } m ? " " + m : "") + " von " + Log.Schwaerzung.Konto(v.KontoId));
        return Antwort.Leer();
    }

    /// <summary>BereitMelden (0x0027): jeder, solange die Abfrage mit dieser Nummer offen ist; ein neues Bereit geht an die Leitung.</summary>
    private Antwort BereitMelden(Verbindung v, S.BereitMeldenAnfrage b)
    {
        if (Melde.Bereit(v.KontoId, b.Nummer, MeldeJetzt()) is not { } neu)
            return Antwort.Fehl(S.Fehlercode.Unbekannt, "Keine offene Bereit-Abfrage mit dieser Nummer");
        if (neu) BereitStandVerteilen();
        return Antwort.Leer();
    }

    /// <summary>Folgen einer Meldung oder des Takts: Meldestand an alle, Bereit-Stand an die Sendeberechtigten.</summary>
    private void MeldeFolgen(Meldeergebnis e)
    {
        if (e.Push is { } push)
            foreach (var v in AktiveVerbindungen()) v.Push(S.Nachrichtentyp.Meldestand, push);
        if (e.BereitEnde is { } ende) BereitStandVerteilen(ende.Abfrage, ende.Bereite, offen: false);
        else if (e.BereitOffen) BereitStandVerteilen();
    }

    /// <summary>Den Stand der offenen Bereit-Abfrage an alle Sitzungen, die gerade senden dürfen; ohne offene Abfrage nichts.</summary>
    internal void BereitStandVerteilen()
    {
        if (Melde.Abfrage is { } a) BereitStandVerteilen(a, Melde.Bereite, offen: true);
    }

    private void BereitStandVerteilen(S.Meldungseintrag abfrage, IReadOnlySet<uint> bereite, bool offen)
    {
        var aktiv = AktiveVerbindungen().ToList();
        var stand = Meldesystem.BereitStandBauen(abfrage, bereite, [.. aktiv.Select(v => v.KontoId).Distinct()], offen);
        foreach (var v in aktiv)
            if (DarfMelden(v.KontoId)) v.Push(S.Nachrichtentyp.BereitStand, stand);
    }

    /// <summary>Takt (alle 250 ms): ein abgelaufener Countdown fällt aus dem Stand, eine Bereit-Abfrage endet nach 2 min.</summary>
    private void MeldungenTakt()
    {
        if (Melde.Takt(MeldeJetzt()) is { } e) MeldeFolgen(e);
    }

    /// <summary>
    /// Nach Welcome (M20): der laufende Stand (Zustand, Ampel, Countdown, offene Abfrage, bleibende Textmeldung), keine vergangenen Befehle
    /// und keine Textmeldung mit 8 s. Die Verbindung hält
    /// Pushes bis nach der Sync-Folge zurück. Läuft eine Bereit-Abfrage, bekommen alle Sendeberechtigten (auch die neue Sitzung, wenn
    /// sie senden darf) den Bereit-Stand mit der neuen Zahl der Online-Sitzungen.
    /// </summary>
    private void MeldungenNachWelcome(Verbindung v)
    {
        var jetzt = MeldeJetzt();
        if (Melde.HatStand(jetzt)) v.Push(S.Nachrichtentyp.Meldestand, Melde.StandBauen(S.Meldungsvorgang.Stand, null, null, jetzt));
        BereitStandVerteilen();
    }

    /// <summary>Eine Sitzung ist zu Ende: Läuft eine Bereit-Abfrage, zählt sie nicht mehr mit.</summary>
    private void MeldungenSitzungEndet() => BereitStandVerteilen();

    /// <summary>
    /// Der Haken eines Kontos hat sich geändert (Mitglied AENDERN). Läuft eine Bereit-Abfrage und darf das Konto jetzt senden, bekommt
    /// seine Sitzung den Stand sofort, sonst erst mit dem nächsten.
    /// </summary>
    internal void MelderechtGeaendert(uint kontoId)
    {
        if (Melde.Abfrage is not { } a || SitzungVonKonto(kontoId) is not { } v || !DarfMelden(kontoId)) return;
        var online = AktiveVerbindungen().Select(x => x.KontoId).Distinct().ToList();
        v.Push(S.Nachrichtentyp.BereitStand, Meldesystem.BereitStandBauen(a, Melde.Bereite, online, offen: true));
    }
}
