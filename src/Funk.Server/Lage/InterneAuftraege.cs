// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

/// <summary>
/// Serverinterne Aufträge an den Zustandsschreiber, die die Anbindung braucht (Erweiterung des Moduls, im Bericht
/// genannt). Konten gehören dem Kontenbuch (Identität, 3.3); der Schnappschuss führt eine Spiegelung mit Stufe, Rechten,
/// Status und Namen, damit Senderecht und Rangregeln (6.3, 6.10) je Paket am Schnappschuss entschieden werden. Das
/// Admin-Werkzeug hat kein Konto; für die Verwaltung von Netzen, Schiffen und Rollen tritt es für die Dauer eines
/// Auftrags als flüchtiges ADMIN-Konto außerhalb des Kontenbereichs auf.
/// </summary>
public static class InterneAuftraege
{
    /// <summary>Kennung des Admin-Werkzeugs während eines Verwaltungsauftrags (außerhalb 1 bis 2^31-1, nie gespeichert).</summary>
    public const uint AdminWerkzeug = 0x8000_0000;

    /// <summary>
    /// Übernimmt Stufe, Rechte, Status und Namen eines Kontos aus dem Kontenbuch. Ein neues Konto bekommt STANDARD (6.9)
    /// und zieht offene Aufstellungseinträge nach. Wird das Konto gesperrt, endet seine Sitzung (3.6).
    /// </summary>
    public static Auftrag KontoUebernehmen(uint kontoId, string name, string? discordId, Stufe stufe, Recht plus, Recht minus, KontoStatus status)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new Auftrag(nameof(KontoUebernehmen), true, w => Uebernehmen(w, kontoId, name, discordId, stufe, plus, minus, status));
    }

    /// <summary>
    /// Konto ist im Kontenbuch gelöscht (3.6): Sitzung endet, Plätze, Felder und ein Auftrag fallen mit dem Konto weg, und was
    /// es als Admin erteilt hat, endet (6.12 Regel 6).
    /// </summary>
    public static Auftrag KontoEntfernen(uint kontoId) =>
        new(nameof(KontoEntfernen), true, w =>
        {
            if (w.Konto(kontoId) is not { } alt) return new Ergebnis();
            var beendet = SitzungBeenden(w, kontoId, Sitzungsende.KontoGeloescht);
            w.KontoEntfernen(kontoId);
            var folgen = new Zustandsschreiber.Folgen();
            Zustandsschreiber.AuftraegeNachKontoaenderung(w, alt, null, folgen);
            return folgen.Ergebnis(new Ergebnis { BeendeteSitzungen = beendet });
        });

    /// <summary>
    /// Verwaltung durch das Admin-Werkzeug (3.8): führt einen Verwaltungsauftrag mit allen Rechten und der aktuellen
    /// NetzListe-Version aus. Das flüchtige Konto steht nur während des Auftrags im Arbeitsstand.
    /// </summary>
    public static Auftrag Verwaltung(string art, Func<VerwaltungsZiel, Ergebnis> ausfuehren)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(ausfuehren);
        return new Auftrag(art, true, w =>
        {
            w.KontoSetzen(new Konto { Id = AdminWerkzeug, Name = "Admin-Werkzeug", Stufe = Stufe.Admin, Status = KontoStatus.Aktiv }, relevant: false);
            try
            {
                return ausfuehren(new VerwaltungsZiel(w));
            }
            finally
            {
                w.KontoEntfernen(AdminWerkzeug);
            }
        });
    }

    private static Ergebnis Uebernehmen(Arbeitsstand w, uint kontoId, string name, string? discordId, Stufe stufe, Recht plus, Recht minus, KontoStatus status)
    {
        if (w.Konto(kontoId) is not { } alt)
        {
            var neu = Zustandsschreiber.KontoAnlegenAus(w, new Konto
            {
                Id = kontoId,
                Name = name,
                DiscordId = discordId,
                Stufe = stufe,
                Plus = plus,
                Minus = minus,
                Status = status,
            });
            // Den Namen hat das Kontenbuch schon eindeutig gemacht (NFKC, 3.3); er gilt wörtlich.
            if (neu.Erfolg && w.Konto(kontoId) is { } angelegt && !string.Equals(angelegt.Name, name, StringComparison.Ordinal))
                w.KontoSetzen(angelegt with { Name = name }, relevant: false);
            return neu;
        }

        var geaendert = alt with { Name = name, DiscordId = discordId, Stufe = stufe, Plus = plus, Minus = minus, Status = status };
        if (geaendert == alt) return new Ergebnis();
        var rechteAnders = alt.Stufe != stufe || alt.Plus != plus || alt.Minus != minus;
        if (rechteAnders) geaendert = geaendert with { Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, geaendert.Sendeauswahl, geaendert.Felder, w.Wirksam(geaendert)) };
        w.KontoSetzen(geaendert, relevant: rechteAnders || alt.Status != status);
        var beendet = status == KontoStatus.Gesperrt && alt.Status != KontoStatus.Gesperrt
            ? SitzungBeenden(w, kontoId, Sitzungsende.KontoGesperrt)
            : [];
        // Sperre und Stufenwechsel beenden einen Auftrag; verliert der Erteilende KONTEN_VERWALTEN, enden seine (6.12 Regel 6).
        var folgen = new Zustandsschreiber.Folgen();
        if (rechteAnders || alt.Status != status) Zustandsschreiber.AuftraegeNachKontoaenderung(w, alt, w.Konto(kontoId), folgen);
        return folgen.Ergebnis(new Ergebnis { BetroffeneKonten = [kontoId], BeendeteSitzungen = beendet });
    }

    private static System.Collections.Immutable.ImmutableArray<BeendeteSitzung> SitzungBeenden(Arbeitsstand w, uint kontoId, Sitzungsende grund)
    {
        if (!w.SitzungJeKonto.TryGetValue(kontoId, out var sitzung)) return [];
        w.SitzungEntfernen(sitzung);
        if (w.Konto(kontoId) is { } k) w.KontoSetzen(k with { SitzungsEnde = w.Jetzt }, relevant: false);
        return [new BeendeteSitzung(sitzung, kontoId, grund)];
    }
}

