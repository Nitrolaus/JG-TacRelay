// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.Server.Sprachpfad;
using Funk.Server.Steuer;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Brücke vom Relay-Thread zum Steuerkanal: stellt die Nachrichten des Sprachpfads der Verbindung der jeweiligen
/// Sitzung zu (UdpStatus, VoiceKeys ROTATION, SpracheTunnel runter, DurchsageStart/-Ende, SendeFehler, VerlustHinweis)
/// oder reicht sie an die Zentrale (Meldungen an Rechteinhaber, Überlast). Blockiert den Relay-Thread nie.
/// </summary>
internal sealed class RelaisBruecke(Zentrale zentrale) : IRelaisSenke
{
    public void UdpStatus(uint sitzung, S.UdpZustand zustand, string? adresse, bool? gleicheIp)
    {
        if (!zentrale.SitzungSuchen(sitzung, out var v)) return;
        v.Push(S.Nachrichtentyp.UdpStatus, new S.UdpStatus { Zustand = zustand, Adresse = adresse, GleicheIp = gleicheIp });
        if (zustand == S.UdpZustand.Gebunden) zentrale.Log.Debug("UDP gebunden: " + zentrale.Log.Schwaerzung.Konto(v.KontoId) + (gleicheIp == false ? " (andere IP als TLS)" : ""));
    }

    public void VoiceKeys(uint sitzung, byte epoche, byte[] material)
    {
        if (zentrale.SitzungSuchen(sitzung, out var v)) v.VoiceKeysSenden(epoche, material);
        else CryptographicOperations.ZeroMemory(material);
    }

    public void Trennen(uint sitzung, S.Fehlercode fehler)
    {
        if (zentrale.SitzungSuchen(sitzung, out var v)) v.Beenden(fehler, null);
    }

    public void Tunnel(uint sitzung, uint absender, byte[] datagramm)
    {
        if (zentrale.SitzungSuchen(sitzung, out var v)) v.TunnelSenden(absender, datagramm);
    }

    public void DurchsageStart(uint empfaenger, uint sprecher, L.Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase)
    {
        if (!zentrale.SitzungSuchen(empfaenger, out var v)) return;
        v.Push(S.Nachrichtentyp.DurchsageStart, new S.DurchsageStart
        {
            Sprecher = sprecher,
            Art = art == L.Stempelart.Global ? S.DurchsageArt.Global : S.DurchsageArt.Durchsage,
            Netze = netze.ToArray(),
            Sprechphase = sprechphase,
        });
    }

    public void DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase)
    {
        if (zentrale.SitzungSuchen(empfaenger, out var v))
            v.Push(S.Nachrichtentyp.DurchsageEnde, new S.DurchsageEnde { Sprecher = sprecher, Sprechphase = sprechphase });
    }

    public void SendeFehler(uint sitzung, ReadOnlySpan<L.SendeFehlerEintrag> fehler, bool teilweise, uint verworfen)
    {
        if (!zentrale.SitzungSuchen(sitzung, out var v)) return;
        var liste = new List<S.SendeFehlerEintrag>(fehler.Length);
        foreach (var f in fehler)
        {
            if (f.Code == L.SendeFehlerCode.Keiner) continue;
            // Gleiche Reihenfolge der Codes, nur ohne "Keiner" am Anfang.
            liste.Add(new S.SendeFehlerEintrag
            {
                Code = (S.SendeFehlerCode)((int)f.Code - 1),
                NetzId = f.NetzId == 0 ? null : f.NetzId,
                Durch = f.Durch == 0 ? null : f.Durch,
            });
        }
        v.Push(S.Nachrichtentyp.SendeFehler, new S.SendeFehler { Fehler = liste, Teilweise = teilweise, Verworfen = verworfen });
    }

    public void Rate(uint sitzung, uint kontoId) =>
        zentrale.Einreihen(() => zentrale.MeldungSenden(S.MeldungsArten.Rate, L.Recht.Zuweisen, kontoId));

    public void Kopierverdacht(uint sitzung, uint kontoId) =>
        zentrale.Einreihen(() => zentrale.MeldungSenden(S.MeldungsArten.Kopierverdacht, L.Recht.GeraeteFreigeben, kontoId));

    public void VerlustHinweis(uint sitzung, byte prozent)
    {
        if (zentrale.SitzungSuchen(sitzung, out var v)) v.Push(S.Nachrichtentyp.VerlustHinweis, new S.VerlustHinweis { Prozent = prozent });
    }

    public void EigenerVerlust(uint sitzung, string richtung)
    {
        if (!zentrale.SitzungSuchen(sitzung, out var v)) return;
        v.Push(S.Nachrichtentyp.Meldung, new S.Meldung
        {
            Art = S.MeldungsArten.EigenerVerlust,
            KontoId = v.KontoId,
            Details = Antwort.DetailsBauen(w => w.WriteString("richtung", richtung)),
        });
    }

    public void Sprechanzeige(IReadOnlyList<Sprecher> aktiv)
    {
        var push = new S.Sprechanzeige
        {
            Aktiv = [.. aktiv.Take(S.Grenzen.SitzungenMax).Select(a => new S.SprechanzeigeEintrag { KontoId = a.KontoId, Art = a.Art, Netze = a.Netze })],
        };
        zentrale.Einreihen(() =>
        {
            foreach (var v in zentrale.VerbindungenMitRecht(L.Recht.SprechanzeigeAlle))
                if (v.SprechanzeigeAbo) v.Push(S.Nachrichtentyp.Sprechanzeige, push);
        });
    }

    public void Last(int stufe, uint verworfen, double ausgangMbit)
    {
        // Stufe 2: keine neuen Registrierungen und unfreigegebenen Verbindungen (7.4).
        zentrale.Zulassung.Ueberlast = stufe >= 2;
        var last = new S.ServerLast { Stufe = (byte)stufe, Verworfen = verworfen, AusgangMbit = Math.Round(ausgangMbit, 2) };
        zentrale.Einreihen(() =>
        {
            foreach (var v in zentrale.VerbindungenMitRecht(L.Recht.Zuweisen)) v.Push(S.Nachrichtentyp.ServerLast, last);
        });
    }
}
