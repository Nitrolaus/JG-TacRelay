// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>Ergebnis eines Antrags ohne Code (3.4): Konto und Gerät in WARTET oder ein Fehler.</summary>
public sealed record Antragsergebnis(Fehlercode Fehler, uint KontoId, uint GeraetId, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);

/// <summary>Ergebnis von "Alle ablehnen, die zu keiner Vormerkung passen": die abgelehnten Geräte.</summary>
public sealed record Ablehnungsergebnis(Fehlercode Fehler, IReadOnlyList<uint> Geraete, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);

public sealed partial class Kontenbuch
{
    /// <summary>Höchstens so viele offene Anträge; darüber nimmt der Server keine weiteren an (3.4).</summary>
    public const int MaxOffeneAntraege = 30;

    /// <summary>
    /// <c>Registrierung{geraetename, scName, discordName}</c> ohne Code (3.4, P3): legt ein Antragskonto (FUNKER, ohne
    /// Discord-ID) mit dem Gerät in WARTET an. Die Namen sind nur Angaben: Sie kommen erst mit der Freigabe in den
    /// Namensraum, ein offener Antrag besetzt also keinen Namen, und zwei Anträge "Mustermann" nebeneinander sind möglich. Nie
    /// führt ein Antrag von selbst in den Funk. Höchstens <see cref="MaxOffeneAntraege"/> offene Anträge.
    /// </summary>
    public Antragsergebnis AntragStellen(Fingerabdruck fingerabdruck, string geraetename, SchluesselArt art, string scName, string discordName)
    {
        ArgumentNullException.ThrowIfNull(geraetename);
        if (!Zeichenregeln.IstText(geraetename, 1, 40) || !Enum.IsDefined(art)) return Antragsfehler(Fehlercode.Ungueltig);
        if (Zeichenregeln.ScNameBereinigen(scName) is not { } sc || Zeichenregeln.DiscordNameBereinigen(discordName) is not { } dn)
            return Antragsfehler(Fehlercode.Ungueltig);

        var s = _stand;
        var jetzt = Jetzt;
        if (s.GesperrteFingerabdruecke.Contains(fingerabdruck)) return Antragsfehler(Fehlercode.GeraetGesperrt);
        if (s.GeraetZuFingerabdruck(fingerabdruck) is { } vorhanden)
        {
            if (!IstVerfallen(vorhanden, jetzt))
            {
                if (vorhanden.Status is GeraetStatus.Abgelehnt or GeraetStatus.Entzogen) return Antragsfehler(Fehlercode.GeraetGesperrt);
                if (vorhanden.Status != GeraetStatus.Entfernt) return Antragsfehler(Fehlercode.Ungueltig);
            }
            s = OhneGeraet(s, vorhanden);
        }
        var w = new List<Wirkung>();
        if (OffeneAntraege(s, jetzt) >= MaxOffeneAntraege)
        {
            w.Add(new Alarm("ANTRAEGE_VOLL"));
            w.Add(new Meldung("ANTRAEGE_VOLL", null, null));
            return new(Fehlercode.AntraegeVoll, 0, 0, w);
        }
        if (s.Konten.Count >= MaxKonten || s.NaechsteKontoId > int.MaxValue || s.NaechsteGeraetId == uint.MaxValue)
            return Antragsfehler(Fehlercode.Grenze);

        var kontoId = s.NaechsteKontoId;
        var konto = new Konto(kontoId, null, "", sc, Stufe.Funker, Rechte.Keine, Rechte.Keine, KontoStatus.Aktiv, Sperrgrund.Keiner, jetzt)
        {
            DiscordName = dn,
            Antrag = new Kontoantrag(sc, dn, jetzt),
        };
        var g = new Geraet(s.NaechsteGeraetId, kontoId, fingerabdruck, geraetename, art, GeraetStatus.Wartet,
            Verknuepft: jetzt, WartefristAb: jetzt, FreigegebenAm: null, Zuletzt: null);
        s = s with { Konten = s.Konten.Add(kontoId, konto), NaechsteKontoId = kontoId + 1 };
        s = MitGeraet(s, g) with { NaechsteGeraetId = g.GeraetId + 1 };
        Uebernehmen(s);

        w.Add(new GeraetStatusGeaendert(g.GeraetId, kontoId, GeraetStatusMeldung.Wartet, null, null));
        w.Add(new Meldung("ANTRAG_NEU", kontoId, g.GeraetId));
        w.Add(new BotNachricht(BotZiel.AdminKanal, null, "ANTRAG_NEU", $"Antrag von {sc} (Discord: {dn}), Gerät {g.Name} ({g.Anzeige}), wartet auf Freigabe."));
        return new(Fehlercode.Keiner, kontoId, g.GeraetId, w);
    }

