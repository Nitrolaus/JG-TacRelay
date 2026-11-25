// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>Weg, auf dem ein Datagramm kam: UDP oder SpracheTunnel (0x8001) im Steuerkanal.</summary>
public enum Weg : byte
{
    Udp = 1,
    Tunnel = 2,
}

/// <summary>Ergebnis der Empfangsprüfung (5.9). Alles außer <see cref="Angenommen"/> heißt verwerfen, ohne Antwort.</summary>
public enum Empfangsergebnis : byte
{
    Angenommen = 0,

    /// <summary>Schritt 0: Sprache lag länger als 60 ms zwischen Empfang und Verarbeitung.</summary>
    ZuAlt,

    /// <summary>Schritt 1: abgeschnitten, kürzer als 41 oder länger als 1200 Byte.</summary>
    LaengeUngueltig,

    /// <summary>Schritt 2.</summary>
    VersionFalsch,
    TypUnbekannt,
    FalscheRichtung,
    ReserviertGesetzt,
    LaengePasstNichtZumTyp,

    /// <summary>Schritt 3: keine Sitzung oder nicht AKTIV.</summary>
    SitzungUnbekannt,

    /// <summary>Schritt 3 im Tunnel: fremde Sitzungs-ID. Folge: PROTOKOLL und Trennung.</summary>
    TunnelFremdeSitzung,

    /// <summary>Schritt 4: Herkunft oder Budget.</summary>
    BudgetErschoepft,

    /// <summary>Schritt 5.</summary>
    EpocheNichtAngenommen,

    /// <summary>Schritt 6: doppelt oder zu alt.</summary>
    Replay,

    /// <summary>Schritt 7: falscher Tag; gezählt, Fenster unverändert.</summary>
    TagUngueltig,

    /// <summary>Schritt 8: zwischen Vorprüfung und Setzen hat eine gleiche Kopie gewonnen.</summary>
    ReplayNachTag,

    /// <summary>Ausnahme in Schritt 6 (Kopierprüfung, 5.10): Duplikat mit gültigem Tag, also Kopierverdacht.</summary>
    AuthentischesDuplikat,
}

/// <summary>Zuordnung der Ergebnisse zu den Schritten aus 5.9.</summary>
public static class Empfangsergebnisse
{
    /// <summary>Schrittnummer, in der verworfen wurde; 10 für angenommen (Verarbeitung nach Typ).</summary>
    public static int Schritt(this Empfangsergebnis ergebnis) => ergebnis switch
    {
        Empfangsergebnis.ZuAlt => 0,
        Empfangsergebnis.LaengeUngueltig => 1,
        Empfangsergebnis.VersionFalsch or Empfangsergebnis.TypUnbekannt or Empfangsergebnis.FalscheRichtung
            or Empfangsergebnis.ReserviertGesetzt or Empfangsergebnis.LaengePasstNichtZumTyp => 2,
        Empfangsergebnis.SitzungUnbekannt or Empfangsergebnis.TunnelFremdeSitzung => 3,
        Empfangsergebnis.BudgetErschoepft => 4,
        Empfangsergebnis.EpocheNichtAngenommen => 5,
        Empfangsergebnis.Replay => 6,
        Empfangsergebnis.TagUngueltig or Empfangsergebnis.AuthentischesDuplikat => 7,
        Empfangsergebnis.ReplayNachTag => 8,
        _ => 10,
    };
}

/// <summary>
/// Was die Empfangsprüfung vom Aufrufer braucht. Der Server liefert Sitzungstabelle, Herkunft und Budgets;
/// der Client nur die eigene Sitzung, sein Budget ist immer frei (5.9: ohne Schritt 4 und 9).
/// </summary>
public interface IEmpfangsUmgebung
{
    /// <summary>Schritt 3: Empfangskanal der Sitzung, wenn es sie gibt und sie AKTIV ist; sonst null.</summary>
    EmpfangsKanal? KanalSuchen(uint sitzungsId);

    /// <summary>Schritt 4: Herkunft und Budget je Weg und Quelladresse; false heißt verwerfen ohne weitere Arbeit.</summary>
    bool BudgetNehmen(EmpfangsKanal kanal);

    /// <summary>Ausnahme in Schritt 6 (Kopierprüfung, 5.10): ob ein Duplikat dieses Zählers trotzdem zu entschlüsseln ist.</summary>
    bool DuplikatEntschluesseln(EmpfangsKanal kanal, ulong zaehler);
}

