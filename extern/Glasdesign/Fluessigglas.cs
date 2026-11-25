// Glasdesign - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace Glasdesign;

/// <summary>
/// Der Effekt des Flüssigglases (Stilfamilie 5): ein Pixelshader, der das Bild dahinter am Rand einer abgerundeten Fläche
/// nach außen verschiebt, wie eine dicke Glaskante es bricht, Rot und Blau etwas verschieden weit (Farbsaum). Innen,
/// jenseits der Kante, bleibt das Bild unverändert. Der Shader steht als HLSL im Code und wird beim ersten Gebrauch mit
/// <c>d3dcompiler_47.dll</c> übersetzt, die zu Windows gehört (auch WPF braucht sie); kein fremdes Paket. Ziel ist
/// Shader-Modell 2.0, weil WPF nur das auch ohne Grafikkarte zeichnet (Tests, Vorschaubilder, Fernsitzungen). Fehlt der
/// Übersetzer, bleibt das Glas ohne Brechung (<see cref="Verfuegbar"/>).
/// </summary>
public sealed partial class Brechungseffekt : ShaderEffect
{
    /// <summary>Der Shader: Lage in DIP aus uv mal Größe, Abstand zum Rand über die Formel des abgerundeten Rechtecks,
    /// die Richtung nach außen ohne Ableitung (in der Ecke vom Eckmittelpunkt weg, sonst senkrecht zur Seite).</summary>
    internal const string Hlsl = """
        sampler2D Eingang : register(s0);
        float4 Form : register(c0);   // Breite, Höhe, Eckenradius, Kantenbreite (DIP)
        float4 Werte : register(c1);  // Verschiebung (DIP), Farbsaum (Anteil), Einzug (DIP), frei

        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 groesse = Form.xy;
            float2 p = uv * groesse - groesse * 0.5;
            float2 halb = groesse * 0.5 - Werte.z;
            float2 q = abs(p) - (halb - Form.z);
            float2 aussen = max(q, 0);
            float d = length(aussen) + min(max(q.x, q.y), 0) - Form.z;
            float2 richtung = (q.x > 0 && q.y > 0) ? normalize(aussen + 0.0001) : ((q.x > q.y) ? float2(1, 0) : float2(0, 1));
            richtung *= sign(p + 0.0001);
            float t = saturate(1 + d / Form.w);
            float v = Werte.x * t * t * step(d, 0);
            float2 o = richtung * v / groesse;
            float4 mitte = tex2D(Eingang, uv + o);
            float rot = tex2D(Eingang, uv + o * (1 + Werte.y)).r;
            float blau = tex2D(Eingang, uv + o * (1 - Werte.y)).b;
            return float4(rot, mitte.g, blau, mitte.a);
        }
        """;

