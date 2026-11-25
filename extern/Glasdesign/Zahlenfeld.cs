// Glasdesign - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Glasdesign;

/// <summary>
/// Zahlenfeld (Abschnitt 8): eine Zahl eintippen oder mit den Pfeilen, den Pfeiltasten, Bild auf und ab (zehnfach) und dem
/// Mausrad stellen. Baut auf <see cref="RangeBase"/> (Minimum, Maximum, Value, SmallChange, LargeChange), gezeichnet wie
/// das Textfeld (Stil Glas.Zahlenfeld). Eingetippt wird in der Sprache von Windows, Komma und Punkt gelten beide; Enter oder
/// das Verlassen übernimmt, Escape verwirft. Was sich nicht lesen lässt, färbt den Rand (<see cref="Ungueltig"/>) und bleibt
/// stehen, bis es berichtigt oder verworfen ist.
/// </summary>
[TemplatePart(Name = "PART_Text", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Hoch", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Runter", Type = typeof(ButtonBase))]
public sealed class Zahlenfeld : RangeBase
{
    public static readonly DependencyProperty StellenProperty = DependencyProperty.Register(
        nameof(Stellen), typeof(int), typeof(Zahlenfeld), new FrameworkPropertyMetadata(0, (d, _) => ((Zahlenfeld)d).TextZeigen()), w => (int)w is >= 0 and <= 10);

    private static readonly DependencyPropertyKey UngueltigKey = DependencyProperty.RegisterReadOnly(
        nameof(Ungueltig), typeof(bool), typeof(Zahlenfeld), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty UngueltigProperty = UngueltigKey.DependencyProperty;

    private TextBox? _text;

    static Zahlenfeld()
    {
        MaximumProperty.OverrideMetadata(typeof(Zahlenfeld), new FrameworkPropertyMetadata(100.0));
        SmallChangeProperty.OverrideMetadata(typeof(Zahlenfeld), new FrameworkPropertyMetadata(1.0));
        LargeChangeProperty.OverrideMetadata(typeof(Zahlenfeld), new FrameworkPropertyMetadata(10.0));
        ValueProperty.OverrideMetadata(typeof(Zahlenfeld), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        FocusableProperty.OverrideMetadata(typeof(Zahlenfeld), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Nachkommastellen der Anzeige; der Wert selbst wird beim Übernehmen darauf gerundet.</summary>
    public int Stellen { get => (int)GetValue(StellenProperty); set => SetValue(StellenProperty, value); }

    /// <summary>Der eingetippte Text lässt sich nicht als Zahl lesen.</summary>
    public bool Ungueltig => (bool)GetValue(UngueltigProperty);

    /// <summary>Der angezeigte Text.</summary>
    public string Text => _text?.Text ?? Formatieren(Value);

    public override void OnApplyTemplate()
    {
        if (_text is not null)
        {
            _text.PreviewKeyDown -= Text_Taste;
            _text.LostKeyboardFocus -= Text_Verlassen;
            _text.PreviewMouseWheel -= Text_Rad;
        }
        base.OnApplyTemplate();
        _text = GetTemplateChild("PART_Text") as TextBox;
        if (GetTemplateChild("PART_Hoch") is ButtonBase hoch) hoch.Click += (_, _) => Hoch();
        if (GetTemplateChild("PART_Runter") is ButtonBase runter) runter.Click += (_, _) => Runter();
        if (_text is not null)
        {
            _text.PreviewKeyDown += Text_Taste;
            _text.LostKeyboardFocus += Text_Verlassen;
            _text.PreviewMouseWheel += Text_Rad;
        }
        TextZeigen();
    }

    /// <summary>Um <see cref="RangeBase.SmallChange"/> hinauf (mit <paramref name="gross"/> um LargeChange).</summary>
    public void Hoch(bool gross = false) => Setzen(Value + (gross ? LargeChange : SmallChange));

    /// <summary>Um <see cref="RangeBase.SmallChange"/> hinab (mit <paramref name="gross"/> um LargeChange).</summary>
    public void Runter(bool gross = false) => Setzen(Value - (gross ? LargeChange : SmallChange));

    /// <summary>Einen Text übernehmen wie nach Enter; false, wenn er keine Zahl ist (dann bleibt der Wert).</summary>
    public bool Uebernehmen(string text)
    {
        if (!Lesen(text, out var w))
        {
            SetValue(UngueltigKey, true);
            return false;
        }
        Setzen(w);
        return true;
    }

    /// <summary>Komma und Punkt als Trennzeichen, dazu die Sprache von Windows; Leerzeichen zählen nicht.</summary>
    public static bool Lesen(string? text, out double wert)
    {
        wert = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out wert) && double.IsFinite(wert)) return true;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out wert) && double.IsFinite(wert);
    }

    private void Setzen(double w)
    {
        Value = Math.Round(Math.Clamp(w, Minimum, Maximum), Stellen, MidpointRounding.AwayFromZero);
        SetValue(UngueltigKey, false);
        TextZeigen();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (_text is not { IsKeyboardFocused: true }) TextZeigen();
    }

    private string Formatieren(double w) => w.ToString("F" + Stellen.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);

    private void TextZeigen()
    {
        if (_text is not null) _text.Text = Formatieren(Value);
    }

    private void Text_Taste(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Uebernehmen(_text!.Text);
                _text.SelectAll();
                break;
            case Key.Escape:
                SetValue(UngueltigKey, false);
                TextZeigen();
                _text!.SelectAll();
                break;
            case Key.Up:
                Uebernehmen(_text!.Text);
                Hoch();
                break;
            case Key.Down:
                Uebernehmen(_text!.Text);
                Runter();
                break;
            case Key.PageUp:
                Hoch(gross: true);
                break;
            case Key.PageDown:
                Runter(gross: true);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Text_Verlassen(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!Uebernehmen(_text!.Text)) return;
        TextZeigen();
    }

    private void Text_Rad(object sender, MouseWheelEventArgs e)
    {
        // Nur mit dem Fokus im Feld, sonst stellte ein Bildlauf über die Seite nebenbei Zahlen um.
        if (!_text!.IsKeyboardFocused) return;
        if (e.Delta > 0) Hoch();
        else Runter();
        e.Handled = true;
    }
}
