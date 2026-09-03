// Real Life Sky — look through the phone as a window. The camera's orientation follows the device's real
// attitude (gyroscope + accelerometer fused by the OS: Input.gyro.attitude) and the true heading from the
// magnetometer (compass), so pointing the phone at the real Sun shows the rendered Sun at the same spot.
// Field of view matches the phone's real geometry (screen height vs typical viewing distance 30 cm) so
// angular sizes on screen equal the real ones at that distance ("1:1 sky"). Touch: drag to look around
// (fallback / manual), pinch to zoom (binocular mode) — zoom is honest: it changes FOV only.
using System;
using UnityEngine;

namespace RealLife.Sky
{
    public class SkyCamera : MonoBehaviour
    {
        public enum Mode { Gyro, Touch }
        public Mode mode = Mode.Gyro;

        [Tooltip("Viewing distance used for the 1:1 field of view (m).")]
        public float viewingDistanceM = 0.30f;
        public float minFov = 5f, maxFov = 110f;

        public float HeadingDeg { get; private set; }     // true heading of the camera forward (0=N, 90=E)
        public float PitchDeg { get; private set; }
        public bool GyroAvailable { get; private set; }
        public float CompassAccuracyDeg { get; private set; }

        Camera _cam;
        float _yaw, _pitch;                 // touch mode
        float _headingOffset;               // gyro yaw → true heading correction from compass (filtered)
        bool _headingInit;
        float _fov;
        float _pinchStartDist, _pinchStartFov;

        void Awake()
        {
            _cam = GetComponent<Camera>() ?? gameObject.AddComponent<Camera>();
            _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 2000f;
            _cam.clearFlags = CameraClearFlags.Skybox;
            Input.multiTouchEnabled = true;
        }

        void Start()
        {
            GyroAvailable = SystemInfo.supportsGyroscope;
            if (GyroAvailable) { Input.gyro.enabled = true; Input.gyro.updateInterval = 1f / 60f; }
            else mode = Mode.Touch;
            Input.compass.enabled = true;
            // 1:1 FOV: screen physical height h = pixels / dpi * 0.0254 ; fov = 2 atan(h/2 / d)
            float dpi = Screen.dpi > 0 ? Screen.dpi : 400f;
            float hM = Screen.height / dpi * 0.0254f;
            _fov = Mathf.Clamp(2f * Mathf.Atan2(hM * 0.5f, viewingDistanceM) * Mathf.Rad2Deg, minFov, maxFov);
            _cam.fieldOfView = _fov;
            _yaw = 180f; _pitch = 20f; // start looking south, a bit up
        }

        void Update()
        {
            HandleTouch();
            if (mode == Mode.Gyro && GyroAvailable) UpdateGyro(); else UpdateTouch();
            _cam.fieldOfView = _fov;
            Vector3 f = transform.forward;
            HeadingDeg = (Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 360f) % 360f;
            PitchDeg = Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg;
        }

        // Unity gyro attitude: right-handed, device frame; convert to left-handed world with +Y up, camera looking out of the back.
        void UpdateGyro()
        {
            Quaternion q = Input.gyro.attitude;
            Quaternion device = new Quaternion(q.x, q.y, -q.z, -q.w);
            Quaternion cam = Quaternion.Euler(90f, 0f, 0f) * device; // portrait: rotate so looking through the back camera
            // Heading correction: gyro yaw drifts / has arbitrary reference; use the magnetometer's true heading (with declination
            // applied by the OS in trueHeading). Filter slowly (compass is noisy), only when phone is roughly upright.
            Vector3 f = cam * Vector3.forward;
            float gyroHeading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float compassHeading = Input.compass.trueHeading;
            CompassAccuracyDeg = Input.compass.headingAccuracy;
            if (Input.compass.enabled && Input.compass.timestamp > 0 && Mathf.Abs(f.y) < 0.85f)
            {
                float desired = Mathf.DeltaAngle(gyroHeading, compassHeading);
                if (!_headingInit) { _headingOffset = desired; _headingInit = true; }
                else _headingOffset = Mathf.LerpAngle(_headingOffset, desired, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 3f));
            }
            transform.rotation = Quaternion.AngleAxis(_headingOffset, Vector3.up) * cam;
        }

        void UpdateTouch()
        {
            transform.rotation = Quaternion.Euler(-_pitch, _yaw, 0f);
        }

        void HandleTouch()
        {
            int n = Input.touchCount;
            if (n == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved)
                {
                    // 1 pixel = FOV/height degrees: drag moves the sky exactly under the finger
                    float degPerPx = _fov / Screen.height;
                    if (mode == Mode.Gyro) { mode = Mode.Touch; SyncTouchFromCurrent(); }
                    _yaw -= t.deltaPosition.x * degPerPx;
                    _pitch = Mathf.Clamp(_pitch + t.deltaPosition.y * degPerPx, -30f, 90f);
                }
                if (t.phase == TouchPhase.Began && t.tapCount >= 2 && GyroAvailable) mode = Mode.Gyro;
                _pinchStartDist = 0;
            }
            else if (n >= 2)
            {
                var a = Input.GetTouch(0); var b = Input.GetTouch(1);
                float d = Vector2.Distance(a.position, b.position);
                if (b.phase == TouchPhase.Began || _pinchStartDist <= 0) { _pinchStartDist = d; _pinchStartFov = _fov; }
                else if (d > 1f) _fov = Mathf.Clamp(_pinchStartFov * (_pinchStartDist / d), minFov, maxFov);
            }
            else _pinchStartDist = 0;
        }

        void SyncTouchFromCurrent()
        {
            Vector3 f = transform.forward;
            _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg;
        }

        public void ToggleMode()
        {
            if (mode == Mode.Gyro) { mode = Mode.Touch; SyncTouchFromCurrent(); }
            else if (GyroAvailable) mode = Mode.Gyro;
        }
    }
}
