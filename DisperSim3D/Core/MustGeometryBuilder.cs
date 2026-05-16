using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Parametric generator for the Mock Urban Setting Test (MUST) container array.
    /// MUST was a field experiment at the Dugway Proving Ground (Utah) in which
    /// 120 ISO shipping containers were arranged in a regular grid to study
    /// dispersion in a built-up environment. Reference geometry from Biltoft 2001
    /// (DPG document WDTC-FR-01-121) and Yee &amp; Biltoft 2004 (Boundary-Layer
    /// Meteorology 111, 363-415).
    /// <para/>
    /// The array is reproduced programmatically here rather than loaded from a
    /// CAD file because the geometry is fully parametric. Containers are
    /// axis-aligned bounding boxes with their long axis crosswind by default
    /// (the conventional MUST orientation).
    /// </summary>
    public static class MustGeometryBuilder
    {
        /// <summary>Standard ISO shipping container long-axis length, in metres.</summary>
        public const double ContainerLengthM = 12.2;

        /// <summary>Standard ISO shipping container width (short axis), in metres.</summary>
        public const double ContainerWidthM = 2.42;

        /// <summary>Standard ISO shipping container height, in metres.</summary>
        public const double ContainerHeightM = 2.54;

        /// <summary>Default centre-to-centre spacing along the wind direction (between
        /// successive rows of containers), in metres. Per Biltoft 2001 layout.</summary>
        public const double DefaultSpacingAlongWindM = 12.9;

        /// <summary>Default centre-to-centre spacing crosswind (between containers
        /// inside a row), in metres. Per Biltoft 2001 layout.</summary>
        public const double DefaultSpacingCrosswindM = 12.9;

        /// <summary>Default number of rows along the wind direction.</summary>
        public const int DefaultRows = 12;

        /// <summary>Default number of columns crosswind (containers per row).</summary>
        public const int DefaultColumns = 10;

        /// <summary>
        /// Builds the 120-container array as a list of axis-aligned bounding boxes.
        /// Wind flows along +X by convention, so the long axis of each container
        /// lies along the Y axis (perpendicular to the wind). The array is centred
        /// on (<paramref name="centerX"/>, <paramref name="centerY"/>) and sits on
        /// the ground at z = <paramref name="groundZ"/>.
        /// </summary>
        /// <param name="rows">Number of rows along the wind direction (X). Default 12.</param>
        /// <param name="columns">Number of columns crosswind (Y). Default 10.</param>
        /// <param name="spacingAlongWindM">Centre-to-centre spacing between rows along wind, in metres. Default 12.9 m.</param>
        /// <param name="spacingCrosswindM">Centre-to-centre spacing between columns crosswind, in metres. Default 12.9 m.</param>
        /// <param name="containerLengthM">Container long-axis length (placed along Y). Default 12.2 m (ISO).</param>
        /// <param name="containerWidthM">Container short-axis width (placed along X). Default 2.42 m (ISO).</param>
        /// <param name="containerHeightM">Container height. Default 2.54 m (ISO).</param>
        /// <param name="centerX">Array centre X coordinate, in metres.</param>
        /// <param name="centerY">Array centre Y coordinate, in metres.</param>
        /// <param name="groundZ">Ground elevation (container base Z) in metres. Default 0.</param>
        public static List<BoundingBox> BuildContainerArray(
            int rows = DefaultRows,
            int columns = DefaultColumns,
            double spacingAlongWindM = DefaultSpacingAlongWindM,
            double spacingCrosswindM = DefaultSpacingCrosswindM,
            double containerLengthM = ContainerLengthM,
            double containerWidthM = ContainerWidthM,
            double containerHeightM = ContainerHeightM,
            double centerX = 0,
            double centerY = 0,
            double groundZ = 0)
        {
            var boxes = new List<BoundingBox>(rows * columns);

            // Container half-extents in each world-axis direction. Long axis is
            // crosswind (Y), short axis is along wind (X), height is Z.
            double halfX = containerWidthM * 0.5;
            double halfY = containerLengthM * 0.5;

            // The array footprint spans (rows-1) * spacingAlongWind in X and
            // (columns-1) * spacingCrosswind in Y. Centre at (centerX, centerY).
            double startX = centerX - (rows - 1) * spacingAlongWindM * 0.5;
            double startY = centerY - (columns - 1) * spacingCrosswindM * 0.5;

            for (int i = 0; i < rows; i++)
            {
                double cx = startX + i * spacingAlongWindM;
                for (int j = 0; j < columns; j++)
                {
                    double cy = startY + j * spacingCrosswindM;
                    var min = new Point3D(cx - halfX, cy - halfY, groundZ);
                    var max = new Point3D(cx + halfX, cy + halfY, groundZ + containerHeightM);
                    boxes.Add(new BoundingBox(min, max));
                }
            }
            return boxes;
        }

        /// <summary>
        /// Wraps each generated bounding box in a <see cref="Decoration3D"/> so it
        /// can be added to a <c>Scene3D.Decorations</c> list. The scene's existing
        /// obstacle pipeline (used by HeadlessRunner and FluidX3DRunner) picks up
        /// the boxes automatically and feeds them to both the wind-field LBM and
        /// the tracer engine.
        /// </summary>
        public static List<Decoration3D> BuildContainerDecorations(
            int rows = DefaultRows,
            int columns = DefaultColumns,
            double spacingAlongWindM = DefaultSpacingAlongWindM,
            double spacingCrosswindM = DefaultSpacingCrosswindM,
            double containerLengthM = ContainerLengthM,
            double containerWidthM = ContainerWidthM,
            double containerHeightM = ContainerHeightM,
            double centerX = 0,
            double centerY = 0,
            double groundZ = 0)
        {
            var boxes = BuildContainerArray(
                rows, columns,
                spacingAlongWindM, spacingCrosswindM,
                containerLengthM, containerWidthM, containerHeightM,
                centerX, centerY, groundZ);
            var decos = new List<Decoration3D>(boxes.Count);
            for (int n = 0; n < boxes.Count; n++)
            {
                int i = n / columns;
                int j = n % columns;
                decos.Add(new Decoration3D
                {
                    Id = "must-c-" + i.ToString("D2") + "-" + j.ToString("D2"),
                    Name = "MUST container [" + i + "," + j + "]",
                    BoundingBox = boxes[n],
                    Scale = 1.0
                });
            }
            return decos;
        }
    }
}
