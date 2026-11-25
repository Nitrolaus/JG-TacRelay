// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;

namespace Funk.Verwaltung;

/// <summary>Was die Vorprüfung über das TPM sagt. TPM 2.0 ist technisch Pflicht (Sarevocs Entscheidung vom 01.10.2026).</summary>
public enum TpmBefund
{
    /// <summary>Der Platform Crypto Provider meldet TPM 2.0.</summary>
    Tpm20,

    /// <summary>Der Platform Crypto Provider meldet TPM 1.2; der Admin-Schlüssel (ECDSA P-256) braucht TPM 2.0.</summary>
    Tpm12,

    /// <summary>Windows findet kein TPM (TBS_E_TPM_NOT_FOUND oder NTE_DEVICE_NOT_FOUND).</summary>
    KeinTpm,

    /// <summary>
    /// Der Platform Crypto Provider lässt sich nicht öffnen. Dann scheitert auch das Anlegen, denn
    /// <c>CngKey.Create</c> öffnet denselben Anbieter zuerst.
    /// </summary>
    ProviderFehlt,

    /// <summary>Unklar (Eigenschaft fehlt, unbekannte Form, keine Antwort); versuchen darf man es trotzdem.</summary>
    NichtFeststellbar,
}

/// <summary>Ergebnis der TPM-Prüfung mit dem Satz für die Oberfläche und dem, was Windows als PCP_PLATFORM_TYPE meldete.</summary>
public sealed record TpmStand(TpmBefund Befund, string Text, string? Plattform = null)
{
    public bool Gefunden => Befund == TpmBefund.Tpm20;

    /// <summary>Sicher kein Admin-Schlüssel auf diesem PC: kein TPM, TPM 1.2 oder der Anbieter lässt sich nicht öffnen.</summary>
    public bool Sperrt => Befund is TpmBefund.Tpm12 or TpmBefund.KeinTpm or TpmBefund.ProviderFehlt;
}

/// <summary>TPM prüfen, ohne einen Schlüssel anzulegen. Im Betrieb <see cref="PcpTpmPruefung"/>.</summary>
public interface ITpmPruefung
{
    TpmStand Pruefen();
}

/// <summary>Die ganze Vorprüfung; wirft nicht.</summary>
public interface IAdminvoraussetzungen
{
    Task<TpmStand> PruefenAsync();
}

/// <summary>
/// Vorprüfung vor dem Einrichten der Verwaltung (seit 0.4.1). Die TPM-Prüfung läuft auf einem eigenen STA-Faden, so wie seit
/// 0.4.1 gemessen, damit nichts davon die Oberfläche blockiert, und mit Frist: Antwortet sie nicht oder wirft sie, gilt das TPM
/// als nicht feststellbar. Nur lesend, es entsteht kein Schlüssel.
/// </summary>
/// <param name="frist">Höchstens so lange; ohne Angabe <see cref="StandardFrist"/>.</param>
public sealed class Adminvoraussetzungen(ITpmPruefung tpm, TimeSpan? frist = null) : IAdminvoraussetzungen
{
    public static TimeSpan StandardFrist { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Ohne Prüfung (Verwaltungsdienst ohne Angabe, Tests): nichts wird angefasst, das TPM gilt als nicht feststellbar.</summary>
    public static IAdminvoraussetzungen Ungeprueft { get; } = new OhnePruefung();

    /// <summary>Am echten PC: Platform Crypto Provider öffnen und PCP_PLATFORM_TYPE lesen.</summary>
    public static Adminvoraussetzungen AmPc() => new(new PcpTpmPruefung());

    private readonly TimeSpan _frist = frist ?? StandardFrist;

    public Task<TpmStand> PruefenAsync() => AufEigenemFaden(tpm.Pruefen, Tpmdeutung.Unklar);

    private async Task<T> AufEigenemFaden<T>(Func<T> pruefen, Func<string, T> unklar)
    {
        var ergebnis = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var faden = new Thread(() =>
        {
            try
            {
                ergebnis.TrySetResult(pruefen());
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                ergebnis.TrySetResult(unklar(e.Message));
            }
        })
        {
            IsBackground = true,
            Name = "JG TacRelay Vorprüfung",
        };
        faden.SetApartmentState(ApartmentState.STA);
        faden.Start();
        using var ende = new CancellationTokenSource();
        var erster = await Task.WhenAny(ergebnis.Task, Task.Delay(_frist, ende.Token)).ConfigureAwait(false);
        await ende.CancelAsync().ConfigureAwait(false);
        return erster == ergebnis.Task
            ? await ergebnis.Task.ConfigureAwait(false)
            : unklar("keine Antwort binnen " + _frist.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture) + " s");
    }

    private sealed class OhnePruefung : IAdminvoraussetzungen
    {
        public Task<TpmStand> PruefenAsync() => Task.FromResult(Tpmdeutung.Unklar("die Vorprüfung lief hier nicht"));
    }
}

/// <summary>
/// Deutung dessen, was der Microsoft Platform Crypto Provider meldet: die Eigenschaft PCP_PLATFORM_TYPE (ncrypt.h,
/// NCRYPT_PCP_PLATFORM_TYPE_PROPERTY; Form etwa <c>TPM-Version:2.0 -Level:0-Revision:1.59-VendorID:'AMD '-Firmware:...</c>)
/// und die Fehlercodes von NCryptOpenStorageProvider und NCryptGetProperty (winerror.h).
/// </summary>
public static partial class Tpmdeutung
{
    /// <summary>TBS_E_TPM_NOT_FOUND: Der TPM-Basisdienst findet kein TPM.</summary>
    public const int TbsTpmNichtGefunden = unchecked((int)0x8028400F);

