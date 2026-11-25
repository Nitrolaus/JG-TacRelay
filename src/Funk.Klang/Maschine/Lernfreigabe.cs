// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Was die <see cref="Sendekette"/> über einen Rahmen weiß, wenn sie entscheidet, ob die <see cref="Pegelstufe"/> aus ihm lernen darf.
/// <paramref name="WaechterEigeneStimme"/> ist die Stelle für den Wächter aus Klangstufe C2 (sein Urteil "eigene Stimme"); bis dahin immer
/// wahr.
/// </summary>
internal readonly record struct LernfreigabeLage(bool Gesendet, float Sprachwahrscheinlichkeit, bool Echoverdacht, bool WaechterEigeneStimme = true);

/// <summary>
/// Die Pegelstufe lernt nur aus eigener Stimme (Klangstufe C1, Entwurf "Ineinandergreifen" 1 und 2). Je Rahmen: gesendet UND hohe
/// Sprachwahrscheinlichkeit (<see cref="Pegelstufe.SprachSchwelle"/>) UND kein Echoverdacht UND (ab C2) das Urteil des Wächters. Vor C1 lernte
/// sie aus allem, was hinausging; ging Echo hinaus, verstellte sie sich danach.
/// <para>
/// Die Bedingung "über der Mindest-Funklautstärke" (sonst nur mit Sprechtaste) prüft die Pegelstufe je Äußerung, nicht je Rahmen
/// (<see cref="Pegelstufe.MindestAbstandDb"/>): Sie lernt ohnehin den mittleren Pegel ganzer Äußerungen. Je Rahmen geprüft ließ die Bedingung
/// die lauten Silben leiser Sprache durch, deren Mittel dann nah genug an der Stimme lag, um zu zählen: Nach drei solchen Äußerungen drehte
/// die Halteregel auf, und die Mindest-Funklautstärke wanderte mit der Stimme nach unten (MessungKlangC1, Nachweis Einmessen); und sie hob
/// den gelernten Pegel normaler Sprache an, weil die leisen Rahmen jeder Äußerung herausfielen. Verglichen wird vor der Pegelstufe
/// (Grundpegel gegen Pegel der Äußerung), also unabhängig von der Verstärkung. Solange die Stimme nicht gelernt ist, gilt die Bedingung nicht
/// (sonst Verklemmung bei leisem Mikrofon, Hinweis aus Sarevocs 0.6.0 am 03.10.2026).
/// </para>
/// </summary>
internal static class Lernfreigabe
{
    public static bool Pruefen(in LernfreigabeLage l) =>
        l.Gesendet && !l.Echoverdacht && l.WaechterEigeneStimme && l.Sprachwahrscheinlichkeit >= Pegelstufe.SprachSchwelle;
}
