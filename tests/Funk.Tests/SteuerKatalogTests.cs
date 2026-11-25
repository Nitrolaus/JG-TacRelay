// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>Nachrichtenkatalog 4.4/4.5, Fehlercodes 4.9, Drahtnamen der Aufzählungen, Versionierung 4.7.</summary>
internal static class SteuerKatalogTests
{
    private static string Drahtnamen<T>() where T : struct, Enum
    {
        var konverter = new StrengeAufzaehlung<T>();
        var namen = new List<string>();
        foreach (var w in Enum.GetValues<T>())
        {
            using var strom = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(strom))
            {
                konverter.Write(writer, w, SteuerJson.Optionen);
            }
            namen.Add(Encoding.UTF8.GetString(strom.ToArray()).Trim('"'));
        }
        return string.Join(',', namen);
    }

    [Test]
    public static void Katalog_Typnummern4_4()
    {
        var erwartet = new Dictionary<ushort, string>
        {
            [0x0001] = "Hello", [0x0002] = "Welcome", [0x0003] = "Bye", [0x0004] = "Ping", [0x0006] = "Fehler",
            [0x0010] = "Registrierung", [0x0011] = "GeraetStatus", [0x0012] = "GeraeteListe", [0x0013] = "GeraetEntscheiden",
            [0x0014] = "GeraetEntfernen", [0x0015] = "KontoVerwaltung", [0x0016] = "VerknuepfungBestaetigen", [0x0017] = "Beauftragung",
            [0x0018] = "ProfilSetzen", [0x0019] = "Journal", [0x001A] = "Rollenkatalog", [0x001B] = "Mitglied", [0x001C] = "Lieblingsrollen",
            [0x001D] = "Untergilde", [0x001E] = "Mitgliedskarte", [0x001F] = "AntraegeAblehnen",
            [0x0090] = "AdminNonce", [0x0091] = "AdminAnmeldung", [0x0092] = "AdminBestaetigen", [0x0093] = "AdminAbmelden", [0x0094] = "AdminEinrichtung",
            [0x0020] = "Zustand", [0x0021] = "NetzListe", [0x0022] = "Mitgliedschaften", [0x0023] = "Praesenz", [0x0024] = "SendeFehler", [0x0025] = "Abzeichen",
            [0x0026] = "MeldungSenden", [0x0027] = "BereitMelden",
            [0x0030] = "SendeauswahlSetzen", [0x0031] = "HoerenSetzen", [0x0032] = "EinsteigenRolleSchiff", [0x0033] = "Verschieben",
            [0x0034] = "Stummschalten", [0x0035] = "FeldSetzen", [0x0036] = "SatzAnwenden", [0x0037] = "PlaetzeFreigeben", [0x0038] = "KontenVerzeichnis",
            [0x0039] = "RolleWaehlen", [0x003A] = "KanalSperre", [0x003C] = "Funkermodus", [0x003D] = "PhaseSchalten",
            [0x0040] = "AufstellungSpeichern", [0x0041] = "AufstellungListe", [0x0042] = "AufstellungAnwenden", [0x0043] = "AufstellungLoeschen",
            [0x0044] = "RaidHelperImport", [0x0045] = "RegelTabelle", [0x0046] = "RaidHelperEvents", [0x0047] = "AufstellungGeaendert",
            [0x0050] = "DurchsageStart", [0x0051] = "DurchsageEnde", [0x0052] = "SprechanzeigeAbo", [0x0053] = "Sprechanzeige",
            [0x0060] = "PegelVoreinstellungen", [0x0061] = "PegelSetzen", [0x0062] = "Ablage",
            [0x0071] = "KeyRotation", [0x0072] = "UdpStatus", [0x0073] = "TunnelModus", [0x0074] = "EmpfangsBericht", [0x0075] = "VerlustHinweis",
            [0x0080] = "Verwaltung", [0x4001] = "ServerLast", [0x4002] = "Meldung", [0x4003] = "Belegung", [0x4004] = "PhaseGeschaltet",
            [0x4005] = "Meldestand", [0x4006] = "BereitStand",
            [0x8001] = "SpracheTunnel", [0x8002] = "VoiceKeys",
        };
        Pruefe.Gleich(76, erwartet.Count, "Zeilen in 4.4 (seit 0.12.0 mit dem Meldesystem)");
        Pruefe.Gleich(76, Katalog.Alle.Count(), "Katalog");
        // Neue Pushes liegen im optionalen Bereich, damit ältere Clients sie übergehen (4.7); neue Anfragen stellen nur neue Clients.
        Pruefe.Wahr(Typbereiche.IstOptional((ushort)Nachrichtentyp.Belegung), "Belegung optional");
        Pruefe.Wahr(Typbereiche.IstOptional((ushort)Nachrichtentyp.PhaseGeschaltet), "PhaseGeschaltet optional: Clients 0.4.x übergehen ihn");
        Pruefe.Falsch(Typbereiche.IstOptional((ushort)Nachrichtentyp.Funkermodus) || Typbereiche.IstOptional((ushort)Nachrichtentyp.PhaseSchalten),
            "Funkermodus und PhaseSchalten sind Pflichtanfragen (ein Server 0.4.x trennt, Versionen.ServerFassungFuer)");
        Pruefe.Falsch(Typbereiche.IstOptional((ushort)Nachrichtentyp.RolleWaehlen) || Typbereiche.IstOptional((ushort)Nachrichtentyp.KanalSperre),
            "RolleWaehlen und KanalSperre sind Pflichtanfragen (ein Server 0.5.x trennt, Versionen.ServerFassungFuer)");
        Pruefe.Wahr(Typbereiche.IstOptional((ushort)Nachrichtentyp.Meldestand) && Typbereiche.IstOptional((ushort)Nachrichtentyp.BereitStand),
            "Meldestand und BereitStand optional: Clients bis 0.11.9 übergehen sie");
        Pruefe.Falsch(Typbereiche.IstOptional((ushort)Nachrichtentyp.MeldungSenden) || Typbereiche.IstOptional((ushort)Nachrichtentyp.BereitMelden),
            "MeldungSenden und BereitMelden sind Pflichtanfragen (ein Server 0.11.x trennt, Versionen.ServerFassungFuer)");
        foreach (var (nr, name) in erwartet)
        {
            var e = Katalog.Finden(nr);
            Pruefe.Wahr(e is not null, $"0x{nr:X4} fehlt");
            Pruefe.Gleich(name, e!.Typ.ToString(), $"0x{nr:X4}");
        }
        Pruefe.Wahr(Katalog.Finden(0x0070) is null, "0x0070 ist frei");
    }

    [Test]
    public static void Katalog_RichtungenUndArten()
    {
        // (Typ, Client sendet, Server sendet) nach 4.4: P = Push, A = Anfrage, W = Antwort.
        (Nachrichtentyp Typ, string Hoch, string Runter)[] zeilen =
        [
            (Nachrichtentyp.Hello, "P", ""), (Nachrichtentyp.Welcome, "", "P"), (Nachrichtentyp.Bye, "P", "P"), (Nachrichtentyp.Ping, "A", "W"),
            (Nachrichtentyp.Fehler, "PW", "PW"), (Nachrichtentyp.Registrierung, "A", "W"), (Nachrichtentyp.GeraetStatus, "", "P"),
            (Nachrichtentyp.VerknuepfungBestaetigen, "W", "A"), (Nachrichtentyp.Zustand, "A", "PW"), (Nachrichtentyp.NetzListe, "", "P"),
            (Nachrichtentyp.PegelVoreinstellungen, "A", "PW"), (Nachrichtentyp.EmpfangsBericht, "P", ""), (Nachrichtentyp.ServerLast, "", "P"),
            (Nachrichtentyp.Meldung, "", "P"), (Nachrichtentyp.SpracheTunnel, "P", "P"), (Nachrichtentyp.VoiceKeys, "W", "A"),
            (Nachrichtentyp.Verschieben, "A", "W"), (Nachrichtentyp.AufstellungGeaendert, "", "P"), (Nachrichtentyp.TunnelModus, "A", "W"),
            (Nachrichtentyp.Beauftragung, "A", "W"), (Nachrichtentyp.KontenVerzeichnis, "A", "W"), (Nachrichtentyp.Belegung, "", "P"),
            (Nachrichtentyp.Funkermodus, "A", "W"), (Nachrichtentyp.PhaseSchalten, "A", "W"), (Nachrichtentyp.PhaseGeschaltet, "", "P"),
            (Nachrichtentyp.Abzeichen, "A", "W"), (Nachrichtentyp.MeldungSenden, "A", "W"), (Nachrichtentyp.BereitMelden, "A", "W"),
            (Nachrichtentyp.Meldestand, "", "P"), (Nachrichtentyp.BereitStand, "", "P"),
        ];
        foreach (var (typ, hoch, runter) in zeilen)
        {
            var e = Katalog.Von(typ);
            Pruefe.Gleich(hoch, Arten(e, Seite.Client), $"{typ} hoch");
            Pruefe.Gleich(runter, Arten(e, Seite.Server), $"{typ} runter");
        }
        // Jede Anfrage hat eine Antwort in der Gegenrichtung.
        foreach (var e in Katalog.Alle)
        {
            Pruefe.Gleich(e.IstBinaer, e.Typ is Nachrichtentyp.SpracheTunnel or Nachrichtentyp.VoiceKeys, $"{e.Typ}: binär");
            if (e.Typ == Nachrichtentyp.Fehler) continue; // Fehler ist Antwort auf jede Anfrage, selbst nie Anfrage.
            Pruefe.Gleich(e.HochAnfrage.Erlaubt, e.RunterAntwort.Erlaubt, $"{e.Typ}: Anfrage hoch <-> Antwort runter");
            Pruefe.Gleich(e.RunterAnfrage.Erlaubt, e.HochAntwort.Erlaubt, $"{e.Typ}: Anfrage runter <-> Antwort hoch");
        }
    }

    private static string Arten(KatalogEintrag e, Seite s) =>
        (e.Fuer(s, Nachrichtenart.Push).Erlaubt ? "P" : "") + (e.Fuer(s, Nachrichtenart.Anfrage).Erlaubt ? "A" : "") + (e.Fuer(s, Nachrichtenart.Antwort).Erlaubt ? "W" : "");

    [Test]
    public static void Fehlercodes4_9_NamenUndTrennung()
    {
        Pruefe.Gleich(
            "PROTOKOLL,VERSION,VERSION_ZU_ALT,ZU_GROSS,UNGUELTIG,UNBEKANNT,KEIN_RECHT,VERALTET,GRENZE,UEBERHOLT,CODE_UNGUELTIG,GERAETE_LIMIT,"
            + "NUR_ADMIN_WERKZEUG,PLATZ_BELEGT,SELBSTWAHL_GESPERRT,FELD_FEST,GLOBAL_PFLICHT,GERAET_GESPERRT,KONTO_GESPERRT,VERDRAENGT,"
            + "VERDRAENGEN_GESPERRT,SERVER_VOLL,EXTERN,INTERN,NAME_VERGEBEN,ADMIN_SITZUNG_NOETIG,SCHRITT_BESTAETIGEN,ZWEITFAKTOR_FALSCH,"
            + "ZWEITFAKTOR_GESPERRT,ANTRAEGE_VOLL",
            Drahtnamen<Fehlercode>(), "Namen");
        var trennend = Enum.GetValues<Fehlercode>().Where(Fehlercodes.Trennt).Select(Fehlercodes.Drahtname);
        Pruefe.Gleich("PROTOKOLL,VERSION,VERSION_ZU_ALT,ZU_GROSS,GERAET_GESPERRT,KONTO_GESPERRT,VERDRAENGT,VERDRAENGEN_GESPERRT,SERVER_VOLL",
            string.Join(',', trennend), "Trennung ja");
        Pruefe.Falsch(Fehlercodes.TrenntAlsPushInRegistrierung(Fehlercode.KontoGesperrt), "KONTO_GESPERRT als Push in REGISTRIERUNG");
        Pruefe.Wahr(Fehlercodes.TrenntAlsPushInRegistrierung(Fehlercode.Protokoll), "PROTOKOLL bleibt schwer");
    }

    [Test]
    public static void Aufzaehlungen_Drahtnamen4_5()
    {
        Pruefe.Gleich("TPM,SOFTWARE", Drahtnamen<SchluesselArt>(), "schluesselArt");
        Pruefe.Gleich("UNBEKANNT,WARTET,FREIGEGEBEN", Drahtnamen<WelcomeStatus>(), "Welcome.status");
        Pruefe.Gleich("BEENDET,WARTUNG,VERDRAENGT,ERSETZT,GESPERRT,ENTZOGEN,ENTFERNT,WARTEZEIT,PROTOKOLL", Drahtnamen<ByeGrund>(), "Bye.grund");
        Pruefe.Gleich("ADMIN,RAIDLEITER,FUNKER", Drahtnamen<Stufe>(), "Stufe");
        Pruefe.Gleich("GLOBAL_SENDEN,DURCHSAGE,DURCHSAGE_ALLE,MEHRFACH_SENDEN,ZUWEISEN,SPRECHANZEIGE_ALLE,GERAETE_FREIGEBEN,KONTEN_VERWALTEN,NETZE_VERWALTEN", Drahtnamen<Recht>(), "Rechte 6.1");
        Pruefe.Gleich("WARTET,FREIGEGEBEN,ABGELEHNT,ENTFERNT,ENTZOGEN,BEANTRAGT", Drahtnamen<GeraeteStatus>(), "GeraetStatus.status");
        Pruefe.Gleich("BESTAETIGT,EINSPRUCH", Drahtnamen<Markierung>(), "markierung");
        Pruefe.Gleich("FREIGEBEN,ABLEHNEN,BESTAETIGEN,EINSPRUCH,ENTSPERREN", Drahtnamen<GeraetEntscheidung>(), "entscheidung");
        Pruefe.Gleich("SPERREN,ENTSPERREN,STUFE,RECHTE,NAME,LOESCHEN,SCNAMEN,DISCORDNAME", Drahtnamen<KontoOperation>(), "KontoVerwaltung.op");
        Pruefe.Gleich("VORGESEHEN,EINRICHTUNG,AKTIV,GESPERRT", Drahtnamen<AdminStatus>(), "admin");
        Pruefe.Gleich("ANTRAG,GEHEIMNIS_ABHOLEN,BESTAETIGEN", Drahtnamen<AdminEinrichtungOperation>(), "AdminEinrichtung.op");
        Pruefe.Gleich("LISTE,VORMERKEN,AENDERN,LOESCHEN,EINFUEGEN", Drahtnamen<MitgliedOperation>(), "Mitglied.op");
        Pruefe.Gleich("LISTE,SETZEN,LOESCHEN", Drahtnamen<UntergildeOperation>(), "Untergilde.op");
        Pruefe.Gleich("ONLINE,FREIGEGEBEN,ANTRAG,VORGEMERKT,GESPERRT", Drahtnamen<MitgliedZustand>(), "Mitglied.zustand");
        Pruefe.Gleich("GLOBAL,NORMAL", Drahtnamen<NetzTyp>(), "netze.typ");
        Pruefe.Gleich("VA,TASTE", Drahtnamen<Ausloeser>(), "ausloeserStandard");
        Pruefe.Gleich("NEIN,HOEREN,HOEREN_SENDEN", Drahtnamen<NetzOffen>(), "offen");
        Pruefe.Gleich("BELEGT,RESERVIERT", Drahtnamen<PlatzStatus>(), "platz");
        Pruefe.Gleich("STANDARD,ROLLE,SELBST,VERSCHIEBEN,AUFSTELLUNG,WIEDERHERGESTELLT,SATZ", Drahtnamen<MitgliedschaftsQuelle>(), "quelle");
        Pruefe.Gleich("UDP,TUNNEL", Drahtnamen<Verbindungsweg>(), "verbindung/weg");
        Pruefe.Gleich("GUT,MAESSIG,SCHLECHT", Drahtnamen<Guete>(), "guete");
        Pruefe.Gleich("KEIN_SENDERECHT,ZIEL_ENTZOGEN,NUR_TASTE,KEIN_RECHT_GLOBAL,KEIN_RECHT_DURCHSAGE,MEHRFACH_NICHT_ERLAUBT,STUMMGESCHALTET,UNBEKANNTES_NETZ,UNGUELTIG,GRENZE", Drahtnamen<SendeFehlerCode>(), "SendeFehler.code");
        Pruefe.Gleich("ERGAENZEN,ERSETZEN", Drahtnamen<AufstellungsModus>(), "modus");
        Pruefe.Gleich("LESEN,SETZEN", Drahtnamen<RegelOperation>(), "RegelTabelle.op");
        Pruefe.Gleich("DURCHSAGE,GLOBAL", Drahtnamen<DurchsageArt>(), "DurchsageStart.art");
        Pruefe.Gleich("NETZ,DURCHSAGE,GLOBAL", Drahtnamen<SprechArt>(), "Sprechanzeige.art");
        Pruefe.Gleich("LESEN,SCHREIBEN", Drahtnamen<AblageOperation>(), "Ablage.op");
        Pruefe.Gleich("SAETZE,LETZTE,ANSICHT", Drahtnamen<AblageBereich>(), "Ablage.bereich");
        Pruefe.Gleich("ZAEHLER,FEHLER", Drahtnamen<RotationsGrund>(), "KeyRotation.grund");
        Pruefe.Gleich("AUS,PRUEFUNG,GEBUNDEN", Drahtnamen<UdpZustand>(), "UdpStatus.zustand");
        Pruefe.Gleich("KEINE_BINDUNG,UDP_AUSFALL,UDP_ZURUECK,NUTZER", Drahtnamen<TunnelGrund>(), "TunnelModus.grund");
        Pruefe.Gleich("NETZ_SETZEN,NETZ_LOESCHEN,SCHIFF_SETZEN,SCHIFF_LOESCHEN,ROLLE_SETZEN,ROLLE_LOESCHEN,SELBSTWAHL,EINSTELLUNG,PAKET,PHASE_SETZEN,PHASE_LOESCHEN",
            Drahtnamen<VerwaltungsOperation>(), "Verwaltung.op");
        Pruefe.Gleich("ERTEILEN,ENTZIEHEN,ABGEBEN,LISTE", Drahtnamen<BeauftragungOperation>(), "Beauftragung.op");
        Pruefe.Gleich("SCHIFF,TRUPP,ORGA", Drahtnamen<Einheitsart>(), "schiffe.art");
        Pruefe.Gleich(0, (int)VoiceKeysGrund.Start, "START = 0");
        Pruefe.Gleich(1, (int)VoiceKeysGrund.Rotation, "ROTATION = 1");
    }

    // ---------------------------------------------------------------- 4.7 Versionierung

    [Test]
    public static void Versionierung4_7()
    {
        Pruefe.Gleich("tacrelay/1", Versionen.Alpn, "ALPN");
        Pruefe.Gleich((byte)1, Rahmen.Version, "Rahmenversion");
        Pruefe.Gleich((ushort)0, Versionen.Nebenversion, "v1.0");
        Pruefe.Gleich((ushort)2, Versionen.NebenversionVereinbaren(2, 5), "Minimum Client");
        Pruefe.Gleich((ushort)1, Versionen.NebenversionVereinbaren(4, 1), "Minimum Server");

        Pruefe.Falsch(Versionen.ClientZuAlt("1.2.0", "1.2.0"), "gleich");
        Pruefe.Falsch(Versionen.ClientZuAlt("1.10.0", "1.9.9"), "numerisch, nicht lexikalisch");
        Pruefe.Wahr(Versionen.ClientZuAlt("1.2.0-beta.2", "1.2.0"), "Vorabversion ist älter");
        Pruefe.Wahr(Versionen.ClientZuAlt("1.1.9", "1.2.0"), "kleiner");
        Pruefe.Wahr(Versionen.ClientZuAlt("kaputt", "1.2.0"), "nicht lesbar gilt als zu alt");
        Pruefe.Falsch(Versionen.ClientZuAlt("1.2.0+build.7", "1.2.0"), "Build zählt nicht");

        Pruefe.Wahr(SemVer.TryParse("1.0.0-alpha", out var a) && SemVer.TryParse("1.0.0-alpha.1", out var b) && a < b, "alpha < alpha.1");
        Pruefe.Wahr(SemVer.TryParse("1.0.0-alpha.beta", out var c) && SemVer.TryParse("1.0.0-beta", out var d) && c < d, "alpha.beta < beta");
        Pruefe.Wahr(SemVer.TryParse("1.0.0-beta.2", out var e) && SemVer.TryParse("1.0.0-beta.11", out var f) && e < f, "beta.2 < beta.11");
        Pruefe.Wahr(SemVer.TryParse("1.0.0-rc.1", out var g) && SemVer.TryParse("1.0.0", out var h) && g < h, "rc.1 < 1.0.0");
        foreach (var falsch in new[] { "1.0", "01.0.0", "1.0.0-", "1.0.0-01", "1.0.0-a..b", "1.0.0+", "v1.0.0", "1.0.0 " })
        {
            Pruefe.Falsch(SemVer.TryParse(falsch, out _), $"keine SemVer: '{falsch}'");
        }

        // Neue Pflichttypen nur an Server, die sie kennen (4.7, CODEMAP): Ein Server 0.4.x trennt bei 0x003C und 0x003D mit PROTOKOLL.
        Pruefe.Gleich("0.5.0", Versionen.ServerFassungFuer(Nachrichtentyp.Funkermodus), "Funkermodus seit 0.5.0");
        Pruefe.Gleich("0.5.0", Versionen.ServerFassungFuer(Nachrichtentyp.PhaseSchalten), "PhaseSchalten seit 0.5.0");
        Pruefe.Gleich("0.3.0", Versionen.ServerFassungFuer(Nachrichtentyp.Beauftragung), "Beauftragung weiter seit 0.3.0");
        foreach (var alt in new[] { "0.4.0", "0.4.1", "0.3.0", "0.2.0" })
        {
            Pruefe.Falsch(Versionen.ServerKennt(alt, Nachrichtentyp.Funkermodus), "Server " + alt + " kennt Funkermodus nicht");
            Pruefe.Falsch(Versionen.ServerKennt(alt, Nachrichtentyp.PhaseSchalten), "Server " + alt + " kennt PhaseSchalten nicht");
        }
        Pruefe.Wahr(Versionen.ServerKennt("0.5.0", Nachrichtentyp.PhaseSchalten) && Versionen.ServerKennt("0.5.1+abc", Nachrichtentyp.Funkermodus), "ab 0.5.0");
        Pruefe.Wahr(Versionen.ServerKennt("0.4.0", Nachrichtentyp.Verschieben), "alte Typen immer");

        // Seit 0.8.0: ENTSPERREN fragt der Client nur einen Server ab 0.8.0 (ein älterer kennt den Wert nicht und antwortet UNGUELTIG).
        Pruefe.Gleich("0.8.0", Versionen.EntsperrenAb, "Sperre aufheben seit 0.8.0");
        foreach (var alt in new[] { "0.7.0", "0.7.9", "0.6.0", "0.8.0-rc.1" })
            Pruefe.Wahr(Versionen.ServerAb(alt, Versionen.EntsperrenAb) == alt.StartsWith("0.8.0", StringComparison.Ordinal), "Server " + alt);
        Pruefe.Wahr(Versionen.ServerAb("0.8.1", Versionen.EntsperrenAb) && Versionen.ServerAb("1.0.0", Versionen.EntsperrenAb), "ab 0.8.0");
        Pruefe.Wahr(Versionen.ServerAb(null, Versionen.EntsperrenAb) && Versionen.ServerAb("unlesbar", Versionen.EntsperrenAb), "ohne lesbare Fassung: ja, wie ServerKennt");
        var entsperren = SteuerJson.Lesen(Encoding.UTF8.GetBytes("{\"geraetId\":7,\"entscheidung\":\"ENTSPERREN\"}"), SteuerJson.Info<GeraetEntscheidenAnfrage>());
        Pruefe.Wahr(entsperren.Body is GeraetEntscheidenAnfrage { Entscheidung: GeraetEntscheidung.Entsperren, GeraetId: 7 }, "ENTSPERREN auf dem Draht");

        var fehler = Encoding.UTF8.GetString(SteuerJson.Serialisieren(Versionen.VersionZuAltFehler("1.2.0")));
        Pruefe.Gleich("{\"code\":\"VERSION_ZU_ALT\",\"details\":{\"mindest\":\"1.2.0\"}}", fehler, "Fehler VERSION_ZU_ALT");
        Pruefe.Gleich(Fehlercode.Ungueltig, SteuerJson.Lesen(Encoding.UTF8.GetBytes("{\"minor\":0,\"client\":{\"name\":\"F\",\"version\":\"1.0\"},\"schluesselArt\":\"TPM\",\"opus\":\"1\",\"faehigkeiten\":[]}"), SteuerJson.Info<Hello>()).Fehler, "Hello mit Nicht-SemVer");
    }
}
