using Android.Content;
using Android.Hardware;
using Android.Runtime;

namespace Vitrum.Android;

/// <summary>
/// Shared device-tilt light source for the glass shaders. Derives a screen-space
/// light axis from the gravity sensor so the specular rim slides around the
/// glass as the phone tilts, matching iOS liquid glass. The axis is world-anchored:
/// rolling the device clockwise moves the highlight counter-clockwise.
/// </summary>
public sealed class GlassLightSensor : Java.Lang.Object, ISensorEventListener
{
    static GlassLightSensor? _instance;
    static int _refCount;

    /// <summary>Raised on the UI thread when the light axis changed noticeably.</summary>
    public static event EventHandler? Updated;

    // Normalized screen-space light axis (canvas coords, y down).
    // Defaults to the static top-left 45 degrees until the sensor reports.
    public static float LightX { get; private set; } = -0.70710678f;
    public static float LightY { get; private set; } = -0.70710678f;

    // Light axis angle when the phone is upright and level: top-left.
    const float BaseAngle = -2.35619449f;   // -3*PI/4

    SensorManager? _sensorManager;
    float _roll;    // smoothed in-plane rotation (radians)
    float _pitch;   // smoothed forward/back tilt (radians)

    /// <summary>Starts the sensor when the first glass view attaches. Refcounted.</summary>
    public static void Acquire(Context context)
    {
        _refCount++;
        if (_instance != null) return;

        var sm = context.GetSystemService(Context.SensorService) as SensorManager;
        var sensor = sm?.GetDefaultSensor(SensorType.Gravity)
                     ?? sm?.GetDefaultSensor(SensorType.Accelerometer);
        if (sm == null || sensor == null) return;

        _instance = new GlassLightSensor { _sensorManager = sm };
        sm.RegisterListener(_instance, sensor, SensorDelay.Game);
    }

    /// <summary>Stops the sensor when the last glass view detaches.</summary>
    public static void Release()
    {
        if (_refCount > 0) _refCount--;
        if (_refCount != 0 || _instance == null) return;
        _instance._sensorManager?.UnregisterListener(_instance);
        _instance.Dispose();
        _instance = null;
    }

    public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy) { }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values == null || e.Values.Count < 3) return;
        float ax = e.Values[0], ay = e.Values[1], az = e.Values[2];

        float norm3 = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
        if (norm3 < 0.5f) return;

        // Two rotation sources, both folded into ONE light angle (the iOS fake):
        //   roll  = rotating the phone in the screen plane
        //   pitch = tilting the screen toward (+z) / away (-z) from the user,
        //           treated as if it were the same in-plane rotation
        float mag2 = (float)Math.Sqrt(ax * ax + ay * ay);
        float pitch = (float)Math.Atan2(az, mag2);

        // Smooth via shortest angular path to avoid wrap spins.
        if (mag2 > 1.5f)
        {
            float roll = (float)Math.Atan2(ax, ay);
            float dRoll = (float)Math.Atan2(Math.Sin(roll - _roll), Math.Cos(roll - _roll));
            _roll += dRoll * 0.15f;
        }
        // Near-flat: roll is undefined noise, hold it and let pitch carry on.
        _pitch += (pitch - _pitch) * 0.15f;

        float phi = BaseAngle + _roll + _pitch;
        float lx = (float)Math.Cos(phi);
        float ly = (float)Math.Sin(phi);

        if (Math.Abs(lx - LightX) < 0.002f && Math.Abs(ly - LightY) < 0.002f) return;
        LightX = lx;
        LightY = ly;
        Updated?.Invoke(null, EventArgs.Empty);
    }
}
