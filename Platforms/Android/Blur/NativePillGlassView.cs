using Android.Content;
using Android.Graphics;
using Android.OS;

namespace Vitrum.Android;

public class NativePillGlassView : global::Android.Views.View
{
    const string LensShaderSource = @"
uniform shader content;
uniform float2 size;
uniform float2 offset;
uniform float4 cornerRadii;
uniform float refractionHeight;
uniform float refractionAmount;
uniform float depthEffect;
uniform float chromaticAberration;
uniform float contrast;
uniform float whitePoint;
uniform float chromaMultiplier;
uniform float highlightStrength;

const half3 rgbToY = half3(0.2126, 0.7152, 0.0722);

float radiusAt(float2 centeredCoord, float4 radii) {
    if (centeredCoord.x >= 0.0) {
        if (centeredCoord.y <= 0.0) return radii.y;
        else return radii.z;
    } else {
        if (centeredCoord.y <= 0.0) return radii.x;
        else return radii.w;
    }
}

float sdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius));
    if (cornerCoord.x >= 0.0 || cornerCoord.y >= 0.0) {
        return sign(coord) * normalize(max(cornerCoord, 0.0));
    } else {
        float gradX = step(cornerCoord.y, cornerCoord.x);
        return sign(coord) * float2(gradX, 1.0 - gradX);
    }
}

float circleMap(float x) {
    return 1.0 - sqrt(1.0 - x * x);
}

half4 gradeColor(half4 color) {
    half3 lin = toLinearSrgb(color.rgb);
    float y = dot(lin, rgbToY);
    half3 gray = half3(y);
    half3 sat = fromLinearSrgb(mix(gray, lin, chromaMultiplier));
    half4 result = half4(sat, color.a);
    float3 target = (whitePoint > 0.0) ? float3(1.0) : float3(0.0);
    result.rgb = mix(result.rgb, target, abs(whitePoint));
    result.rgb = (result.rgb - 0.5) * (1.0 + contrast) + 0.5;
    return result;
}

