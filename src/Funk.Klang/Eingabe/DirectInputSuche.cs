// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Aufzählen der Spielgeräte über DirectInput 8 (DI8DEVCLASS_GAMECTRL), im eigenen Suchthread mit eigenem
/// DirectInput-Objekt. EnumDevices braucht gemessen rund 200 ms bei sechs Geräten; im Lesethread hielte das jeden
/// Tastendruck so lange auf. Das Lesen in einem anderen Thread wird dabei nicht gebremst (gemessen: höchstens 0,13 ms
/// je Lesen, während daneben elf Aufzählungen liefen).
/// <para>
/// Jedes Gerät wird kurz ohne Zugriff angelegt, um Fähigkeiten, Pfad (DIPROP_GUIDANDPATH) und VID/PID (DIPROP_VIDPID)
/// zu lesen, wie im Katalog des JG Signal Monitor (core\src\DeviceCatalog.cpp). Die Instanz-GUID gilt nur für das
/// Öffnen gleich danach.
/// </para>
/// </summary>
internal sealed unsafe class DirectInputSuche : ISpielgeraeteSuche
{
    private readonly IDirectInput8W _directInput = DirectInputZugriff.Anlegen(DirectInputNativ.ModulGriff(null));
    private bool _frei;

    public bool Aufzaehlen(List<SpielgeraetInfo> ziel)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        var gefunden = new List<Rohgeraet>();
        var griff = GCHandle.Alloc(gefunden);
        int hr;
        try
        {
            hr = _directInput.EnumDevices(
                DirectInputNativ.KlasseSpielgeraete,
                (nint)(delegate* unmanaged<DirectInputNativ.Geraeteinstanz*, void*, int>)&Gefunden,
                (void*)GCHandle.ToIntPtr(griff),
                DirectInputNativ.NurAngeschlossene);
        }
        finally
        {
            griff.Free();
        }
        if (hr < 0) return false;
        foreach (var roh in gefunden)
        {
            if (Beschreiben(roh) is { } info) ziel.Add(info);
        }
        return true;
    }

    public void Dispose()
    {
        if (_frei) return;
        _frei = true;
        DirectInputZugriff.Freigeben(_directInput);
    }

    [UnmanagedCallersOnly]
    private static int Gefunden(DirectInputNativ.Geraeteinstanz* geraet, void* kontext)
    {
        try
        {
            var liste = (List<Rohgeraet>)GCHandle.FromIntPtr((nint)kontext).Target!;
            var name = DirectInputZugriff.Bereinigen(new ReadOnlySpan<char>(geraet->Produktname, DirectInputNativ.Pfadlaenge));
            if (name.Length == 0) name = DirectInputZugriff.Bereinigen(new ReadOnlySpan<char>(geraet->Instanzname, DirectInputNativ.Pfadlaenge));
            liste.Add(new Rohgeraet(geraet->Instanz, geraet->Produkt, (byte)(geraet->Typ & 0xFF), name));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keine Ausnahme über die native Grenze; das Gerät fehlt dann in dieser Aufzählung.
        }
        return DirectInputNativ.WeiterAufzaehlen;
    }

    private SpielgeraetInfo? Beschreiben(Rohgeraet roh)
    {
        // Tastatur und Maus nie über DirectInput anlegen: Sie melden intern Raw Input an und nähmen der
        // Raw-Input-Sprechtaste des Prozesses die Anmeldung weg. Die Klasse GAMECTRL liefert sie ohnehin nicht.
        if (roh.Typ is DirectInputNativ.TypMaus or DirectInputNativ.TypTastatur or DirectInputNativ.TypZeiger) return null;
        var zugang = DirectInputZugriff.Erzeugen(_directInput, roh.Instanz, out _);
        if (zugang is null) return null;
        try
        {
            var faehigkeiten = new DirectInputNativ.Faehigkeiten { Groesse = (uint)sizeof(DirectInputNativ.Faehigkeiten) };
            if (zugang.GetCapabilities(&faehigkeiten) < 0) return null;
            var vidPid = DirectInputNativ.DwordKopf();
            ushort vid, pid;
            if (zugang.GetProperty(DirectInputNativ.EigenschaftVidPid, &vidPid.Kopf) >= 0)
            {
                vid = (ushort)(vidPid.Wert & 0xFFFF);
                pid = (ushort)(vidPid.Wert >> 16);
            }
            else
            {
                // Wie im JG Signal Monitor: Data1 der Produkt-GUID trägt PID und VID.
                var produkt = roh.Produkt;
                var data1 = MemoryMarshal.Read<uint>(MemoryMarshal.AsBytes(new ReadOnlySpan<Guid>(in produkt)));
                vid = (ushort)(data1 & 0xFFFF);
                pid = (ushort)(data1 >> 16);
            }
            var kennung = new Geraetekennung(DirectInputZugriff.PfadLesen(zugang), vid, pid, roh.Name);
            return new SpielgeraetInfo(roh.Instanz, kennung, DirectInputZugriff.Begrenzen(faehigkeiten.Knoepfe, Joystickeingabe.Knoepfe),
                DirectInputZugriff.Begrenzen(faehigkeiten.Povs, Joystickeingabe.Huete), (faehigkeiten.Flags & DirectInputNativ.MussAbfragen) != 0);
        }
        finally
        {
            DirectInputZugriff.Freigeben(zugang);
        }
    }

    private readonly record struct Rohgeraet(Guid Instanz, Guid Produkt, byte Typ, string Name);
}
