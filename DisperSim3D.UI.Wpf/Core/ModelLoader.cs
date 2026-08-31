using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Loads and caches 3D models from file (.obj, .stl, .3ds).
    /// Used for importing obstacle/building geometry.
    /// </summary>
    public class ModelLoader
    {
        private readonly Dictionary<string, Model3DGroup> _modelCache = new Dictionary<string, Model3DGroup>();
        private readonly ModelImporter _importer = new ModelImporter();

        /// <summary>
        /// Loads a 3D model from the specified file path (.obj, .stl, .3ds).
        /// Results are cached so that subsequent loads of the same path return a cloned copy without re-reading the file.
        /// A default light-gray material is applied to any geometry that lacks one.
        /// </summary>
        /// <param name="filePath">The absolute path to the 3D model file.</param>
        /// <returns>A cloned <see cref="Model3DGroup"/> containing the loaded geometry, or <c>null</c> if the file does not exist or loading fails.</returns>
        public Model3DGroup LoadModelFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            if (_modelCache.ContainsKey(filePath))
                return CloneModel(_modelCache[filePath]);

            try
            {
                // HelixToolkit's importer has no RVM reader; the engine does, and
                // it is the same one the Avalonia viewport uses.
                var model = DisperSim3D.Core.RvmMeshLoader.IsRvmPath(filePath)
                    ? LoadRvm(filePath)
                    : _importer.Load(filePath);
                if (model != null)
                {
                    var b = model.Bounds;
                    System.Diagnostics.Debug.WriteLine(
                        string.Format("Loaded {0}: bounds=({1:F2},{2:F2},{3:F2})-({4:F2},{5:F2},{6:F2}) size=({7:F2},{8:F2},{9:F2})",
                        Path.GetFileName(filePath),
                        b.X, b.Y, b.Z,
                        b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ,
                        b.SizeX, b.SizeY, b.SizeZ));

                    bool needsMaterial = false;
                    foreach (var child in model.Children)
                    {
                        if (child is GeometryModel3D gm && gm.Material == null)
                        {
                            needsMaterial = true;
                            gm.Material = new DiffuseMaterial(Brushes.LightGray);
                            gm.BackMaterial = new DiffuseMaterial(Brushes.LightGray);
                        }
                    }
                    if (needsMaterial)
                        System.Diagnostics.Debug.WriteLine("Applied default material to model with null materials");

                    _modelCache[filePath] = model;
                    return CloneModel(model);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to load model " + filePath + ": " + ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Builds a <see cref="Model3DGroup"/> from an AVEVA PDMS / E3D
        /// <c>.rvm</c> file. The parsing and tessellation are the engine's
        /// (<see cref="DisperSim3D.Core.RvmMeshLoader"/>); this converts the
        /// resulting triangle soup into WPF geometry.
        /// </summary>
        private static Model3DGroup LoadRvm(string filePath)
        {
            var rvm = DisperSim3D.Core.RvmMeshLoader.Load(filePath);
            if (rvm == null) return null;

            var mesh = new MeshGeometry3D();
            var positions = new Point3DCollection(rvm.Vertices.Length);
            var normals = new Vector3DCollection(rvm.Normals.Length);
            foreach (var v in rvm.Vertices)
                positions.Add(new Point3D(v.X, v.Y, v.Z));
            foreach (var n in rvm.Normals)
                normals.Add(new Vector3D(n.X, n.Y, n.Z));

            var indices = new Int32Collection(rvm.Indices.Length);
            foreach (uint i in rvm.Indices)
                indices.Add((int)i);

            mesh.Positions = positions;
            mesh.Normals = normals;
            mesh.TriangleIndices = indices;
            mesh.Freeze();

            var geometry = new GeometryModel3D(mesh, new DiffuseMaterial(Brushes.LightGray));
            geometry.BackMaterial = new DiffuseMaterial(Brushes.LightGray);

            var group = new Model3DGroup();
            group.Children.Add(geometry);

            System.Diagnostics.Debug.WriteLine(string.Format(
                "Loaded RVM {0}: {1} primitives, {2} triangles, tolerance {3}",
                Path.GetFileName(filePath), rvm.PrimitiveCount, rvm.TriangleCount,
                rvm.ToleranceUsed));

            return group;
        }

        /// <summary>
        /// Returns the file filter string for use in an <see cref="Microsoft.Win32.OpenFileDialog"/>,
        /// listing all supported 3D model formats (OBJ, STL, 3DS, RVM).
        /// </summary>
        /// <returns>A pipe-delimited filter string compatible with WPF file dialogs.</returns>
        public static string GetSupportedFormatsFilter()
        {
            return "3D Models (*.obj;*.stl;*.3ds;*.rvm)|*.obj;*.stl;*.3ds;*.rvm|" +
                   "Wavefront OBJ (*.obj)|*.obj|" +
                   "STL (*.stl)|*.stl|" +
                   "3D Studio (*.3ds)|*.3ds|" +
                   "AVEVA PDMS / E3D (*.rvm)|*.rvm|" +
                   "All files (*.*)|*.*";
        }

        /// <summary>
        /// Clears the internal model cache, releasing all cached <see cref="Model3DGroup"/> instances.
        /// Subsequent calls to <see cref="LoadModelFromFile"/> will re-read from disk.
        /// </summary>
        public void ClearCache()
        {
            _modelCache.Clear();
        }

        private Model3DGroup CloneModel(Model3DGroup model)
        {
            var clone = new Model3DGroup();
            foreach (var child in model.Children)
            {
                clone.Children.Add(child.Clone());
            }
            return clone;
        }
    }
}
