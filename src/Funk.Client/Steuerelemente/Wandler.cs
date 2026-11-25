// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace Funk.Client.Steuerelemente;

/// <summary>bool zu Sichtbarkeit; mit <see cref="Umkehren"/> andersherum.</summary>
public sealed class SichtbarWenn : IValueConverter
{
    public bool Umkehren { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Umkehren ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Umkehren;
}

/// <summary>Sichtbar, wenn der Wert nicht null und kein leerer Text ist.</summary>
public sealed class SichtbarWennWert : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Schlüssel einer Ressource (etwa "Symbol.Mikrofon") zur Ressource selbst.</summary>
public sealed class Ressource : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string schluessel ? Application.Current?.TryFindResource(schluessel) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Kennung eines Zeichens oder Abzeichens ("ADMIN", "MARINE_04", "T_GUNNER_M") zu seinem Bild aus dem eingebauten Satz (seit 0.11.7,
/// <see cref="Design.Zeichensatz.Bild"/>), sonst <c>null</c>. Liegt als <c>AbzeichenBild</c> in Design\Stile.xaml: Darüber zeigt auch der
/// Verwaltungsteil die Bilder, ohne den Client zu kennen.
/// </summary>
public sealed class AbzeichenBild : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string kennung ? Design.Zeichensatz.Bild(kennung) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Knopf, der meldet, solange er gehalten wird (Maus oder Leertaste): für die Sprechtaste zum Ausprobieren. Die
/// Eigenschaft <see cref="Gehalten"/> lässt sich binden, <c>IsPressed</c> nicht.
/// </summary>
public sealed class Halteknopf : ButtonBase
{
    public static readonly DependencyProperty GehaltenProperty = DependencyProperty.Register(nameof(Gehalten), typeof(bool), typeof(Halteknopf),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Wird bei Beginn und Ende des Haltens aufgerufen (true, false).</summary>
    public event Action<bool>? GehaltenGeaendert;

    public bool Gehalten
    {
        get => (bool)GetValue(GehaltenProperty);
        set => SetValue(GehaltenProperty, value);
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        Gehalten = IsPressed;
        GehaltenGeaendert?.Invoke(IsPressed);
    }
}
