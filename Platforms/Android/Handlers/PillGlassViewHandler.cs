using Vitrum.Android;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Vitrum.Android.Handlers;

public class PillGlassViewHandler : ViewHandler<PillGlassView, NativePillGlassView>
{
    public static IPropertyMapper<PillGlassView, PillGlassViewHandler> Mapper =
        new PropertyMapper<PillGlassView, PillGlassViewHandler>(ViewMapper)
        {
            [nameof(PillGlassView.CornerRadius)] = MapCornerRadius,
            [nameof(PillGlassView.TintColor)]    = MapTintColor,
        };

    public PillGlassViewHandler() : base(Mapper) { }

    protected override NativePillGlassView CreatePlatformView()
        => new NativePillGlassView(Context);

    protected override void ConnectHandler(NativePillGlassView platformView)
    {
        base.ConnectHandler(platformView);
        // Defer: sibling handlers (FlexLayout) may not be connected yet at this point.
        platformView.Post(() => platformView.SetIconSource(FindIconSource()));
    }

    protected override void DisconnectHandler(NativePillGlassView platformView)
    {
        platformView.SetIconSource(null);
        base.DisconnectHandler(platformView);
    }

    // VirtualView = PillGlassView, inside Border, inside Grid that also contains the FlexLayout with icons.
    global::Android.Views.View? FindIconSource()
    {
        var grid = VirtualView?.Parent?.Parent as Microsoft.Maui.Controls.Layout;
        if (grid == null) return null;

        foreach (var child in grid)
        {
            if (child is Microsoft.Maui.Controls.FlexLayout fl)
                return fl.Handler?.PlatformView as global::Android.Views.View;
        }
        return null;
    }

    static void MapCornerRadius(PillGlassViewHandler h, PillGlassView v)
    {
        float density = h.Context.Resources!.DisplayMetrics!.Density;
        h.PlatformView.SetCornerRadius(v.CornerRadius * density);
    }

    static void MapTintColor(PillGlassViewHandler h, PillGlassView v)
        => h.PlatformView.SetTintColor(v.TintColor.ToInt());
}
