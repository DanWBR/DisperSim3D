#nullable enable
using System;
using System.Numerics;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Turntable-style perspective camera for the 3D viewport.
    /// Z-up coordinate system matching the engine and WPF viewport convention.
    /// </summary>
    internal sealed class GlCamera
    {
        /// <summary>Horizontal angle (radians) around the Z axis.</summary>
        public float Azimuth { get; set; } = MathF.PI / 4f; // 45 deg

        /// <summary>Vertical angle (radians) from the XY plane.</summary>
        public float Elevation { get; set; } = MathF.PI / 6f; // 30 deg

        /// <summary>Distance from the camera to the target point.</summary>
        public float Distance { get; set; } = 120f;

        /// <summary>The point the camera orbits around (world space).</summary>
        public Vector3 Target { get; set; } = Vector3.Zero;

        /// <summary>Vertical field of view in radians.</summary>
        public float FieldOfView { get; set; } = MathF.PI / 4f; // 45 deg

        public float NearPlane { get; set; } = 0.1f;
        public float FarPlane { get; set; } = 10000f;

        // ── Derived properties ──────────────────────────────────────────

        /// <summary>World-space position of the camera eye.</summary>
        public Vector3 Eye
        {
            get
            {
                float ce = MathF.Cos(Elevation);
                return Target + Distance * new Vector3(
                    ce * MathF.Cos(Azimuth),
                    ce * MathF.Sin(Azimuth),
                    MathF.Sin(Elevation));
            }
        }

        /// <summary>World-space up vector (Z-up).</summary>
        public static Vector3 WorldUp => Vector3.UnitZ;

        /// <summary>View (camera) matrix.</summary>
        public Matrix4x4 ViewMatrix =>
            Matrix4x4.CreateLookAt(Eye, Target, WorldUp);

        /// <summary>Perspective projection matrix for the given aspect ratio.</summary>
        public Matrix4x4 ProjectionMatrix(float aspectRatio) =>
            Matrix4x4.CreatePerspectiveFieldOfView(
                FieldOfView, aspectRatio, NearPlane, FarPlane);

        // ── Camera manipulation ─────────────────────────────────────────

        /// <summary>
        /// Rotate the camera around the target (turntable orbit).
        /// Positive <paramref name="dAzimuth"/> rotates counter-clockwise
        /// when viewed from above; positive <paramref name="dElevation"/>
        /// tilts the camera upward.
        /// </summary>
        public void Orbit(float dAzimuth, float dElevation)
        {
            Azimuth -= dAzimuth;
            Elevation = Math.Clamp(
                Elevation + dElevation,
                -MathF.PI / 2f + 0.02f,   // don't go perfectly vertical
                 MathF.PI / 2f - 0.02f);
        }

        /// <summary>
        /// Move the target (and camera) perpendicular to the view direction.
        /// </summary>
        public void Pan(float dx, float dy)
        {
            var forward = Vector3.Normalize(Target - Eye);
            var right   = Vector3.Normalize(Vector3.Cross(forward, WorldUp));
            var camUp   = Vector3.Cross(right, forward);

            float scale = Distance * 0.002f;
            Target += right * (-dx * scale) + camUp * (dy * scale);
        }

        /// <summary>
        /// Zoom in/out by changing the orbit distance.
        /// Positive <paramref name="delta"/> zooms in.
        /// </summary>
        public void Zoom(float delta)
        {
            Distance *= MathF.Pow(1.1f, -delta);
            Distance = Math.Clamp(Distance, 0.5f, 50000f);
        }

        /// <summary>Reset to the default isometric-style view.</summary>
        public void Reset()
        {
            Azimuth   = MathF.PI / 4f;
            Elevation = MathF.PI / 6f;
            Distance  = 120f;
            Target    = Vector3.Zero;
        }

        /// <summary>
        /// Apply a saved camera preset. Converts the preset's Position +
        /// LookDirection (WPF-style) to the turntable's Azimuth / Elevation /
        /// Distance / Target representation.
        /// </summary>
        public void ApplyPreset(DisperSim3D.Models.CameraPreset preset)
        {
            if (preset is null) return;

            var pos = new Vector3(
                (float)preset.Position.X,
                (float)preset.Position.Y,
                (float)preset.Position.Z);
            var look = new Vector3(
                (float)preset.LookDirection.X,
                (float)preset.LookDirection.Y,
                (float)preset.LookDirection.Z);

            float lookLen = look.Length();
            if (lookLen < 1e-6f) return;

            // Target = position + lookDirection (look is the direction vector)
            Target = pos + look;
            Distance = lookLen;

            // Compute direction from eye to target
            var dir = Vector3.Normalize(look);
            Elevation = MathF.Asin(Math.Clamp(dir.Z, -1f, 1f));
            Azimuth = MathF.Atan2(dir.Y, dir.X) + MathF.PI; // flip: camera looks toward target
        }
    }
}