    /// <summary>Zahl der offenen Anträge: Antragskonten mit einem wartenden, nicht verfallenen Gerät.</summary>
    public int OffeneAntraege() => OffeneAntraege(Stand, Jetzt);

    private static int OffeneAntraege(Kontenstand s, DateTimeOffset jetzt)
    {
        var n = 0;
        foreach (var k in s.Konten.Values)
            if (k.IstAntrag && AktiveGeraete(s, k.KontoId, jetzt) > 0) n++;
        return n;
    }

    /// <summary>
    /// SC-Namen eines Antrags, die schon einem freigegebenen Konto gehören (für <c>GeraetStatus.scNamenVergeben</c>): Der
    /// Antrag bleibt bestehen, freigegeben wird dann ohne diesen Namen.
    /// </summary>
    public IReadOnlyList<string> VergebeneNamen(Konto antragskonto)
    {
        ArgumentNullException.ThrowIfNull(antragskonto);
        if (antragskonto.Antrag is not { } a) return [];
        return Stand.NachScName.ContainsKey(Zeichenregeln.Namensschluessel(a.ScName)) ? [a.ScName] : [];
    }

    /// <summary>
    /// "Alle ablehnen, die zu keiner Vormerkung passen" (3.4, Antragsflut): lehnt jedes wartende Gerät eines Antrags ab,
    /// für das <paramref name="passtZuVormerkung"/> nein sagt, und löscht es seit 0.7.0 wie das einzelne Ablehnen samt
    /// Antragskonto; gesperrt wird dabei nichts. Braucht <c>GERAETE_FREIGEBEN</c> in einer Admin-Sitzung mit frischer
    /// Bestätigung.
    /// </summary>
    public Ablehnungsergebnis AntraegeAblehnen(Akteur akteur, Func<Konto, bool> passtZuVormerkung)
    {
        ArgumentNullException.ThrowIfNull(passtZuVormerkung);
        var s = _stand;
        var jetzt = Jetzt;
        if (!AkteurPruefen(s, akteur, out var ak)) return new(Fehlercode.KeinRecht, [], []);
        var recht = VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben);
        if (recht != Fehlercode.Keiner) return new(recht, [], []);
        if (BestaetigungPruefen(akteur) is var b and not Fehlercode.Keiner) return new(b, [], []);
        uint? durch = akteur.IstAdminWerkzeug ? null : akteur.KontoId;
        var w = new List<Wirkung>();
        var abgelehnt = new List<uint>();
        foreach (var g in s.Geraete.Values.OrderBy(g => g.GeraetId))
        {
            if (g.Status != GeraetStatus.Wartet || IstVerfallen(g, jetzt)) continue;
            if (!s.Konten.TryGetValue(g.KontoId, out var k) || !k.IstAntrag || passtZuVormerkung(k)) continue;
            s = AblehnenIntern(s, g, durch, w);
            abgelehnt.Add(g.GeraetId);
        }
        if (abgelehnt.Count > 0) Uebernehmen(s);
        return new(Fehlercode.Keiner, abgelehnt, w);
    }

    private static Antragsergebnis Antragsfehler(Fehlercode f) => new(f, 0, 0, []);
}
