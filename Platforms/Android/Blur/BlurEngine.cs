using Android.Graphics;
using Android.OS;
using Android.Views;

namespace Vitrum.Android;

/// <summary>
/// Core GPU blur engine using Android 12+ <c>RenderNode</c> + <c>RenderEffect</c>.
/// </summary>
/// <remarks>
/// Scale and blur radius are tuned for high-performance devices:
/// <list type="bullet">
///   <item>Node size = screen in dp (width / density, height / density)</item>
///   <item>Content drawn at 1/density scale into the node</item>
///   <item>BlurRadius 60 applied in dp coordinate space</item>
///   <item>Node drawn back at density scale — visual blur = 60dp on screen</item>
/// </list>
/// On older devices the consumer falls back to drawing a flat tint color rectangle.
/// </remarks>
public class BlurEngine
{
    /// <summary>Saturation multiplier chained after the blur for the frosted glass look.</summary>
    const float Saturation = 2f;

    readonly NativeBlurHostView _host;
    readonly List<WeakReference<NativeBlurConsumerView>> _consumers = new();

    RenderNode? _blurNode;
    float _lastDensity;
    float _blurRadiusDp = 60f;
    int _captureBackground = 0; // transparent by default

    public BlurEngine(NativeBlurHostView host) => _host = host;

    /// <summary>
    /// Color pre-filled into the recording canvas before drawing content.
    /// Set to the page background color for a denser frosted effect.
    /// </summary>
    public void SetCaptureBackground(int argb)
    {
        _captureBackground = argb;
        _blurNode = null;
        InvalidateConsumers();
    }

    /// <summary>Updates the blur radius (in dp) and invalidates the cached RenderNode.</summary>
    public void SetBlurRadius(float dp)
    {
        _blurRadiusDp = dp;
        _blurNode = null; // force recreate with new radius
        InvalidateConsumers();
    }

    /// <summary>Registers a consumer to receive the blurred texture.</summary>
    public void RegisterConsumer(NativeBlurConsumerView consumer)
        => _consumers.Add(new WeakReference<NativeBlurConsumerView>(consumer));

    /// <summary>Unregisters a consumer (called on handler disconnect).</summary>
    public void UnregisterConsumer(NativeBlurConsumerView consumer)
        => _consumers.RemoveAll(w => !w.TryGetTarget(out var v) || v == consumer);

    /// <summary>
    /// Called from <see cref="NativeBlurHostView.DispatchDraw"/> on every frame.
    /// Hides consumers, records the background content into the blur RenderNode,
    /// then restores consumer visibility.
    /// </summary>
    public void CaptureChild(Canvas hostCanvas)
    {
        if (!hostCanvas.IsHardwareAccelerated) return;
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return;
        if (_host.ChildCount == 0) return;

        var content = _host.GetChildAt(0);
        if (content == null || content.Width == 0 || content.Height == 0) return;

        float density = _host.Context!.Resources!.DisplayMetrics!.Density;

        // Node size in dp — screen pixels divided by density
        int bw = Math.Max(1, (int)(_host.Width / density));
        int bh = Math.Max(1, (int)(_host.Height / density));

        if (_blurNode == null || density != _lastDensity)
        {
            _lastDensity = density;
            _blurNode = new RenderNode("vitrum_blur");
            var sat = new ColorMatrix();
            sat.SetSaturation(Saturation);
            _blurNode.SetRenderEffect(
                RenderEffect.CreateChainEffect(
                    RenderEffect.CreateBlurEffect(_blurRadiusDp, _blurRadiusDp, Shader.TileMode.Decal!),
                    RenderEffect.CreateColorFilterEffect(new ColorMatrixColorFilter(sat))));
        }

        _blurNode.SetPosition(0, 0, bw, bh);

        SetConsumersVisible(false);
        var rc = _blurNode.BeginRecording(bw, bh);
        rc.Scale(1f / density, 1f / density);
        // Pre-fill with background color before content — produces denser blur base
        if (_captureBackground != 0)
            rc.DrawColor(new global::Android.Graphics.Color(_captureBackground));
        content.Draw(rc);
        _blurNode.EndRecording();
        SetConsumersVisible(true);
    }

    /// <summary>
    /// Called from <see cref="NativeBlurConsumerView.DispatchDraw"/>.
    /// Clips the canvas, aligns to host origin, draws the blur node at density scale,
    /// then draws the tint overlay.
    /// </summary>
    public void DrawBlurOnto(Canvas canvas, int consLeft, int consTop,
                             int consWidth, int consHeight, int tintColor)
    {
        using var scrimPaint = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
        scrimPaint.Color = new global::Android.Graphics.Color(tintColor);

        canvas.Save();
        canvas.ClipRect(0, 0, consWidth, consHeight);

        if (_blurNode != null && Build.VERSION.SdkInt >= BuildVersionCodes.S
                               && canvas.IsHardwareAccelerated)
        {
            float density = _host.Context!.Resources!.DisplayMetrics!.Density;

            // Step 1: blur at 100% opacity
            _blurNode.SetAlpha(1f);
            canvas.Save();
            canvas.Scale(density, density);
            canvas.Translate(-(consLeft / density), -(consTop / density));
            canvas.DrawRenderNode(_blurNode);
            canvas.Restore();
        }

        // Step 2: scrim tint ON TOP — tintColor carries the alpha (e.g. 0xB2 = 70% for #B21C1C25)
        canvas.DrawRect(0, 0, consWidth, consHeight, scrimPaint);

        canvas.Restore();
    }

    /// <summary>Returns the host view's position in the window coordinate space.</summary>
    public void GetHostLocationInWindow(int[] outLoc)
        => _host.GetLocationInWindow(outLoc);

    void InvalidateConsumers()
    {
        foreach (var w in _consumers)
            if (w.TryGetTarget(out var c))
                c.Invalidate();
    }

    void SetConsumersVisible(bool visible)
    {
        var state = visible ? ViewStates.Visible : ViewStates.Invisible;
        foreach (var w in _consumers)
            if (w.TryGetTarget(out var c))
                c.Visibility = state;
    }
}
