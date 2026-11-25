// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

// Bodies 0x0050-0x0080, 0x4001, 0x4002 aus 4.5.

/// <summary>0x0050 DurchsageStart (S->C, nur Anzeige).</summary>
public sealed record DurchsageStart : ISteuerBody
{
    public required uint Sprecher { get; init; }
    public required DurchsageArt Art { get; init; }
    public required IReadOnlyList<ushort> Netze { get; init; }
    public required ushort Sprechphase { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Netze, Grenzen.ZieleDurchsageMax, "netze");
}

/// <summary>0x0051 DurchsageEnde (S->C).</summary>
public sealed record DurchsageEnde : ISteuerBody
{
    public required uint Sprecher { get; init; }
    public required ushort Sprechphase { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary><c>{an: bool}</c>: SprechanzeigeAbo und die Antwort auf TunnelModus.</summary>
public sealed record AnAus : ISteuerBody
{
    public required bool An { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>Eintrag in <c>Sprechanzeige.aktiv</c>.</summary>
public sealed record SprechanzeigeEintrag
{
    public required uint KontoId { get; init; }
    public required SprechArt Art { get; init; }
    public required IReadOnlyList<ushort> Netze { get; init; }
}

/// <summary>0x0053 Sprechanzeige (S->C an Abonnenten).</summary>
public sealed record Sprechanzeige : ISteuerBody
{
    public required IReadOnlyList<SprechanzeigeEintrag> Aktiv { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Aktiv, Grenzen.SitzungenMax, "aktiv");
        if (p.Ok) foreach (var e in Aktiv) p.Liste(e.Netze, Grenzen.ZieleDurchsageMax, "aktiv.netze");
    }
}

/// <summary>Pegel für ein Konto in einem Netz (0 = alle Netze).</summary>
public sealed record PegelEintrag : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required ushort NetzId { get; init; }
    public required double Db { get; init; }

    public void Pruefen(ref Pruefung p) => p.Regel(PegelGilt(Db), "eintraege.db");

    /// <summary>-60.0 bis +12.0 in Schritten von 0.5.</summary>
    internal static bool PegelGilt(double db) => db is >= -60.0 and <= 12.0 && Math.Abs(db * 2 - Math.Round(db * 2)) == 0;
}

/// <summary>0x0060 PegelVoreinstellungen: Push beim Start und Antwort auf die Anfrage <c>{}</c>.</summary>
public sealed record PegelVoreinstellungen : ISteuerBody
{
    public required IReadOnlyList<PegelEintrag> Eintraege { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Eintraege, Grenzen.PegelMax, "eintraege");
}

/// <summary>0x0061 PegelSetzen. Fehlendes <c>db</c> löscht. Antwort <c>{}</c>.</summary>
public sealed record PegelSetzenAnfrage : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required ushort NetzId { get; init; }
    public double? Db { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        if (Db is { } d) p.Regel(PegelEintrag.PegelGilt(d), "db");
    }
}

/// <summary>0x0062 Ablage, Anfrage. <c>daten</c> ist undurchsichtig, höchstens 16 KiB.</summary>
public sealed record AblageAnfrage : ISteuerBody
{
    public required AblageOperation Op { get; init; }
    public required AblageBereich Bereich { get; init; }
    public JsonElement? Daten { get; init; }
    public uint? Version { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.FreiesJson(Daten, Grenzen.AblageMax, "daten");
        p.IstObjekt(Daten, "daten");
        if (Op == AblageOperation.Schreiben) p.Pflicht(Daten is not null, "daten");
    }
}

/// <summary>0x0062 Ablage, Antwort.</summary>
public sealed record AblageAntwort : ISteuerBody
{
    public JsonElement? Daten { get; init; }
    public required uint Version { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.FreiesJson(Daten, Grenzen.AblageMax, "daten");
        p.IstObjekt(Daten, "daten");
    }
}

/// <summary>0x0071 KeyRotation. Antwort <c>{}</c>.</summary>
public sealed record KeyRotationAnfrage : ISteuerBody
{
    public required byte Epoche { get; init; }
    public required RotationsGrund Grund { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0072 UdpStatus (S->C).</summary>
public sealed record UdpStatus : ISteuerBody
{
    public required UdpZustand Zustand { get; init; }
    public string? Adresse { get; init; }
    public bool? GleicheIp { get; init; }

    public void Pruefen(ref Pruefung p) => p.Text(Adresse, 47, "adresse");

    /// <summary>Die Adresse (IP:Port) erscheint nie in <c>ToString</c> und damit in keinem Log (P-46).</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Zustand = {Zustand}, Adresse = {(Adresse is null ? "" : "***")}, GleicheIp = {GleicheIp}");
        return true;
    }
}

/// <summary>0x0073 TunnelModus, Anfrage. Antwort <see cref="AnAus"/>.</summary>
public sealed record TunnelModusAnfrage : ISteuerBody
{
    public required bool An { get; init; }
    public required TunnelGrund Grund { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary><c>EmpfangsBericht.runter</c>.</summary>
public sealed record RunterZaehler
{
    public required uint Empfangen { get; init; }
    public required uint Verloren { get; init; }
    public required uint Doppelt { get; init; }
    public required uint Umsortiert { get; init; }
}

/// <summary><c>EmpfangsBericht.rttMs</c>.</summary>
public sealed record RttWerte
{
    public required ushort P50 { get; init; }
    public required ushort P95 { get; init; }
}

/// <summary>0x0074 EmpfangsBericht (C->S Push, alle 5 s).</summary>
public sealed record EmpfangsBericht : ISteuerBody
{
    public required ushort ZeitraumMs { get; init; }
    public required Verbindungsweg Weg { get; init; }
    public required RunterZaehler Runter { get; init; }
    public RttWerte? RttMs { get; init; }
    public required double JitterMs { get; init; }
    public required double ZielVerzoegerungMs { get; init; }
    public required uint SpaetVerworfen { get; init; }
    public required uint Unterlaeufe { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Regel(JitterMs >= 0, "jitterMs");
        p.Regel(ZielVerzoegerungMs >= 0, "zielVerzoegerungMs");
    }
}

/// <summary>0x0075 VerlustHinweis (S->C an Sender).</summary>
public sealed record VerlustHinweis : ISteuerBody
{
    public required byte Prozent { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Prozent, 0, 100, "prozent");
}

/// <summary>
/// 0x0080 Verwaltung. <c>objekt</c> hat die Form aus NetzListe (bei EINSTELLUNG <c>{durchsageNurTaste}</c>, bei
/// SELBSTWAHL <c>{an}</c>); lesen mit <see cref="ObjektLesen{T}"/>. Die Antwort nennt 4.5 nicht; hier <c>{}</c>.
/// </summary>
public sealed record VerwaltungAnfrage : ISteuerBody
{
    public required VerwaltungsOperation Op { get; init; }
    public required JsonElement Objekt { get; init; }
    public required long Version { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.IstObjekt(Objekt, "objekt");
        p.Bereich(Version, 0, JsonVorpruefung.MaxGanzzahl, "version");
    }

    /// <summary>
    /// Liest <c>objekt</c> als <typeparamref name="T"/> (etwa <see cref="Netz"/>, <see cref="Schiff"/>,
    /// <see cref="Rolle"/>, <see cref="IdObjekt"/>, <see cref="AnAus"/>, <see cref="EinstellungObjekt"/>) und prüft es.
    /// </summary>
    public DekodierErgebnis ObjektLesen<T>() where T : class, ISteuerBody =>
        SteuerJson.Lesen(System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(Objekt), SteuerJson.Info<T>());
}

/// <summary><c>{id}</c> für die LOESCHEN-Operationen von <c>Verwaltung</c> (Form in 4.5 nicht ausgeschrieben).</summary>
public sealed record IdObjekt : ISteuerBody
{
    public required ushort Id { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Id, 1, Grenzen.NetzIdMax, "objekt.id");
}

/// <summary>0x4001 ServerLast (optional).</summary>
public sealed record ServerLast : ISteuerBody
{
    public required byte Stufe { get; init; }
    public required uint Verworfen { get; init; }
    public required double AusgangMbit { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Stufe, 0, 2, "stufe");
}

/// <summary>0x4002 Meldung (optional). <c>art</c> bleibt Text, damit neue Arten alte Clients nicht stören.</summary>
public sealed record Meldung : ISteuerBody
{
    public required string Art { get; init; }
    public uint? KontoId { get; init; }
    public uint? GeraetId { get; init; }
    public ushort? NetzId { get; init; }
    public JsonElement? Details { get; init; }

    public void Pruefen(ref Pruefung p) => p.IstObjekt(Details, "details");
}

/// <summary>Bekannte Arten von <see cref="Meldung"/> (4.5).</summary>
public static class MeldungsArten
{
    public const string Rate = "RATE";
    public const string VerdraengungHaeufig = "VERDRAENGUNG_HAEUFIG";
    public const string GeraetAntrag = "GERAET_ANTRAG";
    public const string RechteGeaendert = "RECHTE_GEAENDERT";
    public const string Abgleich = "ABGLEICH";
    public const string Kopierverdacht = "KOPIERVERDACHT";
    public const string AufstellungNachgezogen = "AUFSTELLUNG_NACHGEZOGEN";
    public const string EmpfangGedrosselt = "EMPFANG_GEDROSSELT";
    public const string EigenerVerlust = "EIGENER_VERLUST";

    /// <summary>Admin-Sitzung: <c>details.op</c> BEGONNEN (Warnung bei neuem Gerät, an jedes ADMIN-Konto) oder BEENDET (an die Sitzung).</summary>
    public const string AdminSitzung = "ADMIN_SITZUNG";
    /// <summary>Zweitfaktor wegen Fehlversuchen pausiert oder gesperrt (Warnung an jedes ADMIN-Konto).</summary>
    public const string ZweitfaktorGesperrt = "ZWEITFAKTOR_GESPERRT";
    /// <summary>Ein Notfallcode wurde benutzt (Warnung an jedes ADMIN-Konto).</summary>
    public const string Notfallcode = "NOTFALLCODE";
    /// <summary>Admin vorgesehen, beantragt, ernannt, eingerichtet, gesperrt, entzogen, Eigentümer gesetzt (Warnung).</summary>
    public const string AdminGeaendert = "ADMIN_GEAENDERT";
    /// <summary>Neuer Antrag ohne Code, an Admin-Sitzungen mit <c>GERAETE_FREIGEBEN</c>.</summary>
    public const string AntragNeu = "ANTRAG_NEU";
    /// <summary>30 offene Anträge erreicht (Warnung an jedes ADMIN-Konto).</summary>
    public const string AntraegeVoll = "ANTRAEGE_VOLL";
    /// <summary>Mitglieder, Rollenkatalog oder Abzeichen geändert, an Admin-Sitzungen mit <c>KONTEN_VERWALTEN</c>.</summary>
    public const string MitgliedGeaendert = "MITGLIED_GEAENDERT";

    /// <summary>
    /// Auftrag für die Einsatzleitung (F7, 6.12), an den Beauftragten und an Admin-Sitzungen mit <c>KONTEN_VERWALTEN</c>:
    /// <c>details.op</c> ERTEILT, BEGONNEN, ENTZOGEN, ABGEGEBEN, BEENDET oder ABGELAUFEN, dazu <c>von</c>, <c>bis</c>, <c>durch</c>,
    /// <c>grund?</c> (der Anlass) und bei BEENDET <c>warum</c> (GESPERRT, STUFE, GELOESCHT, ERTEILER).
    /// </summary>
    public const string Beauftragung = "BEAUFTRAGUNG";

    /// <summary>
    /// Kanalsperre gesetzt oder beendet (6.17, seit 0.6.0), an alle Sitzungen mit <c>ZUWEISEN</c>: <c>details.op</c> GESETZT, AUFGEHOBEN,
    /// ABGELAUFEN oder BEENDET (der Setzende hat kein <c>ZUWEISEN</c> mehr, oder Kanal und Netz gibt es nicht mehr), dazu <c>netzId</c>,
    /// <c>bis</c> und <c>durch</c>; <c>kontoId</c> fehlt bei einer Sperre für alle.
    /// </summary>
    public const string Kanalsperre = "KANALSPERRE";
}

/// <summary>Einstellung <c>{durchsageNurTaste}</c> für <c>Verwaltung</c> mit EINSTELLUNG.</summary>
public sealed record EinstellungObjekt : ISteuerBody
{
    public required bool DurchsageNurTaste { get; init; }

    public void Pruefen(ref Pruefung p) { }
}