/// <summary>Begleitdaten eines empfangenen Datagramms.</summary>
/// <param name="Richtung">Erwartete Richtung: hoch beim Server, runter beim Client.</param>
/// <param name="Weg">UDP oder Tunnel.</param>
/// <param name="Empfangen">Stopwatch-Zeitstempel direkt nach dem Empfang (Schritt 0).</param>
/// <param name="Abgeschnitten">Das System meldete ein abgeschnittenes Datagramm (<c>SocketFlags.Truncated</c>).</param>
/// <param name="TragendeSitzung">Im Tunnel die Sitzung der tragenden TLS-Verbindung.</param>
public readonly record struct Empfangsdaten(Richtung Richtung, Weg Weg, long Empfangen, bool Abgeschnitten = false, uint TragendeSitzung = 0);

/// <summary>Empfangsprüfung in der verbindlichen Reihenfolge aus 5.9, Schritte 0 bis 8.</summary>
public static class Empfangspruefung
{
    private static readonly long MaxAlterTicks = Uhr.Ticks(Sprachkanal.MaxEmpfangsalter);

    /// <summary>
    /// Prüft ein Datagramm von Schritt 0 bis 8 und entschlüsselt es nach <paramref name="klartext"/>
    /// (mindestens <see cref="Sprachkanal.MaxKlartext"/> Byte). Vor Schritt 7 wird nichts allokiert und nichts entschlüsselt.
    /// Schritt 9 (Adressbehandlung) und 10 (Verarbeitung nach Typ) folgen beim Aufrufer.
    /// Ein zu kleiner Puffer wirft bei jedem Aufruf, unabhängig vom Datagramm: Welche Länge ein Fremder schickt, darf nie
    /// darüber entscheiden, ob der Relay-Thread eine Ausnahme sieht.
    /// </summary>
    public static Empfangsergebnis Pruefen<TUmgebung>(TUmgebung umgebung, ReadOnlySpan<byte> datagramm, in Empfangsdaten daten,
        long jetzt, Span<byte> klartext, out SprachKopf kopf, out EmpfangsKanal? kanal)
        where TUmgebung : IEmpfangsUmgebung
    {
        ArgumentNullException.ThrowIfNull(umgebung);
        ArgumentOutOfRangeException.ThrowIfLessThan(klartext.Length, Sprachkanal.MaxKlartext, nameof(klartext));
        kopf = default;
        kanal = null;

        // 0. Empfangsalter, nur für Sprache
        if (datagramm.Length >= 2 && PaketTypen.IstSprache((PaketTyp)datagramm[1])
            && jetzt - daten.Empfangen > MaxAlterTicks)
            return Empfangsergebnis.ZuAlt;

        // 1. und 2.
        var ergebnis = Vorpruefen(datagramm, daten.Abgeschnitten, daten.Richtung, out kopf);
        if (ergebnis != Empfangsergebnis.Angenommen) return ergebnis;

        // 3. Sitzung; im Tunnel muss es die der tragenden Verbindung sein
        if (daten.Weg == Weg.Tunnel && kopf.SitzungsId != daten.TragendeSitzung) return Empfangsergebnis.TunnelFremdeSitzung;
        kanal = umgebung.KanalSuchen(kopf.SitzungsId);
        if (kanal is null) return Empfangsergebnis.SitzungUnbekannt;

        // 4. Herkunft und Budget
        if (!umgebung.BudgetNehmen(kanal)) return Empfangsergebnis.BudgetErschoepft;

        // 5. bis 8.
        var kopie = umgebung.DuplikatEntschluesseln(kanal, kopf.Zaehler);
        return kanal.Oeffnen(datagramm, klartext, jetzt, kopie);
    }

    /// <summary>
    /// Schritte 1 und 2, ohne Zustand: Länge 41-1200 und nicht abgeschnitten; Version 1, Typ bekannt und in der
    /// erwarteten Richtung, reserviert 0, Länge passt zum Typ. <see cref="Empfangsergebnis.Angenommen"/> heißt bestanden.
    /// </summary>
    public static Empfangsergebnis Vorpruefen(ReadOnlySpan<byte> datagramm, bool abgeschnitten, Richtung richtung, out SprachKopf kopf)
    {
        kopf = default;
        if (abgeschnitten || datagramm.Length < Sprachkanal.MinDatagramm || datagramm.Length > Sprachkanal.MaxDatagramm)
            return Empfangsergebnis.LaengeUngueltig;

        kopf = SprachKopf.Lesen(datagramm);
        if (kopf.Version != Sprachkanal.Version) return Empfangsergebnis.VersionFalsch;
        if (!PaketTypen.IstBekannt(kopf.Typ)) return Empfangsergebnis.TypUnbekannt;
        if (PaketTypen.RichtungVon(kopf.Typ) != richtung) return Empfangsergebnis.FalscheRichtung;
        if (kopf.Reserviert != 0) return Empfangsergebnis.ReserviertGesetzt;
        if (!PaketTypen.LaengePasst(kopf.Typ, datagramm.Length)) return Empfangsergebnis.LaengePasstNichtZumTyp;
        return Empfangsergebnis.Angenommen;
    }
}
