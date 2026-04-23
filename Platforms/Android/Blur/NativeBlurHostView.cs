using Android.Content;
using Android.Graphics;
using Microsoft.Maui.Platform;

namespace Vitrum.Android;

/// <summary>
/// Native Android view that owns the <see cref="BlurEngine"/>.
/// Intercepts <c>DispatchDraw</c> to capture the background content before
/// the normal draw pass renders the consumers on top.
/// </summary>
public class NativeBlurHostView : ContentViewGroup
{
    public BlurEngine Engine { get; }

    public NativeBlurHostView(Context context) : base(context)
    {
        Engine = new BlurEngine(this);
        SetWillNotDraw(false);
    }

    protected override void DispatchDraw(Canvas? canvas)
    {
        if (canvas != null)
            Engine.CaptureChild(canvas);

        base.DispatchDraw(canvas);
    }
}