    public static readonly DependencyProperty EingangProperty = RegisterPixelShaderSamplerProperty("Eingang", typeof(Brechungseffekt), 0);
    public static readonly DependencyProperty FormProperty = DependencyProperty.Register(
        nameof(Form), typeof(Point4D), typeof(Brechungseffekt), new UIPropertyMetadata(new Point4D(100, 40, 20, 12), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty WerteProperty = DependencyProperty.Register(
        nameof(Werte), typeof(Point4D), typeof(Brechungseffekt), new UIPropertyMetadata(new Point4D(14, 0.12, 0, 0), PixelShaderConstantCallback(1)));

    private static readonly Lazy<PixelShader?> Shader = new(Uebersetzen);

    public Brechungseffekt()
    {
        if (Shader.Value is { } s) PixelShader = s;
        UpdateShaderValue(EingangProperty);
        UpdateShaderValue(FormProperty);
        UpdateShaderValue(WerteProperty);
    }

    /// <summary>Ob der Shader übersetzt werden konnte (sonst zeigt das Flüssigglas das Bild dahinter ohne Brechung).</summary>
    public static bool Verfuegbar => Shader.Value is not null;

    public Brush Eingang { get => (Brush)GetValue(EingangProperty); set => SetValue(EingangProperty, value); }

    /// <summary>Breite, Höhe, Eckenradius und Kantenbreite in DIP.</summary>
    public Point4D Form { get => (Point4D)GetValue(FormProperty); set => SetValue(FormProperty, value); }

    /// <summary>Verschiebung am Rand (DIP), Farbsaum (Anteil), Einzug der Fläche im Eingang (DIP).</summary>
    public Point4D Werte { get => (Point4D)GetValue(WerteProperty); set => SetValue(WerteProperty, value); }

    /// <summary>Den Shader einmal übersetzen; null, wenn Windows den Übersetzer nicht hat oder er scheitert.</summary>
    internal static PixelShader? Uebersetzen()
    {
        try
        {
            var code = Bytecode(Hlsl, "main", "ps_2_0", out var fehler);
            if (code is null)
            {
                System.Diagnostics.Debug.WriteLine("Glasdesign Brechungseffekt: " + fehler);
                return null;
            }
            var shader = new PixelShader();
            shader.SetStreamSource(new MemoryStream(code));
            shader.Freeze();
            return shader;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>HLSL übersetzen (D3DCompile aus d3dcompiler_47); bei einem Fehler null und die Meldung.</summary>
    internal static unsafe byte[]? Bytecode(string hlsl, string einstieg, string ziel, out string fehler)
    {
        var quelle = Encoding.UTF8.GetBytes(hlsl);
        IntPtr code = IntPtr.Zero, meldung = IntPtr.Zero;
        try
        {
            int ergebnis;
            fixed (byte* q = quelle)
                ergebnis = D3DCompile(q, (nuint)quelle.Length, "Glasdesign", IntPtr.Zero, IntPtr.Zero, einstieg, ziel, 0, 0, out code, out meldung);
            fehler = meldung == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(Zeiger(meldung)) ?? "";
            if (ergebnis < 0 || code == IntPtr.Zero) return null;
            var bytes = new byte[(int)Groesse(code)];
            Marshal.Copy(Zeiger(code), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Freigeben(code);
            Freigeben(meldung);
        }
    }

    // ID3DBlob: nach IUnknown (QueryInterface, AddRef, Release) folgen GetBufferPointer und GetBufferSize.
    private static unsafe IntPtr Zeiger(IntPtr blob) => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)(*(*(void***)blob + 3)))(blob);

    private static unsafe nuint Groesse(IntPtr blob) => ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)(*(*(void***)blob + 4)))(blob);

    private static unsafe void Freigeben(IntPtr blob)
    {
        if (blob != IntPtr.Zero) _ = ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(*(void***)blob + 2)))(blob);
    }

    [LibraryImport("d3dcompiler_47.dll", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int D3DCompile(byte* quelle, nuint laenge, string name, IntPtr definitionen, IntPtr einbinden,
        string einstieg, string ziel, uint schalter1, uint schalter2, out IntPtr code, out IntPtr meldungen);
}

/// <summary>
/// Der Grund des Flüssigglases (Stil Glas.Fluessigglas): zeigt das Element dahinter (<see cref="Quelle"/>) scharf an genau
/// der Stelle, an der das Glas liegt, und bricht es am Rand mit dem <see cref="Brechungseffekt"/>. Das innere Element ist um
/// die Verschiebung größer, damit der Rand Bild von außerhalb der Fläche holen kann; geschnitten wird auf die Fläche.
/// Wie beim Milchglas muss die Quelle hinter dem Glas liegen und darf es nicht enthalten.
/// </summary>
public sealed class Fluessigglasgrund : FrameworkElement
{
    public static readonly DependencyProperty QuelleProperty = DependencyProperty.Register(
        nameof(Quelle), typeof(Visual), typeof(Fluessigglasgrund), new FrameworkPropertyMetadata(null, (d, _) => ((Fluessigglasgrund)d)._bild.InvalidateVisual()));

    public static readonly DependencyProperty EckeProperty = Zahl(nameof(Ecke), 30);
    public static readonly DependencyProperty BrechungProperty = Zahl(nameof(Brechung), 14);
    public static readonly DependencyProperty KantenbreiteProperty = Zahl(nameof(Kantenbreite), 18);
    public static readonly DependencyProperty FarbsaumProperty = Zahl(nameof(Farbsaum), 0.12);

    private static DependencyProperty Zahl(string name, double vorgabe) => DependencyProperty.Register(
        name, typeof(double), typeof(Fluessigglasgrund), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsArrange));

    private readonly Bild _bild;
    private readonly Brechungseffekt? _effekt;

    public Fluessigglasgrund()
    {
        IsHitTestVisible = false;
        _bild = new Bild(this);
        AddVisualChild(_bild);
        if (Brechungseffekt.Verfuegbar)
        {
            _effekt = new Brechungseffekt();
            _bild.Effect = _effekt;
        }
        LayoutUpdated += (_, _) => _bild.LageGeaendert();
    }

    public Visual? Quelle { get => (Visual?)GetValue(QuelleProperty); set => SetValue(QuelleProperty, value); }

    /// <summary>Eckenradius in DIP (höchstens die halbe Höhe: dann eine Pille).</summary>
    public double Ecke { get => (double)GetValue(EckeProperty); set => SetValue(EckeProperty, value); }

    /// <summary>Wie weit das Bild am äußersten Rand verschoben wird, in DIP.</summary>
    public double Brechung { get => (double)GetValue(BrechungProperty); set => SetValue(BrechungProperty, value); }

    /// <summary>Wie breit die brechende Kante ist, in DIP; innen bleibt das Bild unverändert.</summary>
    public double Kantenbreite { get => (double)GetValue(KantenbreiteProperty); set => SetValue(KantenbreiteProperty, value); }

    /// <summary>Farbsaum: Rot wird um diesen Anteil weiter, Blau weniger weit verschoben.</summary>
    public double Farbsaum { get => (double)GetValue(FarbsaumProperty); set => SetValue(FarbsaumProperty, value); }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _bild : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _bild.Measure(availableSize);
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rand = Math.Max(0, Brechung) + 2;
        var aussen = new Size(finalSize.Width + 2 * rand, finalSize.Height + 2 * rand);
        _bild.Arrange(new Rect(new Point(-rand, -rand), aussen));
        var r = Math.Min(Math.Max(0, Ecke), Math.Min(finalSize.Width, finalSize.Height) / 2);
        if (_effekt is not null)
        {
            _effekt.Form = new Point4D(aussen.Width, aussen.Height, r, Math.Max(1, Kantenbreite));
            _effekt.Werte = new Point4D(Math.Max(0, Brechung), Math.Clamp(Farbsaum, 0, 0.5), rand, 0);
        }
        var schnitt = new RectangleGeometry(new Rect(finalSize), r, r);
        schnitt.Freeze();
        Clip = schnitt;
        return finalSize;
    }

    private sealed class Bild(Fluessigglasgrund grund) : FrameworkElement
    {
        private Rect _zuletzt = Rect.Empty;

        public void LageGeaendert()
        {
            if (Ausschnitt() is { } a && a != _zuletzt) InvalidateVisual();
        }

        private Rect? Ausschnitt()
        {
            if (grund.Quelle is not Visual q || grund.Visibility != Visibility.Visible || VisualTreeHelper.GetParent(this) is null) return null;
            try
            {
                if (q.FindCommonVisualAncestor(this) is null) return null;
                return TransformToVisual(q).TransformBounds(new Rect(RenderSize));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (grund.Quelle is not Visual q || Ausschnitt() is not { } a) return;
            _zuletzt = a;
            drawingContext.DrawRectangle(new VisualBrush(q) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = a, Stretch = Stretch.Fill }, null, new Rect(RenderSize));
        }
    }
}
