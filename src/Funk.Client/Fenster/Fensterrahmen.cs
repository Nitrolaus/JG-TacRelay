// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Funk.Client.Fenster;

/// <summary>
/// Gemeinsames für die Fenster mit eigener Titelleiste (WindowChrome): Befehle der Titelknöpfe, Rand im maximierten
/// Zustand (sonst ragt der Inhalt über den Bildschirm), und für das Funkfeld die Sperre gegen Minimieren und Maximieren
/// (A24, ein Overlay): Ohne WS_MINIMIZEBOX und WS_MAXIMIZEBOX bietet Windows beides weder im Systemmenü noch per Win+Pfeil
/// an, und ein Doppelklick auf die Titelzeile maximiert nicht. Was trotzdem durchkommt (Taskleiste, andere Programme),
/// nimmt <see cref="Erlaubt"/> sofort zurück. Lage und Größe führt <see cref="Platzfuehrung"/>.
/// </summary>
internal static partial class Fensterrahmen
{
    private const int GwlStyle = -16;
    internal const int WsMinimizebox = 0x00020000;
    internal const int WsMaximizebox = 0x00010000;

    public static void Einrichten(Window fenster, bool minimierbar, bool maximierbar = true)
    {
        fenster.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => fenster.Close()));
        fenster.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(fenster), (_, e) => e.CanExecute = minimierbar));
        fenster.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(fenster), (_, e) => e.CanExecute = maximierbar));
        fenster.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(fenster)));
        fenster.StateChanged += (_, _) =>
        {
            var erlaubt = Erlaubt(fenster.WindowState, minimierbar, maximierbar);
            if (erlaubt != fenster.WindowState)
            {
                fenster.WindowState = erlaubt;
                return;
            }
            RandSetzen(fenster);
        };
        if (!minimierbar || !maximierbar)
        {
            fenster.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(fenster).Handle;
                var stil = GetWindowLongW(h, GwlStyle);
                if (stil != 0) _ = SetWindowLongW(h, GwlStyle, Stil(stil, minimierbar, maximierbar));
            };
        }
        RandSetzen(fenster);
    }

    /// <summary>Fensterstil ohne die Knöpfe, die das Fenster nicht haben darf. Reine Rechnung, ohne Handle testbar.</summary>
    internal static int Stil(int stil, bool minimierbar, bool maximierbar)
    {
        if (!minimierbar) stil &= ~WsMinimizebox;
        if (!maximierbar) stil &= ~WsMaximizebox;
        return stil;
    }

    /// <summary>Der Zustand, in den das Fenster zurück soll: Minimiert oder Maximiert nur, wenn erlaubt, sonst Normal.</summary>
    internal static WindowState Erlaubt(WindowState zustand, bool minimierbar, bool maximierbar) => zustand switch
    {
        WindowState.Minimized when !minimierbar => WindowState.Normal,
        WindowState.Maximized when !maximierbar => WindowState.Normal,
        _ => zustand,
    };

    private static void RandSetzen(Window fenster)
    {
        if (fenster.WindowState == WindowState.Maximized)
        {
            var r = SystemParameters.WindowResizeBorderThickness;
            var extra = SystemParameters.FixedFrameVerticalBorderWidth;
            fenster.BorderThickness = new Thickness(r.Left + extra, r.Top + extra, r.Right + extra, r.Bottom + extra);
        }
        else
        {
            fenster.BorderThickness = new Thickness(1);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr fenster, int index);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(IntPtr fenster, int index, int wert);
}
