using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Maligon.SubClasses
{
    public sealed class MeshGraph
    {
        public List<Vector3> Vertices;
        public List<Face> Faces;
        public Vector3 GetVertex(int index) => Vertices[index];

        // edge → face
        private Dictionary<Edge, int> _edgeToFace = new();

        public MeshGraph(MeshData mesh)
        {
            Vertices = mesh.Vertices.ToList();
            Faces = BuildFaces(mesh);
            BuildAdjacency();
        }


        private List<Face> BuildFaces(MeshData mesh)
        {
            var faces = new List<Face>();

            for (int i = 0; i < mesh.Indices.Length; i += 3)
            {
                faces.Add(new Face
                {
                    Id = i / 3,
                    V0 = mesh.Indices[i],
                    V1 = mesh.Indices[i + 1],
                    V2 = mesh.Indices[i + 2],
                    Neighbors = new[] { -1, -1, -1 }
                });
            }

            return faces;
        }

        private void BuildAdjacency()
        {
            var edgeMap = new Dictionary<Edge, List<(int faceId, int edgeIndex)>>();

            for (int i = 0; i < Faces.Count; i++)
            {
                var f = Faces[i];

                var edges = new[]
                {
            new Edge(f.V0, f.V1),
            new Edge(f.V1, f.V2),
            new Edge(f.V2, f.V0)
        };

                for (int e = 0; e < 3; e++)
                {
                    var edge = edges[e];

                    if (!edgeMap.ContainsKey(edge))
                        edgeMap[edge] = new List<(int, int)>();

                    edgeMap[edge].Add((i, e));
                }
            }

            // теперь связываем
            foreach (var pair in edgeMap)
            {
                var list = pair.Value;

                if (list.Count != 2)
                    continue; // пропускаем невалидные (на всякий случай)

                var (f0, e0) = list[0];
                var (f1, e1) = list[1];

                Faces[f0].Neighbors[e0] = f1;
                Faces[f1].Neighbors[e1] = f0;
            }
        }
        //private void BuildAdjacency()
        //{
        //    var edgeMap = new Dictionary<Edge, (int faceId, int edgeIndex)>();

        //    for (int i = 0; i < Faces.Count; i++)
        //    {
        //        var f = Faces[i];

        //        var edges = new[]
        //        {
        //    new Edge(f.V0, f.V1),
        //    new Edge(f.V1, f.V2),
        //    new Edge(f.V2, f.V0)
        //        };

        //        for (int e = 0; e < 3; e++)
        //        {
        //            var edge = edges[e];

        //            if (edgeMap.TryGetValue(edge, out var other))
        //            {
        //                // связка
        //                Faces[i].Neighbors[e] = other.faceId;
        //                Faces[other.faceId].Neighbors[other.edgeIndex] = i;
        //            }
        //            else
        //            {
        //                edgeMap[edge] = (i, e);
        //            }
        //        }
        //    }
        //}

    }
}
