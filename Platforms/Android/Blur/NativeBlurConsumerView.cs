using Android.Content;
using Android.Graphics;
using Microsoft.Maui.Platform;

namespace Vitrum.Android;

/// <summary>
/// Native Android view that renders the blurred texture produced by <see cref="BlurEngine"/>.
/// </summary>
/// <remarks>
/// On each draw pass it asks the engine for the host's window position, translates the
/// canvas so the blur region aligns with the consumer's screen position, draws the blur
/// <c>RenderNode</c>, then draws the tint overlay. Children (the consumer's MAUI content)
/// are drawn on top by <c>base.DispatchDraw</c>.
/// </remarks>
public class NativeBlurConsumerView : ContentViewGroup
{
    int _tintColor = unchecked((int)0x661C1C25);
    BlurEngine? _engine;

    public NativeBlurConsumerView(Context context) : base(context)
    {
        SetWillNotDraw(false);
    }

    public void SetTintColor(int argb) { _tintColor = argb; Invalidate(); }
    public void AttachEngine(BlurEngine engine) => _engine = engine;
    public void DetachEngine() => _engine = null;

    protected override void DispatchDraw(Canvas canvas)
    {
        if (_engine != null)
        {
            int[] myLoc = new int[2];
            GetLocationInWindow(myLoc);

            int[] hostLoc = new int[2];
            _engine.GetHostLocationInWindow(hostLoc);

            _engine.DrawBlurOnto(canvas,
                myLoc[0] - hostLoc[0],
                myLoc[1] - hostLoc[1],
                Width,
                Height,
                _tintColor);
        }

        base.DispatchDraw(canvas);
    }
}