    /// <summary>NTE_DEVICE_NOT_FOUND.</summary>
    public const int NteGeraetNichtGefunden = unchecked((int)0x80090035);

    public static TpmStand AusPlattform(string? plattform)
    {
        var m = plattform is null ? Match.Empty : Fassung().Match(plattform);
        if (!m.Success)
            return new TpmStand(TpmBefund.NichtFeststellbar,
                "Der Platform Crypto Provider meldet keine erkennbare TPM-Fassung. Ob TPM 2.0 da ist, steht damit nicht fest; versuchen kannst du es trotzdem.",
                plattform);
        return (m.Groups[1].Value, m.Groups[2].Value) switch
        {
            ("2", _) => new TpmStand(TpmBefund.Tpm20, Voraussetzungstexte.TpmGefunden, plattform),
            ("1", "2") => new TpmStand(TpmBefund.Tpm12,
                "Auf diesem PC steckt ein TPM 1.2. Der Admin-Schlüssel braucht TPM 2.0, deshalb gibt es hier keine Verwaltung.", plattform),
            _ => new TpmStand(TpmBefund.NichtFeststellbar,
                "Der Platform Crypto Provider meldet die TPM-Fassung " + m.Groups[1].Value + "." + m.Groups[2].Value
                + ". Ob der Admin-Schlüssel damit geht, steht nicht fest; versuchen kannst du es trotzdem.", plattform),
        };
    }

    /// <summary>NCryptOpenStorageProvider scheiterte: kein TPM, sonst fehlt der Anbieter. Beides sperrt das Einrichten.</summary>
    public static TpmStand BeimOeffnen(int status) => KeinTpm(status)
        ? OhneTpm(status)
        : new TpmStand(TpmBefund.ProviderFehlt,
            "Der Microsoft Platform Crypto Provider, über den der Admin-Schlüssel ins TPM kommt, lässt sich auf diesem PC nicht öffnen ("
            + Code(status) + "). Damit gibt es hier keine Verwaltung.");

    /// <summary>Geöffnet, aber PCP_PLATFORM_TYPE ließ sich nicht lesen.</summary>
    public static TpmStand BeimLesen(int status) => KeinTpm(status)
        ? OhneTpm(status)
        : new TpmStand(TpmBefund.NichtFeststellbar,
            "Der Platform Crypto Provider ist da, nennt aber keine TPM-Fassung (" + Code(status)
            + "). Ob TPM 2.0 da ist, steht damit nicht fest; versuchen kannst du es trotzdem.");

    /// <summary>Die Prüfung warf oder antwortete nicht.</summary>
    public static TpmStand Unklar(string grund) => new(TpmBefund.NichtFeststellbar,
        "Ob TPM 2.0 da ist, ließ sich nicht feststellen (" + grund + "). Versuchen kannst du es trotzdem.");

    /// <summary>Ein Fehlercode in der Form, die Windows zeigt: 0x8028400F.</summary>
    public static string Code(int status) => "0x" + status.ToString("X8", CultureInfo.InvariantCulture);

    private static bool KeinTpm(int status) => status is TbsTpmNichtGefunden or NteGeraetNichtGefunden;

    private static TpmStand OhneTpm(int status) => new(TpmBefund.KeinTpm,
        "Windows findet auf diesem PC kein TPM (" + Code(status) + "). Ohne TPM 2.0 gibt es hier keine Verwaltung. Ist eins eingebaut, kann es im BIOS/UEFI abgeschaltet sein.");

    [GeneratedRegex(@"TPM-Version:\s*(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex Fassung();
}

/// <summary>
/// Feste Sätze der Vorprüfung, per du, als Striche nur - und _. Getrennt nach dem, was technisch Pflicht ist (TPM 2.0), und
/// dem, was Richtlinie der Gilde ist (Windows-Konto mit PIN oder Kennwort). Eine Empfehlung zur Laufwerksverschlüsselung gibt
/// es seit 0.5.1 nicht mehr (Sarevocs Entscheidung vom 02.10.2026).
/// </summary>
public static class Voraussetzungstexte
{
    public const string TechnischPflicht =
        "Technisch Pflicht ist nur TPM 2.0: Der Admin-Schlüssel entsteht im TPM dieses PCs und ist nicht exportierbar. Er wird nie als Datei "
        + "gespeichert oder kopiert. Im Benutzerprofil legt Windows dazu nur einen Schlüsselblob ab, vom TPM verschlüsselt und nur mit genau diesem TPM nutzbar.";

    /// <summary>Was zur Richtlinie zu sagen ist, ohne sie zu wiederholen (Zeile unter <see cref="TitelRichtlinie"/>).</summary>
    public const string RichtlinieErklaerung =
        "Ein lokales Konto genügt, ein Microsoft-Konto ist nicht nötig. Das verlangt die Gilde, nicht das TPM; der Client prüft es nicht.";

    public const string Richtlinie = "Richtlinie für Admin-PCs: ein Windows-Konto mit PIN oder Kennwort. " + RichtlinieErklaerung;

    public const string Prueft = "Wird gerade geprüft.";

    public const string TpmGefunden = "TPM 2.0 gefunden.";

    public const string TitelTpm = "TPM 2.0 (technisch Pflicht)";

    public const string TitelRichtlinie = "Windows-Konto mit PIN oder Kennwort (Richtlinie)";
}
