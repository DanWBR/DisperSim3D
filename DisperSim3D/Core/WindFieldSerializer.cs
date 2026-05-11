using System;
using System.IO;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Binary serializer for <see cref="WindField3D"/>. Used to persist the result of a
    /// FluidX3D run between sessions — FluidX3D leaves no case directory like OpenFOAM,
    /// so we save the sampled velocity field directly. File format (little-endian):
    ///
    ///   int32  magic   = 0x57464431 ("WFD1")
    ///   int32  Nx, Ny, Nz
    ///   double xMin, xMax, yMin, yMax, zMax
    ///   double[Nx*Ny*Nz]  ux  (k-major: index = i + Nx*(j + Ny*k))
    ///   double[Nx*Ny*Nz]  uy
    ///   double[Nx*Ny*Nz]  uz
    /// </summary>
    public static class WindFieldSerializer
    {
        private const int Magic = 0x57464431; // 'W','F','D','1'

        public static void Save(string filePath,
            double[,,] ux, double[,,] uy, double[,,] uz,
            double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            if (ux == null || uy == null || uz == null)
                throw new ArgumentNullException(nameof(ux));
            int nx = ux.GetLength(0), ny = ux.GetLength(1), nz = ux.GetLength(2);
            using (var fs = File.Create(filePath))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write(nx); bw.Write(ny); bw.Write(nz);
                bw.Write(xMin); bw.Write(xMax);
                bw.Write(yMin); bw.Write(yMax);
                bw.Write(zMax);
                WriteArray(bw, ux);
                WriteArray(bw, uy);
                WriteArray(bw, uz);
            }
        }

        private static void WriteArray(BinaryWriter bw, double[,,] a)
        {
            int nx = a.GetLength(0), ny = a.GetLength(1), nz = a.GetLength(2);
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        bw.Write(a[i, j, k]);
        }

        /// <summary>Returns null if the file is missing or its magic doesn't match.</summary>
        public static WindField3D TryLoad(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                using (var fs = File.OpenRead(filePath))
                using (var br = new BinaryReader(fs))
                {
                    int magic = br.ReadInt32();
                    if (magic != Magic) return null;
                    int nx = br.ReadInt32();
                    int ny = br.ReadInt32();
                    int nz = br.ReadInt32();
                    double xMin = br.ReadDouble(), xMax = br.ReadDouble();
                    double yMin = br.ReadDouble(), yMax = br.ReadDouble();
                    double zMax = br.ReadDouble();

                    var ux = new double[nx, ny, nz];
                    var uy = new double[nx, ny, nz];
                    var uz = new double[nx, ny, nz];
                    for (int k = 0; k < nz; k++)
                        for (int j = 0; j < ny; j++)
                            for (int i = 0; i < nx; i++)
                                ux[i, j, k] = br.ReadDouble();
                    for (int k = 0; k < nz; k++)
                        for (int j = 0; j < ny; j++)
                            for (int i = 0; i < nx; i++)
                                uy[i, j, k] = br.ReadDouble();
                    for (int k = 0; k < nz; k++)
                        for (int j = 0; j < ny; j++)
                            for (int i = 0; i < nx; i++)
                                uz[i, j, k] = br.ReadDouble();

                    return new WindField3D(ux, uy, uz, xMin, xMax, yMin, yMax, zMax);
                }
            }
            catch { return null; }
        }
    }
}
