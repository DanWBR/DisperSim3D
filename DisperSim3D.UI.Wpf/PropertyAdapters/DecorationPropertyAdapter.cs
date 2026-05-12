using System;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.PropertyAdapters
{
    public class DecorationPropertyAdapter
    {
        private readonly Decoration3D _deco;
        private readonly Action _onChanged;

        public DecorationPropertyAdapter(Decoration3D deco, Action onChanged)
        {
            _deco = deco;
            _onChanged = onChanged;
        }

        [Category("General")]
        [DisplayName("Name")]
        [Description("Display name shown in the project tree.")]
        public string Name
        {
            get => _deco.Name;
            set { _deco.Name = value; _onChanged?.Invoke(); }
        }

        [Category("General")]
        [DisplayName("ID")]
        [ReadOnly(true)]
        [Description("Unique identifier (read-only).")]
        public string Id => _deco.Id;

        [Category("General")]
        [DisplayName("File")]
        [ReadOnly(true)]
        [Description("Path to the source 3D model file (read-only).")]
        public string FilePath => _deco.FilePath;

        [Category("Position")]
        [DisplayName("X")]
        [Description("East-west position (m).")]
        public double PosX
        {
            get => _deco.Position.X;
            set { _deco.Position = new Point3D(value, _deco.Position.Y, _deco.Position.Z); NotifyTransform(); }
        }

        [Category("Position")]
        [DisplayName("Y")]
        [Description("North-south position (m).")]
        public double PosY
        {
            get => _deco.Position.Y;
            set { _deco.Position = new Point3D(_deco.Position.X, value, _deco.Position.Z); NotifyTransform(); }
        }

        [Category("Position")]
        [DisplayName("Z")]
        [Description("Vertical position (m). Z = 0 is ground level.")]
        public double PosZ
        {
            get => _deco.Position.Z;
            set { _deco.Position = new Point3D(_deco.Position.X, _deco.Position.Y, value); NotifyTransform(); }
        }

        [Category("Rotation")]
        [DisplayName("Rotation X (deg)")]
        [Description("Pitch rotation around the X axis.")]
        public double RotX
        {
            get => _deco.Rotation.X;
            set { _deco.Rotation = new Vector3D(value, _deco.Rotation.Y, _deco.Rotation.Z); NotifyTransform(); }
        }

        [Category("Rotation")]
        [DisplayName("Rotation Y (deg)")]
        [Description("Roll rotation around the Y axis.")]
        public double RotY
        {
            get => _deco.Rotation.Y;
            set { _deco.Rotation = new Vector3D(_deco.Rotation.X, value, _deco.Rotation.Z); NotifyTransform(); }
        }

        [Category("Rotation")]
        [DisplayName("Rotation Z (deg)")]
        [Description("Yaw rotation around the vertical Z axis.")]
        public double RotZ
        {
            get => _deco.Rotation.Z;
            set { _deco.Rotation = new Vector3D(_deco.Rotation.X, _deco.Rotation.Y, value); NotifyTransform(); }
        }

        [Category("Transform")]
        [DisplayName("Scale")]
        [Description("Uniform scale factor (1.0 = original size).")]
        public double Scale
        {
            get => _deco.Scale;
            set { _deco.Scale = Math.Max(0.01, value); NotifyTransform(); }
        }

        [Category("Material")]
        [DisplayName("Override Material")]
        [Description("Enable to replace the original model material")]
        public bool UseCustomMaterial
        {
            get => _deco.UseCustomMaterial;
            set { _deco.UseCustomMaterial = value; _onChanged?.Invoke(); }
        }

        [Category("Material")]
        [DisplayName("Type")]
        [Description("Matte, Metallic, Glass, Emissive")]
        public MaterialType3D MaterialType
        {
            get => _deco.MaterialType;
            set { _deco.MaterialType = value; if (_deco.UseCustomMaterial) _onChanged?.Invoke(); }
        }

        [Category("Material")]
        [DisplayName("Color")]
        public System.Drawing.Color MaterialColor
        {
            get
            {
                var c = _deco.MaterialColor;
                return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
            }
            set
            {
                _deco.MaterialColor = Color.FromArgb(value.A, value.R, value.G, value.B);
                if (_deco.UseCustomMaterial) _onChanged?.Invoke();
            }
        }

        [Category("Material")]
        [DisplayName("Specular Power")]
        [Description("Shininess (1=broad, 200=tight)")]
        public double SpecularPower
        {
            get => _deco.SpecularPower;
            set { _deco.SpecularPower = Math.Max(1, Math.Min(200, value)); if (_deco.UseCustomMaterial) _onChanged?.Invoke(); }
        }

        [Category("Material")]
        [DisplayName("Opacity")]
        [Description("0.0 = transparent, 1.0 = opaque")]
        public double Opacity
        {
            get => _deco.Opacity;
            set { _deco.Opacity = Math.Max(0, Math.Min(1, value)); if (_deco.UseCustomMaterial) _onChanged?.Invoke(); }
        }

        [Category("Clipping")]
        [DisplayName("Enable Clip")]
        [Description("Clip the model along a plane to remove parts")]
        public bool ClipEnabled
        {
            get => _deco.ClipEnabled;
            set { _deco.ClipEnabled = value; _deco.ApplyClip(); _onChanged?.Invoke(); }
        }

        [Category("Clipping")]
        [DisplayName("Clip Axis")]
        public ClipAxis ClipAxis
        {
            get => _deco.ClipAxis;
            set { _deco.ClipAxis = value; if (_deco.ClipEnabled) { _deco.ApplyClip(); _onChanged?.Invoke(); } }
        }

        [Category("Clipping")]
        [DisplayName("Clip Value")]
        [Description("Position along the axis where the cut is made (in model-local coordinates)")]
        public double ClipValue
        {
            get => _deco.ClipValue;
            set { _deco.ClipValue = value; if (_deco.ClipEnabled) { _deco.ApplyClip(); _onChanged?.Invoke(); } }
        }

        [Category("Clipping")]
        [DisplayName("Keep Above")]
        [Description("True = keep geometry above the clip value, False = keep below")]
        public bool ClipAbove
        {
            get => _deco.ClipAbove;
            set { _deco.ClipAbove = value; if (_deco.ClipEnabled) { _deco.ApplyClip(); _onChanged?.Invoke(); } }
        }

        private void NotifyTransform()
        {
            _deco.UpdateBoundingBox();
            _onChanged?.Invoke();
        }
    }
}
