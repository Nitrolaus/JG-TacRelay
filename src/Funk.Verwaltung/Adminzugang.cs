// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung;

/// <summary>
/// Der Admin-Nachweis von der Client-Seite (3.9), ohne Oberfläche: Nonce holen, mit dem Admin-Schlüssel unterschreiben
/// (Sitzung, Gerät, Nonce, Zweck), dann Antrag, Geheimnis, erster Code, Anmeldung oder Bestätigung. Den Admin-Schlüssel (im
/// Betrieb ECDSA P-256 im TPM, ohne Rückfall auf Software, <see cref="TpmAdminSchluessel"/>) bringt der Verwaltungsteil mit;
/// hier kommt er als <see cref="ECDsa"/> herein, in Tests flüchtig. Gespeichert wird nichts davon. Liegt im Verwaltungsteil,
/// nicht in Funk.ClientKern: Das normale Setup bringt keinen Verwaltungscode mit (A45, Gegenprüfung Stufe 1).
/// </summary>
public static class Adminzugang
{
    /// <summary>Nonce holen und für <paramref name="zweck"/> unterschreiben; <c>null</c>, wenn der Server keine gibt.</summary>
    public static async Task<(string? Unterschrift, Antwort Antwort)> UnterschreibenAsync(this FunkClient client, ECDsa adminSchluessel, AdminZweck zweck,
        CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(adminSchluessel);
        var a = await client.AnfrageAsync(Nachrichtentyp.AdminNonce, LeererBody.Wert, abbruch).ConfigureAwait(false);
        if (a.Body is not AdminNonceAntwort n) return (null, a);
        var nonce = Convert.FromBase64String(n.Nonce);
        try
        {
            var unterschrift = Adminnachweis.Unterschreiben(adminSchluessel, client.Lage.SitzungsId, client.Geraet, nonce, zweck);
            return (Convert.ToBase64String(unterschrift), a);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    /// <summary><c>AdminEinrichtung ANTRAG</c>: den Admin-Schlüssel beantragen; die Antwort nennt die Kennung zum Vorlesen.</summary>
    public static async Task<Antwort> AdminAntragAsync(this FunkClient client, ECDsa adminSchluessel, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(adminSchluessel);
        var (u, fehler) = await client.UnterschreibenAsync(adminSchluessel, AdminZweck.Antrag, abbruch).ConfigureAwait(false);
        if (u is null) return fehler;
        return await client.AnfrageAsync(Nachrichtentyp.AdminEinrichtung, new AdminEinrichtungAnfrage
        {
            Op = AdminEinrichtungOperation.Antrag,
            Unterschrift = u,
            Schluessel = Convert.ToBase64String(adminSchluessel.ExportSubjectPublicKeyInfo()),
        }, abbruch).ConfigureAwait(false);
    }

    /// <summary><c>AdminEinrichtung GEHEIMNIS_ABHOLEN</c>: genau einmal; Geheimnis und otpauth-URI für den QR-Code.</summary>
    public static async Task<Antwort> GeheimnisAbholenAsync(this FunkClient client, ECDsa adminSchluessel, CancellationToken abbruch = default)
    {
        var (u, fehler) = await client.UnterschreibenAsync(adminSchluessel, AdminZweck.Geheimnis, abbruch).ConfigureAwait(false);
        if (u is null) return fehler;
        return await client.AnfrageAsync(Nachrichtentyp.AdminEinrichtung, new AdminEinrichtungAnfrage
        {
            Op = AdminEinrichtungOperation.GeheimnisAbholen,
            Unterschrift = u,
        }, abbruch).ConfigureAwait(false);
    }

    /// <summary><c>AdminEinrichtung BESTAETIGEN</c>: erster Code aus der App; die Antwort trägt die 10 Notfallcodes.</summary>
    public static async Task<Antwort> EinrichtungBestaetigenAsync(this FunkClient client, ECDsa adminSchluessel, string code, CancellationToken abbruch = default)
    {
        var (u, fehler) = await client.UnterschreibenAsync(adminSchluessel, AdminZweck.Einrichtung, abbruch).ConfigureAwait(false);
        if (u is null) return fehler;
        return await client.AnfrageAsync(Nachrichtentyp.AdminEinrichtung, new AdminEinrichtungAnfrage
        {
            Op = AdminEinrichtungOperation.Bestaetigen,
            Unterschrift = u,
            Code = code,
        }, abbruch).ConfigureAwait(false);
    }

    /// <summary><c>AdminAnmeldung</c>: Admin-Sitzung öffnen, mit Code aus der App oder einem Notfallcode.</summary>
    public static async Task<Antwort> AdminAnmeldenAsync(this FunkClient client, ECDsa adminSchluessel, string? code, string? notfallcode = null,
        CancellationToken abbruch = default)
    {
        var (u, fehler) = await client.UnterschreibenAsync(adminSchluessel, AdminZweck.Anmeldung, abbruch).ConfigureAwait(false);
        if (u is null) return fehler;
        return await client.AnfrageAsync(Nachrichtentyp.AdminAnmeldung, new AdminAnmeldungAnfrage
        {
            Unterschrift = u,
            Code = code,
            Notfallcode = notfallcode,
        }, abbruch).ConfigureAwait(false);
    }

    /// <summary><c>AdminBestaetigen</c>: frischer Code für heikle Handlungen (5 min), nur in einer offenen Admin-Sitzung.</summary>
    public static Task<Antwort> AdminBestaetigenAsync(this FunkClient client, string? code, string? notfallcode = null, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.AnfrageAsync(Nachrichtentyp.AdminBestaetigen, new AdminBestaetigenAnfrage { Code = code, Notfallcode = notfallcode }, abbruch);
    }

    /// <summary>
    /// Was die Schritt-Bestätigung bekam: sechs Ziffern gehen als Code, ein Notfallcode (zwölf Zeichen) als Notfallcode an
    /// <c>AdminBestaetigen</c>. Die Formen schließen einander aus.
    /// </summary>
    public static (string? Code, string? Notfallcode) Bestaetigungsart(string eingabe) =>
        Codeform.Totp(eingabe) is { } code ? (code, null) : (null, Codeform.Notfall(eingabe) ?? eingabe);

    /// <summary><c>AdminAbmelden</c>: "Verwaltung schließen"; der Funk läuft weiter.</summary>
    public static Task<Antwort> AdminAbmeldenAsync(this FunkClient client, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.AnfrageAsync(Nachrichtentyp.AdminAbmelden, LeererBody.Wert, abbruch);
    }

    /// <summary>
    /// Eine heikle Handlung senden; kommt <c>SCHRITT_BESTAETIGEN</c>, fragt <paramref name="codeFragen"/> nach einem frischen
    /// Code, bestätigt und sendet die Handlung noch einmal (3.9, ANMELDUNG-SICHERHEIT "Schritt-Bestätigung").
    /// </summary>
    public static async Task<Antwort> MitBestaetigungAsync<T>(this FunkClient client, Nachrichtentyp typ, T body,
        Func<CancellationToken, Task<string?>> codeFragen, CancellationToken abbruch = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(codeFragen);
        var a = await client.AnfrageAsync(typ, body, abbruch).ConfigureAwait(false);
        if (a.Code != Protokoll.Steuerung.Fehlercode.SchrittBestaetigen) return a;
        if (await codeFragen(abbruch).ConfigureAwait(false) is not { } eingabe) return a;
        // Auch ein Notfallcode, wenn das Telefon fehlt (Gegenprüfung Stufe 1).
        var (code, notfallcode) = Bestaetigungsart(eingabe);
        var b = await client.AdminBestaetigenAsync(code, notfallcode, abbruch).ConfigureAwait(false);
        if (!b.Ok) return b;
        return await client.AnfrageAsync(typ, body, abbruch).ConfigureAwait(false);
    }
}
