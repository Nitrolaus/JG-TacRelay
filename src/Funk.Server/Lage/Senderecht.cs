// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

/// <summary>
/// Senderecht je Paket (6.3) als reine Funktion über dem Schnappschuss. Ohne Ratenlimit; das hält der
/// <see cref="Weiterleiter"/> je Sitzung. Allokationsfrei.
/// </summary>
public static class Senderecht
{
    /// <summary>Form nach 5.4 und 5.13: Zielart, n, Sortierung, Global kein Durchsageziel, ENDE/Opus, m, TOC.</summary>
    public static SendeFehlerCode FormPruefen(in Sprachpaket paket)
    {
        var ziele = paket.Ziele;
        var n = ziele.Length;
        switch (paket.Zielart)
        {
            case Sendeart.Netz:
                if (n is < 1 or > Fristen.ZieleNetzMax) return SendeFehlerCode.Ungueltig;
                break;
            case Sendeart.Durchsage:
                if (n > Fristen.ZieleDurchsageMax) return SendeFehlerCode.Ungueltig;
                break;
            default:
                return SendeFehlerCode.Ungueltig;
        }
        for (var i = 0; i < n; i++)
        {
            if (i > 0 && ziele[i] <= ziele[i - 1]) return SendeFehlerCode.Ungueltig;
            if (paket.Zielart == Sendeart.Durchsage && ziele[i] == Netz.GlobalId) return SendeFehlerCode.Ungueltig;
        }

        var m = paket.Opus.Length;
        if (paket.IstEnde)
        {
            if (m != 0 || (paket.Kennzeichen & Paketkennzeichen.Preroll) != 0) return SendeFehlerCode.Ungueltig;
        }
        else if (m is < 1 or > Fristen.OpusMax || !OpusTocGueltig(paket.Opus))
        {
            return SendeFehlerCode.Ungueltig;
        }
        return SendeFehlerCode.Keiner;
    }

    /// <summary>
    /// Opus-TOC nach 5.13 (RFC 6716 Abschnitt 3.1): genau 20 ms mono. Gleiche Regel wie
    /// <c>Funk.Protokoll.Sprache.OpusPruefung</c>; hier eigenständig, damit dieser Baustein ohne sie baut.
    /// </summary>
    public static bool OpusTocGueltig(ReadOnlySpan<byte> opus)
    {
        if (opus.IsEmpty || opus.Length > Fristen.OpusMax) return false;
        var toc = opus[0];
        var config = toc >> 3;
        if (((toc >> 2) & 1) != 0) return false;
        int anzahl;
        switch (toc & 3)
        {
            case 0:
                anzahl = 1;
                break;
            case 1:
            case 2:
                anzahl = 2;
                break;
            default:
                if (opus.Length < 2) return false;
                anzahl = opus[1] & 0x3F;
                break;
        }
        // Framedauer in Zehntel-Millisekunden.
        var dauer = config switch
        {
            < 12 => (config & 3) switch { 0 => 100, 1 => 200, 2 => 400, _ => 600 },
            < 16 => (config & 1) == 0 ? 100 : 200,
            _ => (config & 3) switch { 0 => 25, 1 => 50, 2 => 100, _ => 200 },
        };
        return dauer * anzahl == 200;
    }

    /// <summary>
    /// Rechteprüfung aus 6.3 ab "erlaubt = []". Liefert <see cref="SendeFehlerCode.Keiner"/>, wenn das Paket an
    /// die erlaubte Teilmenge geht (bei DURCHSAGE mit n = 0 ist sie leer und heißt "an alle"), sonst den Code
    /// des ganzen Verwurfs. Gestrichene Ziele stehen in <paramref name="gestrichen"/>. Beide Puffer brauchen mindestens
    /// so viele Plätze wie das Paket Ziele hat (<paramref name="gestrichen"/> mindestens einen).
    /// </summary>
    public static SendeFehlerCode Pruefen(Schnappschuss z, in Sprachpaket paket, long jetzt,
        Span<ushort> erlaubt, Span<SendeFehlerEintrag> gestrichen, out int erlaubtAnzahl, out int gestrichenAnzahl)
    {
        ArgumentNullException.ThrowIfNull(z);
        var n = paket.Ziele.Length;
        if (erlaubt.Length < n) throw new ArgumentException("Puffer kleiner als die Zahl der Ziele im Paket.", nameof(erlaubt));
        if (gestrichen.Length < Math.Max(1, n)) throw new ArgumentException("Puffer kleiner als die Zahl der Ziele im Paket.", nameof(gestrichen));
        var si = z.SitzungsIndex(paket.Sitzung);
        if (si < 0)
        {
            erlaubtAnzahl = 0;
            gestrichenAnzahl = 0;
            return SendeFehlerCode.Ungueltig;
        }
        return Pruefen(z, si, paket, jetzt, erlaubt, gestrichen, out erlaubtAnzahl, out gestrichenAnzahl);
    }

