// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>
/// "Wer ist da" als eigenes Fenster (seit 0.11.5, Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster ablösbar"): dieselbe Liste wie
/// in der Spalte, frei verschiebbar, über dem Hauptfenster (Owner), ohne eigenen Eintrag in der Taskleiste, mit dunkler Titelleiste. Es steht
/// rechts neben dem Hauptfenster, wenn dort Platz ist, sonst innen an seinem rechten Rand. Wer es schließt, legt die Liste zurück in die
/// Spalte (Hauptfenster.xaml.cs, <see cref="WerIstDaViewModel.Geloest"/>).
/// </summary>
public sealed partial class WerIstDaFenster : Window
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: dunkle Titelleiste wie das übrige Programm.</summary>
    private const int DunkelModus = 20;

    public WerIstDaFenster(WerIstDaViewModel modell, Window besitzer)
    {
        ArgumentNullException.ThrowIfNull(modell);
        ArgumentNullException.ThrowIfNull(besitzer);
        // Die Vorlage der Liste steht in Bereiche.xaml wie für die Spalte.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/TacRelay;component/Fenster/Bereiche.xaml", UriKind.Absolute) });
        Title = "Wer ist da";
        Owner = besitzer;
        ShowInTaskbar = false;
        Width = Fenstergroessen.WerIstDaBreite + 16;
        Height = Math.Max(480, besitzer.ActualHeight);
        MinWidth = Width;
        MinHeight = 300;
        Background = (Brush)FindResource("Pinsel.FlaecheTief");
        Content = new ContentControl { Content = modell, Focusable = false };
        var rechts = besitzer.Left + besitzer.ActualWidth;
        Left = rechts + Width <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ? rechts : rechts - Width - 24;
        Top = besitzer.Top;
        SourceInitialized += (_, _) => DunkleTitelleiste();
    }

    private void DunkleTitelleiste()
    {
        var an = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DunkelModus, ref an, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr fenster, int eigenschaft, ref int wert, int groesse);
}
