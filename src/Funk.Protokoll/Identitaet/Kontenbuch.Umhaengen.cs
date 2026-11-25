// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

public sealed partial class Kontenbuch
{
    /// <summary>
    /// Nur an der Konsole: ein freigegebenes Gerät an ein anderes, aktives Konto hängen. Für die Reparatur, wenn ein Antrag als
    /// neues Konto freigegeben wurde, obwohl er ein weiteres Gerät eines bestehenden Kontos war (Sarevocs Fall vom 01.10.2026). Das
    /// Gerät bleibt freigegeben; seine laufende Sitzung endet ohne Abschied, der Client verbindet sich gleich neu und ist dann
    /// im neuen Konto. Ein wartender Antrag geht dagegen mit <c>freigeben --an-konto</c>. Höchstens drei aktive Geräte je Konto.
    /// </summary>
    public Ergebnis GeraetUmhaengen(Akteur akteur, uint geraetId, uint zielKonto)
    {
        if (!akteur.IstAdminWerkzeug) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        var s = _stand;
        var jetzt = Jetzt;
        if (!s.Geraete.TryGetValue(geraetId, out var g) || IstVerfallen(g, jetzt)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!s.Konten.TryGetValue(zielKonto, out var ziel) || ziel.IstAntrag) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (g.Status != GeraetStatus.Freigegeben || g.KontoId == zielKonto) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (ziel.Status != KontoStatus.Aktiv) return Ergebnis.Mit(Fehlercode.KontoGesperrt);
        if (AktiveGeraete(s, zielKonto, jetzt) >= MaxAktiveGeraete) return Ergebnis.Mit(Fehlercode.GeraeteLimit);
        var vorher = g.KontoId;
        s = MitGeraet(s, g with { KontoId = zielKonto, AntragVon = null, Markierung = Markierung.Keine });
        s = Ereignis(Ereignis(s, zielKonto, jetzt), vorher, jetzt);
        Uebernehmen(s);
        return new Ergebnis(Fehlercode.Keiner, [new SitzungenBeenden(vorher, geraetId, Trenngrund.Umgehaengt)]);
    }
}
