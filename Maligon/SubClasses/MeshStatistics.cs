using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Maligon.SubClasses
{
    public sealed class MeshStatistics
    {
        public float[] Areas;
        public float[] Angles;
        public float[] Errors;

        public static MeshStatistics FromGraph(MeshGraph graph)
        {
            return new MeshStatistics
            {
                Areas = graph.Faces.Select(f => f.Area).ToArray(),
                Errors = graph.Faces.Select(f => f.Error).ToArray(),
                Angles = graph.Faces
                    .SelectMany(f =>
                        f.Neighbors
                            .Where(n => n >= 0)
                            .Select(n =>
                            {
                                var nf = graph.Faces[n];
                                float dot = Vector3.Dot(f.Normal, nf.Normal);
                                return MathF.Acos(Math.Clamp(dot, -1f, 1f));
                            }))
                    .ToArray()
            };
        }
    }
}
