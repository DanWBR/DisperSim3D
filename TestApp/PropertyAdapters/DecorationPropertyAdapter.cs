using System;
using System.ComponentModel;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.App.PropertyAdapters
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
        [Description("Name of this decoration")]
        public string Name
        {
            get => _deco.Name;
            set { _deco.Name = value; _onChanged?.Invoke(); }
        }

        [Category("General")]
        [DisplayName("ID")]
        [ReadOnly(true)]
        public string Id => _deco.Id;

        [Category("General")]
        [DisplayName("File")]
        [ReadOnly(true)]
        public string FilePath => _deco.FilePath;

        // --- Position ---

        [Category("Position")]
        [DisplayName("X")]
        public double PosX
        {
            get => _deco.Position.X;
            set { _deco.Position = new Point3D(value, _deco.Position.Y, _deco.Position.Z); NotifyTransform(); }
        }

        [Category("Position")]
        [DisplayName("Y")]
        public double PosY
        {
            get => _deco.Position.Y;
            set { _deco.Position = new Point3D(_deco.Position.X, value, _deco.Position.Z); NotifyTransform(); }
        }

        [Category("Position")]
        [DisplayName("Z")]
        public double PosZ
        {
            get => _deco.Position.Z;
            set { _deco.Position = new Point3D(_deco.Position.X, _deco.Position.Y, value); NotifyTransform(); }
        }

        // --- Rotation ---

        [Category("Rotation")]
        [DisplayName("Rotation X (deg)")]
        public double RotX
        {
            get => _deco.Rotation.X;
            set { _deco.Rotation = new Vector3D(value, _deco.Rotation.Y, _deco.Rotation.Z); NotifyTransform(); }
        }

        [Category("Rotation")]
        [DisplayName("Rotation Y (deg)")]
        public double RotY
        {
            get => _deco.Rotation.Y;
            set { _deco.Rotation = new Vector3D(_deco.Rotation.X, value, _deco.Rotation.Z); NotifyTransform(); }
        }

        [Category("Rotation")]
        [DisplayName("Rotation Z (deg)")]
        public double RotZ
        {
            get => _deco.Rotation.Z;
            set { _deco.Rotation = new Vector3D(_deco.Rotation.X, _deco.Rotation.Y, value); NotifyTransform(); }
        }

        // --- Scale ---

        [Category("Transform")]
        [DisplayName("Scale")]
        [Description("Uniform scale factor")]
        public double Scale
        {
            get => _deco.Scale;
            set { _deco.Scale = Math.Max(0.01, value); NotifyTransform(); }
        }

        private void NotifyTransform()
        {
            _deco.UpdateBoundingBox();
            _onChanged?.Invoke();
        }
    }
}
