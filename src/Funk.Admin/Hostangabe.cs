// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Identitaet;

namespace Funk.Admin;

/// <summary>
/// Ein Host fürs Serverprofil, wie funk-admin ihn annimmt: <c>name</c>, <c>name/v4</c>, <c>name/v6</c> oder <c>name/v4v6</c>
/// (ohne Angabe nur IPv4, wie in der Heimphase). Gilt für <c>profil-schreiben --host</c>, <c>profil-anfrage --host</c> und den
/// Host in der Anfragezeile. Genau geprüft, damit ein Tippfehler nicht erst als "Profil verletzt 3.1" endet: Der Client
/// verbindet später nur mit den Familien, die hier stehen.
/// </summary>
public static class Hostangabe
{
    /// <summary>Liest die Angabe; wirft <see cref="ArgumentException"/> mit einem Satz, was nicht stimmt.</summary>
    public static ProfilHost Lesen(string wert, string option = "--host")
    {
        ArgumentNullException.ThrowIfNull(wert);
        if (wert.Contains("://", StringComparison.Ordinal)) throw new ArgumentException(option + " braucht nur den Namen, ohne https:// davor: " + wert);
        var teile = wert.Split('/');
        var familien = teile.Length > 1 ? teile[1] : "v4";
        if (teile.Length > 2 || familien is not ("v4" or "v6" or "v4v6" or "v6v4"))
            throw new ArgumentException(option + ": hinter dem Namen nur /v4, /v6 oder /v4v6: " + wert);
        if (teile[0].Contains(':', StringComparison.Ordinal))
            throw new ArgumentException(option + " ohne Port; der Port gehört in --tcp-port: " + wert);
        if (!Serverprofil.HostnameGueltig(teile[0]))
            throw new ArgumentException(option + " ist kein gültiger Name (Buchstaben, Ziffern, Bindestrich und Punkte, etwa funk.example.org): " + wert);
        return new ProfilHost(teile[0], familien.Contains("v4", StringComparison.Ordinal), familien.Contains("v6", StringComparison.Ordinal));
    }

    /// <summary>Zurück in die Schreibweise der Angabe: <c>name</c> (nur IPv4), <c>name/v6</c> oder <c>name/v4v6</c>.</summary>
    public static string Text(ProfilHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Name + (host is { IPv4: true, IPv6: true } ? "/v4v6" : host.IPv6 ? "/v6" : "");
    }

    /// <summary>Für die Anzeige: "funk.example.org (IPv4)".</summary>
    public static string Anzeige(ProfilHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Name + (host is { IPv4: true, IPv6: true } ? " (IPv4 und IPv6)" : host.IPv6 ? " (IPv6)" : " (IPv4)");
    }
}
