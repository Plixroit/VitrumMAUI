namespace Vitrum;

/// <summary>
/// A glass-within-glass pill overlay that samples the nearest
/// <see cref="BlurConsumerView"/> parent's glass surface and applies
/// its own lens refraction to produce a second-order glass depth effect.
/// Android 13+ only; no-op on older devices.
/// </summary>
public class PillGlassView : View
{
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(float), typeof(PillGlassView), 24f);

    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(nameof(TintColor), typeof(Color), typeof(PillGlassView), Colors.Transparent);

    /// <summary>
    /// When true the full glass effect renders every frame even while the pill is stationary.
    /// Use this when the pill tint colour needs to show through glass (e.g. Jaww navy tint).
    /// Default false (DChat behaviour: glass only while moving, solid tint when still).
    /// </summary>
    public static readonly BindableProperty ForceGlassProperty =
        BindableProperty.Create(nameof(ForceGlass), typeof(bool), typeof(PillGlassView), false);

    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public bool ForceGlass
    {
        get => (bool)GetValue(ForceGlassProperty);
        set => SetValue(ForceGlassProperty, value);
    }
}