/// <summary>Verwaltungsaufträge des Admin-Werkzeugs, gebunden an den Arbeitsstand eines Auftrags.</summary>
public sealed class VerwaltungsZiel
{
    private readonly Arbeitsstand _w;

    internal VerwaltungsZiel(Arbeitsstand w) => _w = w;

    public Ergebnis NetzSetzen(Netz netz) => Zustandsschreiber.NetzSetzenAus(_w, InterneAuftraege.AdminWerkzeug, netz, _w.NetzListeVersion);

    public Ergebnis NetzLoeschen(ushort netzId) => Zustandsschreiber.NetzLoeschenAus(_w, InterneAuftraege.AdminWerkzeug, netzId, _w.NetzListeVersion);

    public Ergebnis SchiffSetzen(Schiff schiff) => Zustandsschreiber.SchiffSetzenAus(_w, InterneAuftraege.AdminWerkzeug, schiff, _w.NetzListeVersion);

    public Ergebnis SchiffLoeschen(ushort schiffId) => Zustandsschreiber.SchiffLoeschenAus(_w, InterneAuftraege.AdminWerkzeug, schiffId, _w.NetzListeVersion);

    public Ergebnis RolleSetzen(Rolle rolle) => Zustandsschreiber.RolleSetzenAus(_w, InterneAuftraege.AdminWerkzeug, rolle, _w.NetzListeVersion);

    public Ergebnis RolleLoeschen(ushort rolleId) => Zustandsschreiber.RolleLoeschenAus(_w, InterneAuftraege.AdminWerkzeug, rolleId, _w.NetzListeVersion);

    public Ergebnis Einstellung(bool? selbstwahl, bool? durchsageNurTaste, bool? nachzuegler = null) =>
        Zustandsschreiber.EinstellungAus(_w, InterneAuftraege.AdminWerkzeug, _w.NetzListeVersion, selbstwahl, durchsageNurTaste, nachzuegler);

    /// <summary>Verwaltungspaket an der Konsole (N5), etwa eine Flottenvorlage.</summary>
    public Ergebnis Paket(IReadOnlyList<Verwaltungsschritt> schritte) =>
        Zustandsschreiber.PaketAus(_w, InterneAuftraege.AdminWerkzeug, schritte, _w.NetzListeVersion);

    /// <summary>Auftrag an der Konsole erteilen (<c>funk-admin auftrag</c>); Urheber im Journal ist die Konsole.</summary>
    public Ergebnis BeauftragungErteilen(uint kontoId, long? von, long bis, Recht? rechte, string? grund) =>
        Zustandsschreiber.BeauftragungErteilenAus(_w, InterneAuftraege.AdminWerkzeug, kontoId, von, bis, rechte, grund);

    public Ergebnis BeauftragungEntziehen(uint kontoId) =>
        Zustandsschreiber.BeauftragungEntziehenAus(_w, InterneAuftraege.AdminWerkzeug, kontoId);

    public Ergebnis VerdraengungFreigeben(uint kontoId) => Zustandsschreiber.VerdraengungFreigebenAus(_w, InterneAuftraege.AdminWerkzeug, kontoId);

    /// <summary>
    /// Kanalsperre an der Konsole (<c>funk-admin kanalsperre-setzen</c>, 6.17): für ein Konto oder mit <paramref name="kontoId"/> 0 für alle ohne
    /// Einsatzleitung, bis <paramref name="bis"/> (Lage-ms, höchstens 24 h). Urheber im Zustand und im Journal ist die Konsole (0).
    /// </summary>
    public Ergebnis KanalSperreSetzen(uint kontoId, ushort netzId, long bis) =>
        Zustandsschreiber.KanalSperreSetzenAus(_w, InterneAuftraege.AdminWerkzeug, kontoId, netzId, bis);

    /// <summary>Kanalsperre an der Konsole aufheben, auch eine, die eine Einsatzleitung gesetzt hat (<c>funk-admin kanalsperre-aufheben</c>).</summary>
    public Ergebnis KanalSperreAufheben(uint kontoId, ushort netzId) =>
        Zustandsschreiber.KanalSperreAufhebenAus(_w, InterneAuftraege.AdminWerkzeug, kontoId, netzId);
}
