// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Funk.Klang.Kern;

/// <summary>
/// Längenprüfungen für Spans, bevor ein Zeiger an eine native Bibliothek geht. Der Normalfall
/// kostet einen Vergleich; nur der Fehlerfall baut eine Meldung und wirft.
/// </summary>
internal static class LaengenPruefung
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Genau(int ist, int soll, string parameter)
    {
        if (ist != soll) WerfenGenau(ist, soll, parameter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Mindestens(int ist, int mindestens, string parameter)
    {
        if (ist < mindestens) WerfenMindestens(ist, mindestens, parameter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Vielfaches(int ist, int teiler, string parameter)
    {
        if (ist % teiler != 0) WerfenVielfaches(ist, teiler, parameter);
    }

    [DoesNotReturn]
    private static void WerfenGenau(int ist, int soll, string parameter) =>
        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Erwartet genau {soll} Werte, erhalten {ist}."), parameter);

    [DoesNotReturn]
    private static void WerfenMindestens(int ist, int mindestens, string parameter) =>
        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Erwartet mindestens {mindestens} Werte, erhalten {ist}."), parameter);

    [DoesNotReturn]
    private static void WerfenVielfaches(int ist, int teiler, string parameter) =>
        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Erwartet ein Vielfaches von {teiler}, erhalten {ist}."), parameter);
}
