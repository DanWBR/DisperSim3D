using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Provides factory methods for creating and applying WPF 3D materials (diffuse, metallic, glass, emissive)
    /// to <see cref="Model3DGroup"/> scene objects.
    /// </summary>
    public static class MaterialHelper
    {
        /// <summary>
        /// Creates a WPF 3D material of the specified type with the given color, specular power, and opacity.
        /// </summary>
        /// <param name="type">The material type determining the combination of diffuse, specular, and emissive components.</param>
        /// <param name="color">The base RGB color of the material.</param>
        /// <param name="specularPower">The specular exponent controlling highlight sharpness. Higher values produce tighter highlights.</param>
        /// <param name="opacity">The overall opacity in the range [0, 1], where 1 is fully opaque.</param>
        /// <returns>A <see cref="Material"/> instance configured for the requested type, or a plain diffuse material for the default case.</returns>
        public static Material CreateMaterial(MaterialType3D type, Color color, double specularPower, double opacity)
        {
            byte a = (byte)(opacity * 255);
            var baseColor = Color.FromArgb(a, color.R, color.G, color.B);
            var baseBrush = new SolidColorBrush(baseColor);

            // Procedural textured industrial materials — rust, galvanised, brushed,
            // painted, concrete. All return a tiled DrawingBrush; UV generation in
            // ApplyToModel handles the wrap.
            if (DecorationTextureRenderer.NeedsUV(type))
            {
                var texBrush = DecorationTextureRenderer.BuildBrush(type, color);
                var group = new MaterialGroup();
                group.Children.Add(new DiffuseMaterial(texBrush));
                // Metals get a specular pass so highlights still pop; concrete gets none.
                if (type != MaterialType3D.Concrete)
                {
                    double sp = type == MaterialType3D.BrushedMetal ? Math.Max(specularPower, 30)
                              : type == MaterialType3D.RustedMetal ? Math.Min(specularPower, 15)
                              : specularPower;
                    group.Children.Add(new SpecularMaterial(
                        new SolidColorBrush(Color.FromArgb(180, 220, 220, 230)), sp));
                }
                return group;
            }

            switch (type)
            {
                case MaterialType3D.Metallic:
                {
                    var group = new MaterialGroup();
                    group.Children.Add(new DiffuseMaterial(baseBrush));
                    var specColor = Color.FromArgb(255,
                        (byte)(200 + (255 - 200) * color.R / 255.0),
                        (byte)(200 + (255 - 200) * color.G / 255.0),
                        (byte)(200 + (255 - 200) * color.B / 255.0));
                    group.Children.Add(new SpecularMaterial(new SolidColorBrush(specColor), specularPower));
                    return group;
                }

                case MaterialType3D.Glass:
                {
                    byte glassA = (byte)(opacity * 0.5 * 255);
                    var glassBrush = new SolidColorBrush(Color.FromArgb(glassA, color.R, color.G, color.B));
                    var group = new MaterialGroup();
                    group.Children.Add(new DiffuseMaterial(glassBrush));
                    group.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), specularPower * 2));
                    return group;
                }

                case MaterialType3D.Emissive:
                {
                    var group = new MaterialGroup();
                    group.Children.Add(new DiffuseMaterial(baseBrush));
                    group.Children.Add(new EmissiveMaterial(new SolidColorBrush(
                        Color.FromArgb((byte)(a * 0.6), color.R, color.G, color.B))));
                    return group;
                }

                default:
                    return new DiffuseMaterial(baseBrush);
            }
        }

        /// <summary>
        /// Recursively applies the specified material to every <see cref="GeometryModel3D"/> in the model tree,
        /// setting both front and back materials.
        /// </summary>
        /// <param name="model">The root <see cref="Model3DGroup"/> whose children will receive the material. If <c>null</c>, the method returns immediately.</param>
        /// <param name="material">The material to assign. If <c>null</c>, the method returns immediately.</param>
        public static void ApplyToModel(Model3DGroup model, Material material)
        {
            ApplyToModel(model, material, MaterialType3D.Matte);
        }

        /// <summary>Overload that knows the material type so it can generate UVs on
        /// the fly for procedural textures (cylindrical projection — see
        /// <see cref="DecorationTextureRenderer.GenerateCylindricalUVs"/>).</summary>
        public static void ApplyToModel(Model3DGroup model, Material material, MaterialType3D type)
        {
            if (model == null || material == null) return;
            bool needsUV = DecorationTextureRenderer.NeedsUV(type);
            foreach (var child in model.Children)
            {
                if (child is GeometryModel3D gm)
                {
                    if (needsUV && gm.Geometry is MeshGeometry3D mesh)
                        DecorationTextureRenderer.GenerateCylindricalUVs(mesh);
                    gm.Material = material;
                    gm.BackMaterial = material;
                }
                else if (child is Model3DGroup childGroup)
                {
                    ApplyToModel(childGroup, material, type);
                }
            }
        }
    }
}
