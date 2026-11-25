// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using Funk.Server.Betrieb;
using Funk.Server.Steuer;
using I = Funk.Protokoll.Identitaet;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Admin-Sitzung und Schritt-Bestätigung (3.9): Nonce, Admin-Anmeldung mit Unterschrift des Admin-Schlüssels und Code aus
/// der Authenticator-App (oder Notfallcode), Bestätigung, Abmelden, Einrichtung. Die Verwaltungsrechte wirken nur in einer
/// offenen Admin-Sitzung (<see cref="AkteurVon"/>, <see cref="VerbindungenMitRecht"/>, <see cref="HatRecht"/>); sie endet nach
/// 12 h (seit 0.9.1, Sarevoc 08.10.2026, Q54: kein Ende mehr nach 30 min ohne Verwaltungshandlung), bei Trennung, Abmelden und jeder Sperre von Konto, Gerät oder
/// Admin-Nachweis. Es gibt keinen Schalter, der das abschaltet, auch nicht für Tests.
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>Admin-Sitzung endet spätestens nach so langer Zeit.</summary>
    public static readonly TimeSpan AdminHoechstdauer = TimeSpan.FromHours(12);

    /// <summary>So lange nach einem Code gehen heikle Handlungen ohne neuen Code (frische Bestätigung).</summary>
    public static readonly TimeSpan Bestaetigungsdauer = TimeSpan.FromMinutes(5);

    private readonly Dictionary<long, AdminSitzung> _adminSitzungen = [];
    private readonly Dictionary<long, (byte[] Wert, DateTimeOffset Bis)> _nonces = [];

    /// <summary>Eine offene Admin-Sitzung auf einer Verbindung (flüchtig; ein Neustart beendet sie).</summary>
    private sealed class AdminSitzung(uint kontoId, uint geraetId, DateTimeOffset beginn)
    {
        public uint KontoId { get; } = kontoId;
        public uint GeraetId { get; } = geraetId;
        public DateTimeOffset Beginn { get; } = beginn;
        /// <summary>Letzte Verwaltungshandlung; seit 0.9.1 (Q54) nur noch Auskunft, das Ende hängt nicht mehr daran.</summary>
        public DateTimeOffset LetzteHandlung { get; set; } = beginn;
        public DateTimeOffset BestaetigtBis { get; set; } = beginn + Bestaetigungsdauer;

        /// <summary>Ende der Sitzung: seit 0.9.1 (Q54) nur noch die Höchstdauer; bis 0.9.0 dazu 30 min ohne Verwaltungshandlung.</summary>
        public DateTimeOffset Bis => Beginn + AdminHoechstdauer;
    }

    private DateTimeOffset JetztUtc => Uhr.GetUtcNow();

    /// <summary>
    /// Die offene Admin-Sitzung einer Verbindung, geprüft am aktuellen Stand: Zeit, Konto AKTIV und ADMIN, Gerät FREIGEGEBEN,
    /// Admin-Nachweis AKTIV. Trifft etwas davon nicht mehr zu, endet sie hier sofort.
    /// </summary>
    private AdminSitzung? AdminSitzungVon(Verbindung v)
    {
        if (!_adminSitzungen.TryGetValue(v.Id, out var a)) return null;
        var stand = Konten.Stand;
        string? grund = null;
        if (v.Endet) grund = "GETRENNT";
        else if (JetztUtc >= a.Bis) grund = "ABGELAUFEN";
        else if (!stand.Konten.TryGetValue(a.KontoId, out var k) || k.Status != I.KontoStatus.Aktiv || k.Stufe != I.Stufe.Admin) grund = "KONTO";
        else if (!stand.Geraete.TryGetValue(a.GeraetId, out var g) || g.Status != I.GeraetStatus.Freigegeben) grund = "GERAET";
        else if (!Admins.IstEingerichtet(a.KontoId)) grund = "ZWEITFAKTOR";
        if (grund is null) return a;
        AdminSitzungBeenden(v, grund);
        return null;
    }

    /// <summary>Wer die Anfrage stellt: normale Sitzung oder Admin-Sitzung, mit oder ohne frische Bestätigung.</summary>
    private I.Akteur AkteurVon(Verbindung v)
    {
        var a = AdminSitzungVon(v);
        return a is null ? I.Akteur.Sitzung(v.KontoId, v.GeraetId) : I.Akteur.Verwaltend(v.KontoId, v.GeraetId, a.BestaetigtBis > JetztUtc);
    }

    /// <summary>Eine Verwaltungshandlung wird vermerkt; seit 0.9.1 (Q54) verlängert sie nichts mehr, die Sitzung hält ohnehin bis 12 h.</summary>
    private void AdminHandlung(Verbindung v)
    {
        if (_adminSitzungen.TryGetValue(v.Id, out var a)) a.LetzteHandlung = JetztUtc;
    }

    private void AdminSitzungBeenden(Verbindung v, string grund, bool melden = true)
    {
        if (!_adminSitzungen.Remove(v.Id, out var a)) return;
        Vermerken("ADMIN_SITZUNG_ENDE", a.KontoId, a.KontoId, $"Admin-Sitzung von {KontoText(a.KontoId)} beendet ({grund}), Gerät {GeraetText(a.GeraetId)}");
        if (melden && !v.Endet)
        {
            v.Push(S.Nachrichtentyp.Meldung, new S.Meldung
            {
                Art = S.MeldungsArten.AdminSitzung,
                KontoId = a.KontoId,
                Details = Antwort.DetailsBauen(w =>
                {
                    w.WriteString("op", "BEENDET");
                    w.WriteString("grund", grund);
                }),
            });
        }
    }

    /// <summary>Takt: abgelaufene Admin-Sitzungen und Nonces aufräumen.</summary>
    private void AdminSitzungenPruefen()
    {
        foreach (var id in _adminSitzungen.Keys.ToList())
        {
            if (_verbindungen.TryGetValue(id, out var v)) AdminSitzungVon(v);
            else _adminSitzungen.Remove(id);
        }
        var jetzt = JetztUtc;
        foreach (var (id, n) in _nonces.ToList())
        {
            if (n.Bis > jetzt && _verbindungen.ContainsKey(id)) continue;
            CryptographicOperations.ZeroMemory(n.Wert);
            _nonces.Remove(id);
        }
    }

    /// <summary>Ende einer Verbindung: Admin-Sitzung und Nonce verfallen.</summary>
    private void AdminVerbindungEndet(Verbindung v)
    {
        AdminSitzungBeenden(v, "GETRENNT", melden: false);
        if (_nonces.Remove(v.Id, out var n)) CryptographicOperations.ZeroMemory(n.Wert);
    }

    /// <summary>Die Nonce der Verbindung, genau einmal und nur binnen 60 s; danach ist sie verbraucht.</summary>
    private byte[]? NonceNehmen(Verbindung v)
    {
        if (!_nonces.Remove(v.Id, out var n)) return null;
        if (n.Bis > JetztUtc) return n.Wert;
        CryptographicOperations.ZeroMemory(n.Wert);
        return null;
    }

    // ---------------------------------------------------------------- Anfragen (0x0090-0x0094)

    private Antwort AdminNonce(Verbindung v)
    {
        // Nur für Konten mit Admin-Nachweis (auch vorgesehen); allen anderen sagt der Server nichts.
        if (Admins.Eintrag(v.KontoId) is null) return Antwort.Fehl(S.Fehlercode.KeinRecht);
        var nonce = RandomNumberGenerator.GetBytes(I.Adminnachweis.NonceLaenge);
        if (_nonces.Remove(v.Id, out var alt)) CryptographicOperations.ZeroMemory(alt.Wert);
        _nonces[v.Id] = (nonce, JetztUtc + I.Adminnachweis.NonceGueltigkeit);
        return Antwort.Mit(new S.AdminNonceAntwort { Nonce = Convert.ToBase64String(nonce), GueltigMs = (uint)I.Adminnachweis.NonceGueltigkeit.TotalMilliseconds });
    }

    private I.Nachweis? Nachweis(Verbindung v, string unterschrift)
    {
        var nonce = NonceNehmen(v);
        if (nonce is null) return null;
        return new I.Nachweis(v.SitzungsId, v.Geraet, nonce, Convert.FromBase64String(unterschrift));
    }

    private Antwort AdminAnmeldung(Verbindung v, S.AdminAnmeldungAnfrage a)
    {
        if (!Konten.Stand.Konten.TryGetValue(v.KontoId, out var k) || k.Stufe != I.Stufe.Admin || Admins.Eintrag(v.KontoId) is null)
            return Antwort.Fehl(S.Fehlercode.KeinRecht);
        if (Nachweis(v, a.Unterschrift) is not { } nachweis) return Antwort.Fehl(S.Fehlercode.Ungueltig, "Nonce fehlt oder abgelaufen");
        var r = Admins.Anmelden(v.KontoId, v.GeraetId, nachweis, a.Code, a.Notfallcode);
        ZweitfaktorVerarbeiten(r, v.KontoId, "Anmeldung");
        if (!r.Ok) return ZweitfaktorFehler(r);
        var jetzt = JetztUtc;
        if (_adminSitzungen.ContainsKey(v.Id)) AdminSitzungBeenden(v, "NEU_ANGEMELDET", melden: false);
        var s = new AdminSitzung(v.KontoId, v.GeraetId, jetzt);
        _adminSitzungen[v.Id] = s;
        Vermerken("ADMIN_SITZUNG", v.KontoId, v.KontoId, $"Admin-Sitzung von {KontoText(v.KontoId)} geöffnet, Gerät {GeraetText(v.GeraetId)}"
            + (r.NotfallcodeBenutzt ? " mit Notfallcode" : ""));
        Log.Info("Admin-Sitzung geöffnet: " + Log.Schwaerzung.Konto(v.KontoId));
        if (r.NeuesGeraet)
        {
            WarnungSenden(S.MeldungsArten.AdminSitzung, v.KontoId, Antwort.DetailsBauen(w =>
            {
                w.WriteString("op", "BEGONNEN");
                w.WriteBoolean("neuesGeraet", true);
                w.WriteString("geraet", GeraetText(v.GeraetId));
            }));
        }
        return Antwort.Mit(new S.AdminSitzungAntwort
        {
            Bis = s.Bis.ToUnixTimeMilliseconds(),
            BestaetigtBis = s.BestaetigtBis.ToUnixTimeMilliseconds(),
            UhrHinweis = r.UhrHinweis ? true : null,
            NotfallcodesUebrig = r.NotfallcodeBenutzt ? (byte)r.NotfallcodesUebrig : null,
        });
    }

    private Antwort AdminBestaetigen(Verbindung v, S.AdminBestaetigenAnfrage a)
    {
        // Nur in einer offenen Admin-Sitzung; außerhalb zählt eine Bestätigung nicht und ist kein Weg, Codes zu probieren.
        if (AdminSitzungVon(v) is not { } s) return Antwort.Fehl(S.Fehlercode.AdminSitzungNoetig);
        var r = Admins.Bestaetigen(v.KontoId, a.Code, a.Notfallcode);
        ZweitfaktorVerarbeiten(r, v.KontoId, "Bestätigung");
        if (!r.Ok) return ZweitfaktorFehler(r);
        var jetzt = JetztUtc;
        s.BestaetigtBis = jetzt + Bestaetigungsdauer;
        s.LetzteHandlung = jetzt;
        Vermerken("SCHRITT_BESTAETIGT", v.KontoId, v.KontoId, $"Schritt-Bestätigung von {KontoText(v.KontoId)}" + (r.NotfallcodeBenutzt ? " mit Notfallcode" : ""));
        return Antwort.Mit(new S.AdminSitzungAntwort
        {
            Bis = s.Bis.ToUnixTimeMilliseconds(),
            BestaetigtBis = s.BestaetigtBis.ToUnixTimeMilliseconds(),
            UhrHinweis = r.UhrHinweis ? true : null,
            NotfallcodesUebrig = r.NotfallcodeBenutzt ? (byte)r.NotfallcodesUebrig : null,
        });
    }

    private Antwort AdminAbmelden(Verbindung v)
    {
        if (!_adminSitzungen.ContainsKey(v.Id)) return Antwort.Fehl(S.Fehlercode.AdminSitzungNoetig);
        AdminSitzungBeenden(v, "ABGEMELDET", melden: false);
        return Antwort.Leer();
    }

    private Antwort AdminEinrichtung(Verbindung v, S.AdminEinrichtungAnfrage a)
    {
        if (Admins.Eintrag(v.KontoId) is null) return Antwort.Fehl(S.Fehlercode.KeinRecht);
        if (Nachweis(v, a.Unterschrift) is not { } nachweis) return Antwort.Fehl(S.Fehlercode.Ungueltig, "Nonce fehlt oder abgelaufen");
        switch (a.Op)
        {
            case S.AdminEinrichtungOperation.Antrag:
            {
                var r = Admins.AntragStellen(v.KontoId, Convert.FromBase64String(a.Schluessel!), nachweis);
                if (!r.Ok) return Antwort.Fehl(Abbildung.Fehler(r.Fehler));
                Vermerken("ADMIN_ANTRAG", v.KontoId, v.KontoId, $"Admin-Schlüssel beantragt von {KontoText(v.KontoId)}, Kennung {r.Kennung}, Gerät {GeraetText(v.GeraetId)}");
                // Jeder Antrag ist eine Meldung an den Eigentümer (3.9, Admin ernennen Schritt 2).
                WarnungSenden(S.MeldungsArten.AdminGeaendert, v.KontoId, Antwort.DetailsBauen(w =>
                {
                    w.WriteString("op", "ANTRAG");
                    w.WriteString("kennung", r.Kennung);
                }));
                return Antwort.Mit(new S.AdminEinrichtungAntwort { Kennung = r.Kennung });
            }
            case S.AdminEinrichtungOperation.GeheimnisAbholen:
            {
                var r = Admins.GeheimnisAbholen(v.KontoId, nachweis);
                if (r.TresorDefekt) Log.Fehler("Zweitfaktor", new CryptographicException("TOTP-Geheimnis ließ sich nicht entschlüsseln (funk-totp-schluessel getauscht?)"));
                if (!r.Ok || r.Geheimnis is not { } geheimnis) return r.Fehler == I.Fehlercode.Frist
                    ? Antwort.Fehl(S.Fehlercode.Ungueltig, "Einrichtung abgelaufen: der Eigentümer führt admin-einrichtung-neu aus")
                    : Antwort.Fehl(r.TresorDefekt ? S.Fehlercode.Intern : Abbildung.Fehler(r.Fehler));
                try
                {
                    Vermerken("ZWEITFAKTOR_ABGEHOLT", v.KontoId, v.KontoId, $"TOTP-Geheimnis abgeholt von {KontoText(v.KontoId)}, Gerät {GeraetText(v.GeraetId)}");
                    return Antwort.Mit(new S.AdminEinrichtungAntwort
                    {
                        Geheimnis = I.Base32.Kodieren(geheimnis),
                        Uri = I.Totp.OtpauthUri(OtpauthName(v.KontoId), geheimnis),
                        GueltigBis = r.AbholbarBis?.ToUnixTimeMilliseconds(),
                    });
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(geheimnis);
                }
            }
            default:
            {
                var r = Admins.EinrichtungBestaetigen(v.KontoId, nachweis, a.Code!);
                ZweitfaktorVerarbeiten(r, v.KontoId, "Einrichtung");
                if (!r.Ok) return r.Fehler == I.Fehlercode.Frist
                    ? Antwort.Fehl(S.Fehlercode.Ungueltig, "Einrichtung abgelaufen: der Eigentümer führt admin-einrichtung-neu aus")
                    : ZweitfaktorFehler(r);
                Vermerken("ZWEITFAKTOR_EINGERICHTET", v.KontoId, v.KontoId, $"Zweitfaktor von {KontoText(v.KontoId)} eingerichtet");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, v.KontoId, Antwort.DetailsBauen(w => w.WriteString("op", "EINGERICHTET")));
                KontoGeaendert(v.KontoId);
                return Antwort.Mit(new S.AdminEinrichtungAntwort { Notfallcodes = r.Notfallcodes });
            }
        }
    }

    /// <summary>Folgen eines Code-Versuchs: Journal, bei Pause oder Sperre eine Warnung an alle Admins, bei Notfallcode auch.</summary>
    private void ZweitfaktorVerarbeiten(I.Adminergebnis r, uint kontoId, string wobei)
    {
        if (r.TresorDefekt) Log.Fehler("Zweitfaktor", new CryptographicException("TOTP-Geheimnis ließ sich nicht entschlüsseln (funk-totp-schluessel getauscht?)"));
        if (r.Fehlversuch) Vermerken("ZWEITFAKTOR_FEHLVERSUCH", kontoId, kontoId, $"Fehlversuch ({wobei}) von {KontoText(kontoId)}");
        if (r.Gesperrt)
        {
            var dauerhaft = r.Bis is null;
            Vermerken("ZWEITFAKTOR_GESPERRT", kontoId, kontoId, dauerhaft
                ? $"Zweitfaktor von {KontoText(kontoId)} nach zu vielen Fehlversuchen gesperrt, bis der Eigentümer entsperrt"
                : $"Zweitfaktor von {KontoText(kontoId)} 15 min pausiert (5 Fehlversuche)");
            Log.Warnung("Zweitfaktor " + (dauerhaft ? "gesperrt" : "pausiert") + ": " + Log.Schwaerzung.Konto(kontoId));
            WarnungSenden(S.MeldungsArten.ZweitfaktorGesperrt, kontoId, Antwort.DetailsBauen(w =>
            {
                if (r.Bis is { } bis) w.WriteNumber("bis", bis.ToUnixTimeMilliseconds());
            }));
        }
        if (r.NotfallcodeBenutzt)
        {
            Vermerken("NOTFALLCODE", kontoId, kontoId, $"Notfallcode von {KontoText(kontoId)} benutzt ({wobei}), {r.NotfallcodesUebrig} übrig");
            WarnungSenden(S.MeldungsArten.Notfallcode, kontoId, Antwort.DetailsBauen(w => w.WriteNumber("uebrig", r.NotfallcodesUebrig)));
        }
    }

    private static Antwort ZweitfaktorFehler(I.Adminergebnis r)
    {
        if (r.TresorDefekt) return Antwort.Fehl(S.Fehlercode.Intern);
        if (r.Fehler == I.Fehlercode.ZweitfaktorGesperrt)
            return Antwort.Fehl(S.Fehlercode.ZweitfaktorGesperrt, r.Bis is null ? "gesperrt, bis der Eigentümer an der Konsole entsperrt" : null,
                r.Bis is { } bis ? Antwort.DetailsBauen(w => w.WriteNumber("bis", bis.ToUnixTimeMilliseconds())) : null);
        return Antwort.Fehl(Abbildung.Fehler(r.Fehler));
    }

    /// <summary><c>SCHRITT_BESTAETIGEN</c> mit dem Ende der Admin-Sitzung in <c>details.bis</c>; der Client fragt einen Code und sendet erneut.</summary>
    private Antwort SchrittBestaetigen(Verbindung v) =>
        Antwort.Fehl(S.Fehlercode.SchrittBestaetigen, "Code bestätigen",
            _adminSitzungen.TryGetValue(v.Id, out var a) ? Antwort.DetailsBauen(w => w.WriteNumber("bis", a.Bis.ToUnixTimeMilliseconds())) : null);

    /// <summary>Fehler eines Moduls; SCHRITT_BESTAETIGEN bekommt das Ende der Admin-Sitzung dazu.</summary>
    private Antwort ModulFehler(Verbindung v, I.Fehlercode f) =>
        f == I.Fehlercode.SchrittBestaetigen ? SchrittBestaetigen(v) : Antwort.Fehl(Abbildung.Fehler(f));

    // ---------------------------------------------------------------- Konsole (3.10): nur der Eigentümer am Server

    /// <summary>Urheber "Konsole" im Journal.</summary>
    internal const uint KonsoleDurch = 0;

    /// <summary>
    /// Befehle des Eigentümers an der Konsole (3.10): Eigentümer setzen, Admins vorsehen, ernennen (mit vorgelesener Kennung),
    /// Zweitfaktor neu einrichten (auf Wunsch mit otpauth-URI zum Abtippen), erster Code an der Konsole, sperren, entsperren,
    /// zurücksetzen, entziehen, Schlüssel entfernen, Notfallcodes, Liste. Über das Funkprotokoll führt kein Weg hierher.
    /// </summary>
    private VerwaltungsAntwort AdminKonsole(string befehl, System.Text.Json.Nodes.JsonObject a)
    {
        if (befehl == "admins")
        {
            var liste = new System.Text.Json.Nodes.JsonArray();
            var sb = new System.Text.StringBuilder();
            foreach (var e in Admins.Stand.Eintraege.Values.OrderBy(e => e.KontoId))
            {
                liste.Add(AdminAlsJson(e));
                sb.Append(AdminZeile(KontoText(e.KontoId), e, JetztUtc)).Append('\n');
            }
            return VerwaltungsAntwort.Gut(liste.Count == 0 ? "Keine Admins eingetragen" : sb.ToString().TrimEnd(), liste);
        }
        if (Zahl(a, "konto") is not { } konto) return VerwaltungsAntwort.Fehler("konto fehlt");
        if (!Konten.Stand.Konten.TryGetValue(konto, out var k) || k.IstAntrag) return VerwaltungsAntwort.Fehler("Konto unbekannt");
        var wer = KontoText(konto);
        switch (befehl)
        {
            case "eigentuemer":
            {
                var e = Konten.EigentuemerSetzen(I.Akteur.AdminWerkzeug, konto);
                if (!e.Ok) return VerwaltungsAntwort.Fehler(e.Fehler == I.Fehlercode.Ungueltig ? "Eigentümer kann nur ein ADMIN-Konto sein" : I.Drahtnamen.Drahtname(e.Fehler));
                Vermerken("EIGENTUEMER", KonsoleDurch, konto, $"Konsole: Eigentümer ist jetzt {wer}");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "EIGENTUEMER")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut("Eigentümer: " + wer);
            }
            case "admin-vorsehen":
                Admins.Vorsehen(konto);
                Vermerken("ADMIN_VORGESEHEN", KonsoleDurch, konto, $"Konsole: {wer} als Admin vorgesehen");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "VORGESEHEN")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut(wer + " ist vorgesehen. Im Client mit Verwaltungsteil jetzt 'Verwaltung einrichten'; "
                    + "danach die vorgelesene Kennung mit admin-ernennen bestätigen.");
            case "admin-ernennen":
            {
                if (Text(a, "kennung") is not { } kennung) return VerwaltungsAntwort.Fehler("kennung fehlt (XXXX-XXXX-XXXX-XXXX, vom Admin vorgelesen)");
                var r = Admins.Ernennen(konto, kennung);
                if (!r.Ok)
                {
                    return VerwaltungsAntwort.Fehler(r.Fehler switch
                    {
                        I.Fehlercode.Unbekannt => "kein offener Antrag: erst admin-vorsehen, dann im Client 'Verwaltung einrichten'",
                        I.Fehlercode.Ungueltig => "Kennung passt nicht zum Antrag (oder der Schlüssel ist schon eingetragen). Nichts geändert.",
                        I.Fehlercode.Grenze => "schon drei Admin-Schlüssel eingetragen; erst einen entfernen",
                        _ => I.Drahtnamen.Drahtname(r.Fehler),
                    });
                }
                if (k.Stufe != I.Stufe.Admin)
                {
                    var s = Konten.StufeSetzen(I.Akteur.AdminWerkzeug, konto, I.Stufe.Admin);
                    if (s.Ok) WirkungenAusfuehren(s.Wirkungen);
                }
                Vermerken("ADMIN_ERNANNT", KonsoleDurch, konto, $"Konsole: {wer} zum Admin ernannt, Schlüssel {r.Kennung}");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w =>
                {
                    w.WriteString("op", "ERNANNT");
                    w.WriteString("kennung", r.Kennung);
                }));
                KontoGeaendert(konto);
                var bis = r.AbholbarBis is { } ab ? ab.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC" : "";
                return VerwaltungsAntwort.Gut(r.EinrichtungBegonnen
                    ? $"{wer} ist Admin (Schlüssel {r.Kennung}). Der Client holt das TOTP-Geheimnis bis {bis} ab und zeigt den QR-Code; danach den ersten Code eingeben."
                    : $"Schlüssel {r.Kennung} für {wer} eingetragen.");
            }
            case "admin-einrichtung-neu":
            {
                var anzeigen = a["anzeigen"] is System.Text.Json.Nodes.JsonValue av && av.TryGetValue<bool>(out var an) && an;
                var r = Admins.EinrichtungNeu(konto, anzeigen);
                if (!r.Ok) return VerwaltungsAntwort.Fehler(r.Fehler == I.Fehlercode.Ungueltig ? "noch kein Admin-Schlüssel: erst admin-ernennen" : "kein Admin-Nachweis für dieses Konto");
                AdminSitzungenVonKontoBeenden(konto, "ZWEITFAKTOR_NEU");
                Vermerken("ZWEITFAKTOR_NEU", KonsoleDurch, konto, $"Konsole: Zweitfaktor von {wer} neu eingerichtet" + (anzeigen ? " (an der Konsole angezeigt)" : ""));
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "ZWEITFAKTOR_NEU")));
                KontoGeaendert(konto);
                if (r.Geheimnis is not { } geheimnis)
                    return VerwaltungsAntwort.Gut($"Neues Geheimnis für {wer} erzeugt. Der Client holt es bis {r.AbholbarBis:HH:mm} UTC ab (Verwaltung einrichten).");
                try
                {
                    var uri = I.Totp.OtpauthUri(OtpauthName(konto), geheimnis);
                    var gruppiert = I.Totp.Gruppiert(geheimnis);
                    return VerwaltungsAntwort.Gut(
                        $"Zweitfaktor für {wer}: in der Authenticator-App 'Manuell eingeben' und diesen Schlüssel abtippen oder den Link als QR-Code scannen.\n"
                        + $"Schlüssel: {gruppiert}\nLink: {uri}\nKein Bildschirmfoto. Danach den ersten Code: zweitfaktor-bestaetigen --konto {konto} --code <6 Ziffern>",
                        new System.Text.Json.Nodes.JsonObject { ["uri"] = uri, ["geheimnis"] = gruppiert });
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(geheimnis);
                }
            }
            case "zweitfaktor-bestaetigen":
            {
                if (Text(a, "code") is not { } code) return VerwaltungsAntwort.Fehler("code fehlt");
                var r = Admins.EinrichtungBestaetigenKonsole(konto, code);
                ZweitfaktorVerarbeiten(r, konto, "Einrichtung an der Konsole");
                if (!r.Ok) return VerwaltungsAntwort.Fehler(r.Fehler == I.Fehlercode.Frist ? "Einrichtung abgelaufen: admin-einrichtung-neu" : I.Drahtnamen.Drahtname(r.Fehler));
                Vermerken("ZWEITFAKTOR_EINGERICHTET", KonsoleDurch, konto, $"Konsole: Zweitfaktor von {wer} eingerichtet");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "EINGERICHTET")));
                KontoGeaendert(konto);
                return NotfallcodesAntwort(wer, r.Notfallcodes!);
            }
            case "notfallcodes":
            {
                var r = Admins.NotfallcodesErzeugen(konto);
                if (!r.Ok) return VerwaltungsAntwort.Fehler("Zweitfaktor nicht eingerichtet");
                Vermerken("NOTFALLCODES_NEU", KonsoleDurch, konto, $"Konsole: neue Notfallcodes für {wer}, die alten gelten nicht mehr");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "NOTFALLCODES")));
                return NotfallcodesAntwort(wer, r.Notfallcodes!);
            }
            case "admin-sperren":
            {
                if (!Admins.Sperren(konto).Ok) return VerwaltungsAntwort.Fehler("kein Admin-Nachweis für dieses Konto");
                AdminSitzungenVonKontoBeenden(konto, "GESPERRT");
                Vermerken("ADMIN_GESPERRT", KonsoleDurch, konto, $"Konsole: Admin-Nachweis von {wer} gesperrt");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "GESPERRT")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut("Admin-Nachweis von " + wer + " gesperrt; offene Admin-Sitzungen sind beendet.");
            }
            case "admin-entsperren":
            {
                if (!Admins.Entsperren(konto).Ok) return VerwaltungsAntwort.Fehler("kein Admin-Nachweis für dieses Konto");
                Vermerken("ADMIN_ENTSPERRT", KonsoleDurch, konto, $"Konsole: Admin-Nachweis von {wer} entsperrt, Fehlversuche gelöscht");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "ENTSPERRT")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut("Admin-Nachweis von " + wer + " entsperrt.");
            }
            case "admin-zuruecksetzen":
            {
                if (!Admins.Zuruecksetzen(konto).Ok) return VerwaltungsAntwort.Fehler("kein Admin-Nachweis für dieses Konto");
                AdminSitzungenVonKontoBeenden(konto, "ZURUECKGESETZT");
                Vermerken("ADMIN_ZURUECKGESETZT", KonsoleDurch, konto, $"Konsole: Admin-Nachweis von {wer} zurückgesetzt (Schlüssel, Geheimnis, Notfallcodes fort)");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "ZURUECKGESETZT")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut(wer + " ist zurückgesetzt und wieder vorgesehen; neu einrichten wie beim ersten Mal.");
            }
            case "admin-entziehen":
            {
                if (StufeAusText(Text(a, "stufe") ?? "RAIDLEITER") is not { } stufe || stufe == I.Stufe.Admin)
                    return VerwaltungsAntwort.Fehler("stufe: RAIDLEITER oder FUNKER");
                if (Admins.Eintrag(konto) is null && k.Stufe != I.Stufe.Admin) return VerwaltungsAntwort.Fehler(wer + " ist kein Admin");
                // Vor jeder Änderung: sonst wäre der Nachweis fort, die Stufe aber noch ADMIN (3.10).
                if (k.Eigentuemer) return EigentuemerBleibtAdmin();
                if (Admins.Eintrag(konto) is not null) Admins.Entziehen(konto);
                AdminSitzungenVonKontoBeenden(konto, "ENTZOGEN");
                if (k.Stufe == I.Stufe.Admin)
                {
                    var s = Konten.StufeSetzen(I.Akteur.AdminWerkzeug, konto, stufe);
                    if (!s.Ok) return VerwaltungsAntwort.Fehler(I.Drahtnamen.Drahtname(s.Fehler));
                    WirkungenAusfuehren(s.Wirkungen);
                }
                Vermerken("ADMIN_ENTZOGEN", KonsoleDurch, konto, $"Konsole: {wer} ist kein Admin mehr, Stufe {I.Rechteregeln.Drahtname(stufe)}");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "ENTZOGEN")));
                KontoGeaendert(konto);
                return VerwaltungsAntwort.Gut(wer + " ist kein Admin mehr (Stufe " + I.Rechteregeln.Drahtname(stufe) + ").");
            }
            default:
            {
                if (Text(a, "kennung") is not { } kennung) return VerwaltungsAntwort.Fehler("kennung fehlt");
                var r = Admins.SchluesselEntfernen(konto, kennung);
                if (!r.Ok) return VerwaltungsAntwort.Fehler("kein eingetragener Schlüssel mit dieser Kennung");
                AdminSitzungenVonKontoBeenden(konto, "SCHLUESSEL_ENTFERNT");
                Vermerken("ADMIN_SCHLUESSEL_ENTFERNT", KonsoleDurch, konto, $"Konsole: Admin-Schlüssel {r.Kennung} von {wer} entfernt");
                WarnungSenden(S.MeldungsArten.AdminGeaendert, konto, Antwort.DetailsBauen(w => w.WriteString("op", "SCHLUESSEL_ENTFERNT")));
                return VerwaltungsAntwort.Gut("Schlüssel " + r.Kennung + " entfernt.");
            }
        }
    }

    private static VerwaltungsAntwort NotfallcodesAntwort(string wer, IReadOnlyList<string> codes) =>
        VerwaltungsAntwort.Gut($"Notfallcodes für {wer}, nur jetzt sichtbar. Getrennt vom Telefon aufbewahren (sichere Notiz oder Papier); jeder gilt einmal:\n  "
            + string.Join("\n  ", codes),
            new System.Text.Json.Nodes.JsonArray([.. codes.Select(c => (System.Text.Json.Nodes.JsonNode)c)]));

    /// <summary>Beendet jede Admin-Sitzung eines Kontos sofort (Sperre, Entzug, neuer Zweitfaktor).</summary>
    private void AdminSitzungenVonKontoBeenden(uint kontoId, string grund)
    {
        foreach (var id in _adminSitzungen.Where(p => p.Value.KontoId == kontoId).Select(p => p.Key).ToList())
        {
            if (_verbindungen.TryGetValue(id, out var v)) AdminSitzungBeenden(v, grund);
            else _adminSitzungen.Remove(id);
        }
    }

    // ---------------------------------------------------------------- Texte für Journal und Konsole

    /// <summary>Journal-Eintrag mit der Wanduhr des Servers.</summary>
    internal void Vermerken(string art, uint durch, uint? kontoId, string text) =>
        Journal.Eintragen(JetztUtc.ToUnixTimeMilliseconds(), art, durch, kontoId, text);

    /// <summary>"Mustermann (Konto 7)" für Journal und Konsole; Klarnamen stehen nur dort, nie im Logbuch.</summary>
    internal string KontoText(uint kontoId)
    {
        if (!Konten.Stand.Konten.TryGetValue(kontoId, out var k)) return "Konto " + kontoId.ToString(CultureInfo.InvariantCulture);
        return (k.Hauptname ?? k.Name) + " (Konto " + kontoId.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private string GeraetText(uint geraetId) =>
        Konten.Stand.Geraete.TryGetValue(geraetId, out var g) ? $"{g.Name} ({g.Anzeige})" : "Gerät " + geraetId.ToString(CultureInfo.InvariantCulture);

    private string OtpauthName(uint kontoId) =>
        Konten.Stand.Konten.TryGetValue(kontoId, out var k) ? k.Hauptname ?? k.Name : kontoId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Drahtform des Admin-Zustands für das eigene Konto (Welcome, Mitgliedschaften).</summary>
    internal S.AdminStatus? AdminStatusVon(uint kontoId) => Admins.Eintrag(kontoId)?.Zustand switch
    {
        I.AdminZustand.Vorgesehen => S.AdminStatus.Vorgesehen,
        I.AdminZustand.Einrichtung => S.AdminStatus.Einrichtung,
        I.AdminZustand.Aktiv => S.AdminStatus.Aktiv,
        I.AdminZustand.Gesperrt => S.AdminStatus.Gesperrt,
        _ => null,
    };

    /// <summary>
    /// Zeichen neben dem Namen (A49), vom Server abgeleitet, nie vom Client: ADMIN für einen eingerichteten Admin, FUEHRUNG für
    /// die Stufe RAIDLEITER. Grafiken kommen aus dem Client; der Server schickt nur Kennungen.
    /// </summary>
    internal List<string> ZeichenVon(I.Konto k)
    {
        var liste = new List<string>(3);
        if (k.Stufe == I.Stufe.Admin && Admins.IstEingerichtet(k.KontoId)) liste.Add(Zeichen.Admin);
        if (k.Stufe == I.Stufe.Raidleiter) liste.Add(Zeichen.Fuehrung);
        // Stern für einen laufenden Auftrag (6.12, A49); ein geplanter zeigt noch nichts.
        if (Schreiber.Aktuell.Konten.TryGetValue(k.KontoId, out var l) && l.Beauftragung is { } b && b.GiltZu(Zeit.LageMs())) liste.Add(Zeichen.Einsatzleitung);
        return liste;
    }

    /// <summary>Kennungen der Zeichen (A49); die Grafiken zeichnet der Client selbst.</summary>
    public static class Zeichen
    {
        public const string Admin = "ADMIN";
        public const string Fuehrung = "FUEHRUNG";

        /// <summary>Für einen laufenden Auftrag der Einsatzleitung (6.12, seit 0.3.0).</summary>
        public const string Einsatzleitung = "EINSATZLEITUNG";
    }

    /// <summary>Eigene Kontoangaben für Mitgliedschaften (F3, F13).</summary>
    internal S.KontoStand? KontoStandVon(uint kontoId)
    {
        if (!Konten.Stand.Konten.TryGetValue(kontoId, out var k)) return null;
        var m = Mitglieder.Stand;
        var d = m.DatenVon(kontoId);
        var e = Admins.Eintrag(kontoId);
        return new S.KontoStand
        {
            Rechte = Abbildung.Rechte(Abbildung.RechtLage(k.Rechte)),
            ScNamen = [.. k.ScNamen],
            DiscordName = k.DiscordName,
            Admin = AdminStatusVon(kontoId),
            AdminAntrag = e is { AntragErlaubt: true } ? true : null,
            Eigentuemer = k.Eigentuemer ? true : null,
            Rollen = [.. d.Rollen],
            Lieblingsrollen = [.. d.Lieblingsrollen],
            ErsteWahl = d.ErsteWahl,
            Zeichen = ZeichenVon(k),
            ZeichenZeigen = d.ZeichenZeigen,
            KarteFreigeben = d.KarteFreigeben,
            Beauftragung = Abbildung.BeauftragungAngabe(Schreiber.Aktuell.Konten.GetValueOrDefault(kontoId), Zeit.LageMs()),
            AlsFunkerBis = AlsFunkerBisVon(kontoId),
            // Abzeichen (0.11.7): alle eigenen, auch ausgeblendete; dazu die eigene Wahl für vorn und die ausgeblendeten.
            Abzeichen = [.. S.Abzeichenkatalog.Ordnen(d.AlleAbzeichen().Where(S.Abzeichenkatalog.Kennt))],
            AbzeichenVorn = d.AbzeichenVorn,
            AbzeichenAus = [.. d.AbzeichenAus],
            // Meldesystem (0.12.0): der eigene Haken; ob er wirkt, rechnet der Client mit Stufe, Auftrag und Funker-Modus (S.Melderecht).
            MeldungenSenden = d.MeldungenSenden ? true : null,
        };
    }

    /// <summary>Die Zeichen (A49), die andere sehen: ohne die ausgeblendeten automatischen Zeichen (0.11.7).</summary>
    internal List<string> SichtbareZeichen(I.Konto k, I.Mitgliedsdaten d) =>
        [.. ZeichenVon(k).Where(z => !d.AbzeichenAus.Contains(z))];

    /// <summary>Bis wann das Konto "Heute als Funker" spielt (6.15), <c>null</c>, wenn der Modus nicht läuft.</summary>
    internal long? AlsFunkerBisVon(uint kontoId) =>
        Schreiber.Aktuell.Konten.TryGetValue(kontoId, out var l) && l.AlsFunkerZu(Zeit.LageMs()) ? l.AlsFunkerBis : null;

    /// <summary>
    /// Hauptname, Discord-Name und Zeichen für Praesenz (F4, F12, Antwort 3); ohne Zustimmung des Inhabers keine Zeichen.
    /// <c>Freigegeben</c> sagt, ob Zeichen und Abzeichen auch Empfänger ohne <c>ZUWEISEN</c> sehen.
    /// </summary>
    internal Abbildung.Praesenzzusatz PraesenzZusatz(I.Konto? k)
    {
        if (k is null) return default;
        var m = Mitglieder.Stand;
        var d = m.DatenVon(k.KontoId);
        if (!d.ZeichenZeigen) return new(k.Hauptname, k.DiscordName, null, d.KarteFreigeben);
        // Abzeichen (0.11.7): nur Sichtbares; vorn nur, wenn er es besitzt und nicht ausgeblendet hat.
        var zeichen = SichtbareZeichen(k, d);
        var abzeichen = I.Mitgliederbuch.SichtbareAbzeichen(d);
        var vorn = d.AbzeichenVorn is { } gewaehlt && (zeichen.Contains(gewaehlt) || abzeichen.Contains(gewaehlt)) ? gewaehlt : null;
        return new(k.Hauptname, k.DiscordName, zeichen.Count == 0 ? null : zeichen, d.KarteFreigeben)
        {
            Abzeichen = abzeichen.IsEmpty ? null : [.. abzeichen],
            AbzeichenVorn = vorn,
        };
    }

    /// <summary>
    /// Eine Zeile von <c>admins</c>: Zustand, Schlüssel, Antrag, Notfallcodes, Sperre, dazu Fehlversuche der letzten 24 h und eine
    /// laufende Pause (BETRIEB.md verspricht beides; bis 0.2.0 standen sie nur in <c>--json-aus</c>).
    /// </summary>
    internal static string AdminZeile(string wer, I.AdminEintrag e, DateTimeOffset jetzt)
    {
        var antrag = e.AntragSpki is not null ? "  Antrag offen" : e.AntragErlaubt ? "  Antrag erlaubt" : "";
        var sperre = e.Sperrgrund is { } g ? "  gesperrt: " + g : "";
        var fehl = e.Fehlversuche.Length > 0 ? string.Create(CultureInfo.InvariantCulture, $"  Fehlversuche {e.Fehlversuche.Length} (24 h)") : "";
        var pause = e.PauseBis is { } bis && bis > jetzt ? "  Pause bis " + bis.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC" : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{wer,-30} {e.Zustand.ToString().ToUpperInvariant(),-11} Schlüssel {string.Join(", ", e.Schluessel.Select(s => s.Kennung))}{antrag}  Notfallcodes {e.Notfallcodes.Length}{sperre}{fehl}{pause}");
    }

    /// <summary>JSON-Wert für Konsole und Tests: der Admin-Nachweis ohne Geheimnis, Chiffrat oder Prüfwerte.</summary>
    internal static System.Text.Json.Nodes.JsonObject AdminAlsJson(I.AdminEintrag e) => new()
    {
        ["kontoId"] = e.KontoId,
        ["zustand"] = e.Zustand.ToString().ToUpperInvariant(),
        ["antragErlaubt"] = e.AntragErlaubt,
        ["antragOffen"] = e.AntragSpki is not null,
        ["schluessel"] = new System.Text.Json.Nodes.JsonArray([.. e.Schluessel.Select(s => (System.Text.Json.Nodes.JsonNode)s.Kennung)]),
        ["eingerichtet"] = e.Eingerichtet?.ToUnixTimeMilliseconds(),
        ["notfallcodes"] = e.Notfallcodes.Length,
        ["fehlversuche"] = e.Fehlversuche.Length,
        ["pauseBis"] = e.PauseBis?.ToUnixTimeMilliseconds(),
        ["sperrgrund"] = e.Sperrgrund,
        ["zuletztBenutzt"] = e.ZuletztBenutzt?.ToUnixTimeMilliseconds(),
    };
}
