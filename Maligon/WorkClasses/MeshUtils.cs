using Maligon.SubClasses;
using System.Numerics;

namespace Maligon.WorkClasses
{
    public static class MeshUtils
    {
        public static int[] GetVertices(Face f)
        {
            return new[] { f.V0, f.V1, f.V2 };
        }

        public static Vector3 GetFaceCenter(Face f, MeshGraph mesh)
        {
            var v0 = mesh.Vertices[f.V0];
            var v1 = mesh.Vertices[f.V1];
            var v2 = mesh.Vertices[f.V2];

            return (v0 + v1 + v2) / 3f;
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

        public static (int, int)? GetSharedEdge(Face a, Face b, MeshGraph mesh)
        {
            var aVerts = GetVertices(a);
            var bVerts = GetVertices(b);

            var shared = new List<int>();

            const float eps = 1e-6f;

            foreach (var va in aVerts)
            {
                var pa = mesh.Vertices[va];

                foreach (var vb in bVerts)
                {
                    var pb = mesh.Vertices[vb];

                    if (Vector3.Distance(pa, pb) < eps)
                    {
                        shared.Add(va);
                        break;
                    }
                }
            }

            if (shared.Count != 2)
                return null;

            return (shared[0], shared[1]);
        }
        public static (int, int)? GetSharedEdge(Face a, Face b)
        {
            var aVerts = GetVertices(a);
            var bVerts = GetVertices(b);

            var shared = aVerts.Intersect(bVerts).ToArray();

            if (shared.Length < 2)
                return null;

            return (shared[0], shared[1]);
        }

        //public static (int, int) GetSharedEdge(Face a, Face b)
        //{
        //    var aVerts = GetVertices(a);
        //    var bVerts = GetVertices(b);

        //    var shared = aVerts.Intersect(bVerts).ToArray();

        //    return (shared[0], shared[1]);
        //}
    }
}
