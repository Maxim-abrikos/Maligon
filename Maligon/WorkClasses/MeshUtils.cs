using Maligon.SubClasses;
using System;
using System.Collections.Generic;
using System.Text;

namespace Maligon.WorkClasses
{
    public static class MeshUtils
    {
        public static int[] GetVertices(Face f)
        {
            return new[] { f.V0, f.V1, f.V2 };
        }

        public static bool IsNeighbor(Face a, Face b)
        {
            return a.Neighbors.Contains(b.Id) || b.Neighbors.Contains(a.Id);
        }

        public static List<Face> GetNeighbors(Face f, MeshGraph mesh)
        {
            var result = new List<Face>();

            foreach (var nId in f.Neighbors)
            {
                if (nId >= 0)
                    result.Add(mesh.Faces[nId]);
            }

            return result;
        }

        public static (int, int) GetSharedEdge(Face a, Face b)
        {
            var aVerts = GetVertices(a);
            var bVerts = GetVertices(b);

            var shared = aVerts.Intersect(bVerts).ToArray();

            return (shared[0], shared[1]);
        }
    }
}
