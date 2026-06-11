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
uniform float rimWidth;
uniform float2 lightDir;

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
    // Disperse along the SDF gradient so the rainbow fringe follows the whole
    // pill edge (straight runs included), matching the panel shader.
    float2 dispersedCoord = d * grad * chromaticAberration;

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

    // Glass bevel: SOLID bright band from the boundary to rimWidth with a hard
    // inner cutoff (1px anti-alias only, no feather) -- iOS edges are sharp.
    // The pow(4) angular falloff confines it to the top-left and bottom-right
    // CORNER ARCS only; without it the straight edges glow at 70% and the two
    // arcs merge into a full outline ring.
    float bevel      = 1.0 - smoothstep(rimWidth - 1.0, rimWidth, -sd);
    float lightDot   = pow(abs(dot(grad, lightDir)), 4.0);
    float hl         = bevel * lightDot * highlightStrength;
    half hlH         = half(hl);
    result.rgb       = min(result.rgb + half3(hlH, hlH, hlH), half3(1.0, 1.0, 1.0));

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

    // The iOS "swallow" on the bubble: the pill composites its layers minified
    // about its center, so it shows this much real content from beyond each
    // edge, squeezed in. The layers (panel glass, icons) extend past the pill
    // and are composited unblurred, so the compression is clearly visible.
    // 0 = off.
    const float PillSwallowDp = 6f;

    float _cornerRadiusPx;
    int _tintColor;
    bool _forceGlass = false;
    bool _shaderDirty = true;
    // One-shot guard: the panel squish is zeroed exactly once when the glass
    // pops to flat, so no displacement freezes around the resting pill.
    bool _squishCleared = true;

    // Motion detection — glass activates only while the pill is moving on screen.
    int[] _lastWindowLoc = new int[2];
    int _motionCooldown;
    // Short cooldown: after the finger lifts and the snap settles, the glass pops
    // back to the flat tint pill almost immediately (iOS vanishes the bubble fast).
    const int MotionCooldownFrames = 8;   // ~130 ms at 60 fps

    NativeBlurConsumerView? _navBarConsumer;
    global::Android.Views.View? _iconSourceView;

    RuntimeShader? _lensShader;
    int _lastWidth, _lastHeight;
    FrameInvalidator? _frameInvalidator;

    // Per-frame scratch buffers and paints (OnDraw runs every frame; avoid GC churn).
    readonly int[] _pillLoc = new int[2];
    readonly int[] _hostLoc = new int[2];
    global::Android.Graphics.Paint? _tintPaint;

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
        GlassLightSensor.Acquire(Context!);
        FindNavBarConsumer();
        _frameInvalidator ??= new FrameInvalidator(this);
        PostOnAnimation(_frameInvalidator);
    }

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        GlassLightSensor.Release();
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
        GetLocationInWindow(_pillLoc);
        if (_pillLoc[0] != _lastWindowLoc[0] || _pillLoc[1] != _lastWindowLoc[1])
            _motionCooldown = MotionCooldownFrames;
        else if (_motionCooldown > 0)
            _motionCooldown--;
        _lastWindowLoc[0] = _pillLoc[0];
        _lastWindowLoc[1] = _pillLoc[1];

        // Glass is purely press-driven: ForceGlass on = glass, off = flat on the
        // very next frame (iOS kills the bubble instantly on release; motion alone
        // never shows glass, so tap-snaps slide flat like iOS). The cooldown above
        // only drives the squish relax ramp while the glass is held.
        if (!_forceGlass)
        {
            DrawStaticPill(canvas);
            return;
        }
        _squishCleared = false;

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

        // Squish strength follows the motion cooldown: 1 while moving, ramping to 0
        // over ~500ms once the pill settles, so the panel displacement relaxes back
        // instead of staying frozen around the resting pill.
        _navBarConsumer.SetPillSquishInfo(
            offsetX + Width  / 2f,
            offsetY + Height / 2f,
            Width  / 2f,
            Height / 2f,
            _cornerRadiusPx,
            _motionCooldown / (float)MotionCooldownFrames);

        // The panel records the pill shadow and squish uniforms inside its own
        // DispatchDraw. On HW rendering the pill moving only re-records the pill's
        // display list, so the panel must be invalidated explicitly each glass frame
        // or the shadow and squish freeze at the last position the panel drew.
        _navBarConsumer.PostInvalidateOnAnimation();

        EnsureLensEffect();

        float density = Context!.Resources!.DisplayMetrics!.Density;
        _navBarConsumer.Engine?.GetHostLocationInWindow(_hostLoc);

        // Swallow: composite all layers minified about the pill center so the
        // bubble shows PillSwallowDp of real content from beyond each edge,
        // squeezed in (iOS bubble edge compression). Plain canvas transform,
        // no shader/coordinate remapping. Tint is drawn after the restore.
        float swPx = PillSwallowDp * density;
        float swKx = Width  / (Width  + 2f * swPx);
        float swKy = Height / (Height + 2f * swPx);
        canvas.Save();
        canvas.Translate(Width / 2f, Height / 2f);
        canvas.Scale(swKx, swKy);
        canvas.Translate(-Width / 2f, -Height / 2f);

        // Layer 0: engine blur — everything behind the pill, blurred.
        var engine = _navBarConsumer.Engine;
        if (engine != null)
            engine.DrawBlurNodeOnto(canvas, _pillLoc[0] - _hostLoc[0], _pillLoc[1] - _hostLoc[1], density);

        // Layer 1: panel glass at 75% opacity so layer 0 shows through for depth.
        // The alpha layer rect is expanded by the swallow margin so it still
        // covers the full pill after the minify transform.
        var glassNode = _navBarConsumer.GlassLayerNode;
        if (glassNode != null)
        {
            canvas.SaveLayerAlpha(-swPx, -swPx, Width + swPx, Height + swPx, 190);
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

        canvas.Restore();

        int tintA = (_tintColor >> 24) & 0xFF;
        if (tintA != 0)
        {
            _tintPaint ??= new global::Android.Graphics.Paint(PaintFlags.AntiAlias);
            _tintPaint.Color = new global::Android.Graphics.Color(_tintColor);
            canvas.DrawRect(0, 0, Width, Height, _tintPaint);
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

        // Glass just popped to flat: zero the panel squish immediately or the
        // displacement field freezes mid-deformation around the resting pill.
        if (!_squishCleared && _navBarConsumer != null)
        {
            _squishCleared = true;
            _navBarConsumer.SetPillSquishInfo(0f, 0f, 0f, 0f, 0f, 0f);
            _navBarConsumer.PostInvalidateOnAnimation();
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

    float _lastLightX, _lastLightY;

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    void EnsureLensEffect()
    {
        bool firstTime = _lensShader == null;
        _lensShader ??= new RuntimeShader(LensShaderSource);

        // CRITICAL: RenderEffect snapshots the shader's uniform values at
        // SetRenderEffect time; uniform writes after that are IGNORED until the
        // effect is re-applied. All uniforms must be set BEFORE SetRenderEffect,
        // and the effect must be re-applied when the tilt light axis changes or
        // the rim freezes at the last applied direction.
        float lx = GlassLightSensor.LightX, ly = GlassLightSensor.LightY;
        bool lightChanged = lx != _lastLightX || ly != _lastLightY;

        if (firstTime || Width != _lastWidth || Height != _lastHeight || _shaderDirty || lightChanged)
        {
            _lastWidth = Width;
            _lastHeight = Height;
            _shaderDirty = false;
            _lastLightX = lx;
            _lastLightY = ly;

            float density = Context!.Resources!.DisplayMetrics!.Density;
            float r = _cornerRadiusPx;

            _lensShader.SetFloatUniform("size", Width, Height);
            _lensShader.SetFloatUniform("offset", 0f, 0f);
            _lensShader.SetFloatUniform("cornerRadii", r, r, r, r);
            // The pill is the most refractive element on screen, but still subtle:
            // a clear lens, not a fisheye.
            _lensShader.SetFloatUniform("refractionHeight", 12f * density);
            _lensShader.SetFloatUniform("refractionAmount", -20f * density);
            _lensShader.SetFloatUniform("depthEffect", 0.15f);
            _lensShader.SetFloatUniform("chromaticAberration", 0.15f);
            // Near-neutral grading: the sampled panel glass is already saturated by
            // the engine (2x) and graded by the panel shader; grading again here
            // stacks white-point lifts and washes the pill out.
            _lensShader.SetFloatUniform("contrast", 0f);
            _lensShader.SetFloatUniform("whitePoint", 0f);
            _lensShader.SetFloatUniform("chromaMultiplier", 1f);
            // rimWidth is in physical px on purpose: a crisp 3px edge line.
            _lensShader.SetFloatUniform("highlightStrength", 0.35f);
            _lensShader.SetFloatUniform("rimWidth", 3f);

            // World-anchored light axis from the device tilt sensor; set before
            // the effect is applied so the snapshot carries the live direction.
            _lensShader.SetFloatUniform("lightDir", lx, ly);

            // Re-apply the RenderEffect on EVERY size/uniform change, not just once.
            // Android caches the applied effect at the recording size it was created
            // for; with a stale effect the shader keeps outputting transparent outside
            // the OLD rounded rect, so the pill looks frozen at its old size while
            // the view itself grows (same gotcha as the consumer's lens node).
            SetRenderEffect(RenderEffect.CreateRuntimeShaderEffect(_lensShader, "content"));
        }
    }
}
