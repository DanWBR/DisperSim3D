using PortablePoint3D = DisperSim3D.Geometry.Point3D;
using PortableVector3D = DisperSim3D.Geometry.Vector3D;
using WpfPoint3D = System.Windows.Media.Media3D.Point3D;
using WpfVector3D = System.Windows.Media.Media3D.Vector3D;

namespace DisperSim3D.UI.Wpf.Geometry
{
    /// <summary>
    /// Bidirectional extension methods between the engine's portable
    /// <see cref="DisperSim3D.Geometry.Point3D"/> / <see cref="DisperSim3D.Geometry.Vector3D"/>
    /// and WPF's <c>System.Windows.Media.Media3D</c> counterparts. Renderers and
    /// dialogs call <c>.ToWpf()</c> when they need to build visuals or feed
    /// HelixToolkit; <c>.ToPortable()</c> goes the other way when a WPF API
    /// returns something the engine needs.
    /// </summary>
    public static class WpfInterop
    {
        public static WpfPoint3D ToWpf(this PortablePoint3D p)
            => new WpfPoint3D(p.X, p.Y, p.Z);

        public static WpfVector3D ToWpf(this PortableVector3D v)
            => new WpfVector3D(v.X, v.Y, v.Z);

        public static PortablePoint3D ToPortable(this WpfPoint3D p)
            => new PortablePoint3D(p.X, p.Y, p.Z);

        public static PortableVector3D ToPortable(this WpfVector3D v)
            => new PortableVector3D(v.X, v.Y, v.Z);
    }
}