    internal static SendeFehlerCode Pruefen(Schnappschuss z, int senderIndex, in Sprachpaket paket, long jetzt,
        Span<ushort> erlaubt, Span<SendeFehlerEintrag> gestrichen, out int erlaubtAnzahl, out int gestrichenAnzahl)
    {
        var konto = z.KontoAn(senderIndex);
        var rechte = z.RechteAn(senderIndex, jetzt);
        var ziele = paket.Ziele;
        var va = paket.IstVa;
        var ne = 0;
        var ng = 0;

        if (paket.Zielart == Sendeart.Netz)
        {
            if (ziele.Length > 1 && !rechte.Hat(Recht.MehrfachSenden))
            {
                gestrichen[0] = new SendeFehlerEintrag(SendeFehlerCode.MehrfachNichtErlaubt, 0, 0);
                erlaubtAnzahl = 0;
                gestrichenAnzahl = 1;
                return SendeFehlerCode.MehrfachNichtErlaubt;
            }
            foreach (var netz in ziele)
            {
                var ni = z.NetzIndex(netz);
                if (ni < 0 && !z.KanalGueltig(netz))
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.UnbekanntesNetz, netz, 0);
                }
                else if (ni < 0)
                {
                    // Ein gültiger Kanal ohne Spalte hält keine Sitzung, also auch der Sender nicht; eine Ruhe sendet nie (6.16).
                    gestrichen[ng++] = Schnappschuss.IstEntzogen(konto, netz, jetzt, out var entzug)
                        ? new SendeFehlerEintrag(SendeFehlerCode.ZielEntzogen, netz, entzug.Durch, entzug.Umstellung)
                        : new SendeFehlerEintrag(SendeFehlerCode.KeinSenderecht, netz, 0);
                }
                else if (netz == Netz.GlobalId && !rechte.Hat(Recht.GlobalSenden))
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.KeinRechtGlobal, netz, 0);
                }
                else if (netz != Netz.GlobalId && (z.MitgliedAn(senderIndex, ni) is var m && (m == Schnappschuss.KeinMitglied || (m & Schnappschuss.BitSenden) == 0)))
                {
                    gestrichen[ng++] = Schnappschuss.IstEntzogen(konto, netz, jetzt, out var entzug)
                        ? new SendeFehlerEintrag(SendeFehlerCode.ZielEntzogen, netz, entzug.Durch, entzug.Umstellung)
                        : new SendeFehlerEintrag(SendeFehlerCode.KeinSenderecht, netz, 0);
                }
                else if (z.GesperrtAn(senderIndex, ni, jetzt, out var gesperrtDurch))
                {
                    // Kanalsperre (6.17): kein Senderecht, mit dem, der gesperrt hat.
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.KeinSenderecht, netz, gesperrtDurch);
                }
                else if (Schnappschuss.IstStumm(konto, netz, z.RegelnetzAn(ni), jetzt, out var durch))
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.Stummgeschaltet, netz, durch);
                }
                else if (z.NurTasteAn(ni) && va)
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.NurTaste, netz, 0);
                }
                else
                {
                    erlaubt[ne++] = netz;
                }
            }
        }
        else
        {
            if (!rechte.Hat(Recht.Durchsage) || (ziele.IsEmpty && !rechte.Hat(Recht.DurchsageAlle)))
                return Ganz(SendeFehlerCode.KeinRechtDurchsage, 0, gestrichen, out erlaubtAnzahl, out gestrichenAnzahl);
            if (z.Einstellungen.DurchsageNurTaste && va)
                return Ganz(SendeFehlerCode.NurTaste, 0, gestrichen, out erlaubtAnzahl, out gestrichenAnzahl);
            if (Schnappschuss.IstStumm(konto, 0, 0, jetzt, out var durchAlle))
                return Ganz(SendeFehlerCode.Stummgeschaltet, durchAlle, gestrichen, out erlaubtAnzahl, out gestrichenAnzahl);
            if (ziele.IsEmpty)
            {
                erlaubtAnzahl = 0;
                gestrichenAnzahl = 0;
                return SendeFehlerCode.Keiner;
            }
            foreach (var netz in ziele)
            {
                var ni = z.NetzIndex(netz);
                if (ni < 0)
                {
                    // Ein gültiger Kanal, den niemand hält, erreicht niemanden und wird nicht gestrichen (6.3, 6.16).
                    if (!z.KanalGueltig(netz)) gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.UnbekanntesNetz, netz, 0);
                }
                else if (z.GesperrtAn(senderIndex, ni, jetzt, out var gesperrtDurch))
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.KeinSenderecht, netz, gesperrtDurch);
                }
                else if (Schnappschuss.IstStumm(konto, netz, z.RegelnetzAn(ni), jetzt, out var durch))
                {
                    gestrichen[ng++] = new SendeFehlerEintrag(SendeFehlerCode.Stummgeschaltet, netz, durch);
                }
                else
                {
                    erlaubt[ne++] = netz;
                }
            }
        }

        erlaubtAnzahl = ne;
        gestrichenAnzahl = ng;
        return ne == 0 && ng > 0 ? gestrichen[0].Code : SendeFehlerCode.Keiner;
    }

    private static SendeFehlerCode Ganz(SendeFehlerCode code, uint durch, Span<SendeFehlerEintrag> gestrichen,
        out int erlaubtAnzahl, out int gestrichenAnzahl)
    {
        gestrichen[0] = new SendeFehlerEintrag(code, 0, durch);
        erlaubtAnzahl = 0;
        gestrichenAnzahl = 1;
        return code;
    }
}