half4 main(float2 coord) {
    float2 halfSize = size * 0.5;
    float2 centeredCoord = (coord + offset) - halfSize;
    float radius = radiusAt(centeredCoord, cornerRadii);

    float sd = sdRoundedRect(centeredCoord, halfSize, radius);

    // Transparent outside the rounded shape — gives proper curved pill edges.
    if (sd > 0.0) return half4(0.0);

    if (-sd >= refractionHeight) {
        return gradeColor(content.eval(coord));
    }

    sd = min(sd, 0.0);
    float d = circleMap(1.0 - -sd / refractionHeight) * refractionAmount;
    float smoothRadius = max(radius * 1.5, 30.0);
    float gradRadius = min(smoothRadius, min(halfSize.x, halfSize.y));
    float2 grad = normalize(gradSdRoundedRect(centeredCoord, halfSize, gradRadius) + depthEffect * normalize(centeredCoord));

    float2 refractedCoord = coord + d * grad;
    float dispersionIntensity = chromaticAberration * ((centeredCoord.x * centeredCoord.y) / (halfSize.x * halfSize.y));
    float2 dispersedCoord = d * grad * dispersionIntensity;

    half4 color = half4(0.0);
    half4 red    = content.eval(refractedCoord + dispersedCoord);
    color.r += red.r / 3.5;  color.a += red.a / 7.0;
    half4 orange = content.eval(refractedCoord + dispersedCoord * (2.0 / 3.0));
    color.r += orange.r / 3.5;  color.g += orange.g / 7.0;  color.a += orange.a / 7.0;
    half4 yellow = content.eval(refractedCoord + dispersedCoord * (1.0 / 3.0));
    color.r += yellow.r / 3.5;  color.g += yellow.g / 3.5;  color.a += yellow.a / 7.0;
    half4 green  = content.eval(refractedCoord);
    color.g += green.g / 3.5;  color.a += green.a / 7.0;
    half4 cyan   = content.eval(refractedCoord - dispersedCoord * (1.0 / 3.0));
    color.g += cyan.g / 3.5;  color.b += cyan.b / 3.0;  color.a += cyan.a / 7.0;
    half4 blue   = content.eval(refractedCoord - dispersedCoord * (2.0 / 3.0));
    color.b += blue.b / 3.0;  color.a += blue.a / 7.0;
    half4 purple = content.eval(refractedCoord - dispersedCoord);
    color.r += purple.r / 7.0;  color.b += purple.b / 3.0;  color.a += purple.a / 7.0;

    half4 result = gradeColor(color);

    // Directional glass rim -- light from top-left, shadow on bottom-right.
    // grad is the outward surface normal (computed above). dot(grad, lightDir)
    // is 1 at the top-left corner, 0 on right/bottom edges, negative elsewhere.
    float rimFactor  = 1.0 - smoothstep(0.0, 2.0, -sd);
    float2 lightDir  = normalize(float2(-0.7, -0.7));
    float lightDot   = abs(dot(grad, lightDir));
    float hl         = rimFactor * lightDot * highlightStrength;
    result.rgb       = mix(result.rgb, half3(1.0, 1.0, 1.0), half(hl * 0.5));

    return result;
}";

    // Fires in ANIMATION phase (same phase as MAUI ValueAnimator), so the view is
    // marked dirty before TRAVERSAL runs OnDraw. This eliminates the 1-frame lag
    // that PostInvalidateOnAnimation() caused (it scheduled for NEXT TRAVERSAL,
    // one phase after the animator already moved the view).
    sealed class FrameInvalidator : Java.Lang.Object, Java.Lang.IRunnable
    {
        readonly WeakReference<NativePillGlassView> _ref;
        internal FrameInvalidator(NativePillGlassView v) => _ref = new(v);
        public void Run()
        {
            if (_ref.TryGetTarget(out var v) && v.IsAttachedToWindow)
            {
                v.Invalidate();
                v.PostOnAnimation(this);
            }
        }
    }

    float _cornerRadiusPx;
    int _tintColor;
    bool _forceGlass = false;
    bool _shaderDirty = true;

    // Motion detection — glass activates only while the pill is moving on screen.
    int[] _lastWindowLoc = new int[2];
    int _motionCooldown;
    const int MotionCooldownFrames = 30;  // ~500 ms at 60 fps

    NativeBlurConsumerView? _navBarConsumer;
    global::Android.Views.View? _iconSourceView;

    RuntimeShader? _lensShader;
    int _lastWidth, _lastHeight;
    FrameInvalidator? _frameInvalidator;

    public NativePillGlassView(Context context) : base(context)
    {
        SetWillNotDraw(false);
    }

    public override bool OnTouchEvent(global::Android.Views.MotionEvent? e) => false;

    public void SetCornerRadius(float px) { _cornerRadiusPx = px; _shaderDirty = true; Invalidate(); }
    public void SetTintColor(int argb) { _tintColor = argb; Invalidate(); }
    public void SetForceGlass(bool force) { _forceGlass = force; Invalidate(); }
    public void SetIconSource(global::Android.Views.View? view) { _iconSourceView = view; Invalidate(); }
    public void InvalidateIconCapture() { }

    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        FindNavBarConsumer();
        _frameInvalidator ??= new FrameInvalidator(this);
        PostOnAnimation(_frameInvalidator);
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        _navBarConsumer = null;
    }

    void FindNavBarConsumer()
    {
        var p = Parent;
        while (p != null)
        {
            if (p is NativeBlurConsumerView c) { _navBarConsumer = c; return; }
            p = (p as global::Android.Views.View)?.Parent;
        }
    }

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas == null) return;

        if (_navBarConsumer != null
            && canvas.IsHardwareAccelerated
            && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            DrawPillGlass(canvas);
        }
        else if (_tintColor != 0)
        {
            using var p = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
            p.Color = new global::Android.Graphics.Color(_tintColor);
            canvas.DrawRect(0, 0, Width, Height, p);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void DrawPillGlass(Canvas canvas)
    {
        if (Width == 0 || Height == 0) return;

        // Detect motion by comparing the pill's screen position each frame.
        int[] pillLoc = new int[2];
        GetLocationInWindow(pillLoc);
        if (pillLoc[0] != _lastWindowLoc[0] || pillLoc[1] != _lastWindowLoc[1])
            _motionCooldown = MotionCooldownFrames;
        else if (_motionCooldown > 0)
            _motionCooldown--;
        _lastWindowLoc[0] = pillLoc[0];
        _lastWindowLoc[1] = pillLoc[1];

        bool isMoving = _motionCooldown > 0 || _forceGlass;

        if (!isMoving)
        {
            DrawStaticPill(canvas);
            return;
        }

        // Moving: full glass effect.
        // Float offset walk for sub-pixel accuracy when sampling the panel glass node.
        float offsetX = 0f, offsetY = 0f;
        for (global::Android.Views.View? v = this;
             v != null && v != (global::Android.Views.View)(object)_navBarConsumer;
             v = v.Parent as global::Android.Views.View)
        {
            offsetX += v.GetX();
            offsetY += v.GetY();
        }

        _navBarConsumer.SetPillSquishInfo(
            offsetX + Width  / 2f,
            offsetY + Height / 2f,
            Width  / 2f,
            Height / 2f,
            _cornerRadiusPx);

        EnsureLensEffect();

        float density = Context!.Resources!.DisplayMetrics!.Density;
        int[] hostLoc = new int[2];
        _navBarConsumer.Engine?.GetHostLocationInWindow(hostLoc);

        // Layer 0: engine blur — everything behind the pill, blurred.
        var engine = _navBarConsumer.Engine;
        if (engine != null)
            engine.DrawBlurNodeOnto(canvas, pillLoc[0] - hostLoc[0], pillLoc[1] - hostLoc[1], density);

        // Layer 1: panel glass at 75% opacity so layer 0 shows through for depth.
        var glassNode = _navBarConsumer.GlassLayerNode;
        if (glassNode != null)
        {
            canvas.SaveLayerAlpha(0, 0, Width, Height, 190);
            canvas.Save();
            canvas.Translate(-offsetX, -offsetY);
            canvas.DrawRenderNode(glassNode);
            canvas.Restore();
            canvas.Restore();
        }

        // Layer 2: icons drawn fresh each frame so the lens magnifies them correctly.
        if (_iconSourceView != null)
        {
            canvas.Save();
            canvas.Translate(-offsetX, -offsetY);
            _iconSourceView.Draw(canvas);
            canvas.Restore();
        }

        int tintA = (_tintColor >> 24) & 0xFF;
        if (tintA != 0)
        {
            using var tp = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
            tp.Color = new global::Android.Graphics.Color(_tintColor);
            canvas.DrawRect(0, 0, Width, Height, tp);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void DrawStaticPill(Canvas canvas)
    {
        // Remove lens shader — no glass while still.
        if (_lensShader != null)
        {
            SetRenderEffect(null);
            _lensShader = null;
            _shaderDirty = true;
        }

        // Clip to rounded pill shape.
        using var path = new global::Android.Graphics.Path();
        path.AddRoundRect(
            new global::Android.Graphics.RectF(0, 0, Width, Height),
            _cornerRadiusPx, _cornerRadiusPx,
            global::Android.Graphics.Path.Direction.Cw!);
        canvas.ClipPath(path);

        // Layer 0: solid tint fill (e.g. navy #FF1A2847 = solid navy).
        if (_tintColor != 0)
        {
            using var p = new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
            p.Color = new global::Android.Graphics.Color(_tintColor);
            canvas.DrawPaint(p);
        }

        // Layer 1: draw icon source on top so the active tab icon is visible
        // through the solid fill — same offset walk as DrawPillGlass.
        if (_iconSourceView != null)
        {
            float offsetX = 0f, offsetY = 0f;
            for (global::Android.Views.View? v = this;
                 v != null && v != (global::Android.Views.View?)(object?)_navBarConsumer;
                 v = v.Parent as global::Android.Views.View)
            {
                offsetX += v.GetX();
                offsetY += v.GetY();
            }
            canvas.Save();
            canvas.Translate(-offsetX, -offsetY);
            _iconSourceView.Draw(canvas);
            canvas.Restore();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void EnsureLensEffect()
    {
        bool firstTime = _lensShader == null;
        _lensShader ??= new RuntimeShader(LensShaderSource);

        if (firstTime || Width != _lastWidth || Height != _lastHeight || _shaderDirty)
        {
            _lastWidth = Width;
            _lastHeight = Height;
            _shaderDirty = false;

            float density = Context!.Resources!.DisplayMetrics!.Density;
            float r = _cornerRadiusPx;

            _lensShader.SetFloatUniform("size", Width, Height);
            _lensShader.SetFloatUniform("offset", 0f, 0f);
            _lensShader.SetFloatUniform("cornerRadii", r, r, r, r);
            _lensShader.SetFloatUniform("refractionHeight", 10f * density);
            _lensShader.SetFloatUniform("refractionAmount", -14f * density);
            _lensShader.SetFloatUniform("depthEffect", 0f);
            _lensShader.SetFloatUniform("chromaticAberration", 1f);
            _lensShader.SetFloatUniform("contrast", 0.05f);
            _lensShader.SetFloatUniform("whitePoint", 0.05f);
            _lensShader.SetFloatUniform("chromaMultiplier", 1.1f);
            _lensShader.SetFloatUniform("highlightStrength", 0.8f);

            // Apply lens as a view-level RenderEffect (set once, reuses same shader object
            // so uniform updates are picked up automatically each frame with zero recording overhead).
            if (firstTime)
                SetRenderEffect(RenderEffect.CreateRuntimeShaderEffect(_lensShader, "content"));
        }
    }
}
