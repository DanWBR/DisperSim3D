#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DisperSim3D.UI.Avalonia.Views
{
    internal struct RayHit
    {
        public Vector3 Position;
        public Vector3 Normal;
        public float Distance;
        public string? Tag;
    }

    internal static class RayCaster
    {
        public static (Vector3 origin, Vector3 direction) ScreenToRay(
            double mouseX, double mouseY,
            float viewportWidth, float viewportHeight,
            Matrix4x4 view, Matrix4x4 proj)
        {
            float ndcX = (float)(2.0 * mouseX / viewportWidth - 1.0);
            float ndcY = (float)(1.0 - 2.0 * mouseY / viewportHeight);

            if (!Matrix4x4.Invert(proj, out var invProj))
                return (Vector3.Zero, -Vector3.UnitZ);
            if (!Matrix4x4.Invert(view, out var invView))
                return (Vector3.Zero, -Vector3.UnitZ);

            var nearClip = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invProj);
            nearClip /= nearClip.W;
            var farClip = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invProj);
            farClip /= farClip.W;

            var nearWorld = Vector4.Transform(nearClip, invView);
            var farWorld = Vector4.Transform(farClip, invView);

            var origin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
            var target = new Vector3(farWorld.X, farWorld.Y, farWorld.Z);
            var dir = Vector3.Normalize(target - origin);

            return (origin, dir);
        }

        public static RayHit? RaycastScene(
            Vector3 origin, Vector3 direction,
            IReadOnlyList<SceneObject> objects,
            HashSet<string>? excludeTags = null)
        {
            RayHit? closest = null;

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (!obj.Visible) continue;
                if (obj.Mesh.CpuVertices == null) continue;
                if (excludeTags != null && obj.Tag != null && excludeTags.Contains(obj.Tag))
                    continue;

                if (!Matrix4x4.Invert(obj.ModelMatrix, out var invModel))
                    continue;

                var localOrigin = Vector3.Transform(origin, invModel);
                var localTarget = Vector3.Transform(origin + direction, invModel);
                var localDir = Vector3.Normalize(localTarget - localOrigin);

                var verts = obj.Mesh.CpuVertices;
                var indices = obj.Mesh.CpuIndices;

                if (indices != null && indices.Length >= 3)
                {
                    for (int t = 0; t < indices.Length - 2; t += 3)
                    {
                        var v0 = verts[indices[t]].Position;
                        var v1 = verts[indices[t + 1]].Position;
                        var v2 = verts[indices[t + 2]].Position;

                        if (RayTriangle(localOrigin, localDir, v0, v1, v2, out float dist))
                        {
                            var localHit = localOrigin + localDir * dist;
                            var worldHit = Vector3.Transform(localHit, obj.ModelMatrix);
                            float worldDist = Vector3.Distance(origin, worldHit);

                            if (closest == null || worldDist < closest.Value.Distance)
                            {
                                var localNormal = Vector3.Normalize(
                                    Vector3.Cross(v1 - v0, v2 - v0));
                                var worldNormal = Vector3.TransformNormal(localNormal, obj.ModelMatrix);
                                worldNormal = Vector3.Normalize(worldNormal);

                                if (Vector3.Dot(worldNormal, direction) > 0)
                                    worldNormal = -worldNormal;

                                closest = new RayHit
                                {
                                    Position = worldHit,
                                    Normal = worldNormal,
                                    Distance = worldDist,
                                    Tag = obj.Tag
                                };
                            }
                        }
                    }
                }
                else
                {
                    for (int t = 0; t < verts.Length - 2; t += 3)
                    {
                        var v0 = verts[t].Position;
                        var v1 = verts[t + 1].Position;
                        var v2 = verts[t + 2].Position;

                        if (RayTriangle(localOrigin, localDir, v0, v1, v2, out float dist))
                        {
                            var localHit = localOrigin + localDir * dist;
                            var worldHit = Vector3.Transform(localHit, obj.ModelMatrix);
                            float worldDist = Vector3.Distance(origin, worldHit);

                            if (closest == null || worldDist < closest.Value.Distance)
                            {
                                var localNormal = Vector3.Normalize(
                                    Vector3.Cross(v1 - v0, v2 - v0));
                                var worldNormal = Vector3.TransformNormal(localNormal, obj.ModelMatrix);
                                worldNormal = Vector3.Normalize(worldNormal);

                                if (Vector3.Dot(worldNormal, direction) > 0)
                                    worldNormal = -worldNormal;

                                closest = new RayHit
                                {
                                    Position = worldHit,
                                    Normal = worldNormal,
                                    Distance = worldDist,
                                    Tag = obj.Tag
                                };
                            }
                        }
                    }
                }
            }

            return closest;
        }

        public static RayHit? RaycastGroundPlane(Vector3 origin, Vector3 direction, float z = 0f)
        {
            if (MathF.Abs(direction.Z) < 1e-8f) return null;
            float t = (z - origin.Z) / direction.Z;
            if (t < 0) return null;
            return new RayHit
            {
                Position = origin + direction * t,
                Normal = Vector3.UnitZ,
                Distance = t,
                Tag = "ground"
            };
        }

        private static bool RayTriangle(
            Vector3 origin, Vector3 dir,
            Vector3 v0, Vector3 v1, Vector3 v2,
            out float t)
        {
            t = 0;
            const float EPSILON = 1e-7f;

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = Vector3.Cross(dir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -EPSILON && a < EPSILON) return false;

            float f = 1.0f / a;
            var s = origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;

            var q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(dir, q);
            if (v < 0f || u + v > 1f) return false;

            t = f * Vector3.Dot(edge2, q);
            return t > EPSILON;
        }
    }
}
