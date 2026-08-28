using System.Collections.Generic;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// One place to ask a scene what its solid geometry is.
    ///
    /// Every consumer that needs "does this get through" — the flash-fire flood fill,
    /// the radiation shading, the tracer engines — wants the same list of axis-aligned
    /// boxes from <see cref="Decoration3D.BoundingBox"/>. The WPF host can voxelise an
    /// imported mesh per triangle for the CFD path, which is finer; this is the portable
    /// answer, and it is the one both UIs share.
    /// </summary>
    public static class SceneObstacles
    {
        /// <summary>Obstacle boxes from the scene decorations. Never null.</summary>
        public static List<BoundingBox> Collect(Scene3D scene)
        {
            var boxes = new List<BoundingBox>();
            if (scene?.Decorations == null) return boxes;
            foreach (var deco in scene.Decorations)
                if (deco?.BoundingBox != null) boxes.Add(deco.BoundingBox);
            return boxes;
        }
    }
}
