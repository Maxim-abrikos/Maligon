using Maligon.SubClasses;
using Maligon.SubClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace Maligon.WorkClasses
{
    public static class MeshConverter
    {
        public static MeshGraph ToMeshGraph(MeshData data)
        {
            var mesh = new MeshGraph(data);

            ComputeNormalsAndAreas(mesh);
            ComputeNeighbors(mesh);
            ComputeErrors(mesh);

            foreach (var f in mesh.Faces.Take(20))
            {
                Debug.WriteLine(
                    $"Face {f.Id}: neighbors = {string.Join(",", f.Neighbors)}"
                );
            }

            return mesh;
        }



        private static void ComputeNeighbors(MeshGraph mesh)
        {
            // ключ: ребро (упорядоченное)
            var edgeMap = new Dictionary<(int, int), int>();

            foreach (var face in mesh.Faces)
            {
                var edges = new (int, int)[]
                {
            NormalizeEdge(face.V0, face.V1),
            NormalizeEdge(face.V1, face.V2),
            NormalizeEdge(face.V2, face.V0)
                };

                for (int i = 0; i < 3; i++)
                {
                    var edge = edges[i];

                    if (edgeMap.TryGetValue(edge, out int otherFaceId))
                    {
                        // связываем два полигона
                        var other = mesh.Faces[otherFaceId];

                        face.Neighbors[i] = other.Id;

                        // находим индекс ребра у соседа
                        for (int j = 0; j < 3; j++)
                        {
                            var otherEdge = GetEdge(other, j);
                            if (NormalizeEdge(otherEdge.Item1, otherEdge.Item2) == edge)
                            {
                                other.Neighbors[j] = face.Id;
                                break;
                            }
                        }
                    }
                    else
                    {
                        edgeMap[edge] = face.Id;
                    }
                }
            }
        }

        private static (int, int) NormalizeEdge(int a, int b)
        {
            return a < b ? (a, b) : (b, a);
        }

        private static (int, int) GetEdge(Face f, int index)
        {
            return index switch
            {
                0 => (f.V0, f.V1),
                1 => (f.V1, f.V2),
                2 => (f.V2, f.V0),
                _ => throw new ArgumentOutOfRangeException()
            };
        }


        public static MeshData ToMeshData(MeshGraph mesh)
        {
            var data = new MeshData();

            data.Vertices = mesh.Vertices.ToArray();

            var indices = new List<int>();
            var normals = new Vector3[mesh.Vertices.Count];

            foreach (var f in mesh.Faces)
            {
                if (f.IsUsed) continue;

                indices.Add(f.V0);
                indices.Add(f.V1);
                indices.Add(f.V2);

                // накапливаем нормали
                normals[f.V0] += f.Normal;
                normals[f.V1] += f.Normal;
                normals[f.V2] += f.Normal;
            }

            // нормализация
            for (int i = 0; i < normals.Length; i++)
            {
                if (normals[i] != Vector3.Zero)
                    normals[i] = Vector3.Normalize(normals[i]);
            }

            data.Indices = indices.ToArray();
            data.Normals = normals;

            return data;
        }

        private static void ComputeNormalsAndAreas(MeshGraph mesh)
        {
            foreach (var f in mesh.Faces)
            {
                var v0 = mesh.Vertices[f.V0];
                var v1 = mesh.Vertices[f.V1];
                var v2 = mesh.Vertices[f.V2];

                var cross = Vector3.Cross(v1 - v0, v2 - v0);

                f.Normal = Vector3.Normalize(cross);
                f.Area = cross.Length() * 0.5f;
            }
        }

        private static void ComputeErrors(MeshGraph mesh)
        {
            foreach (var f in mesh.Faces)
            {
                float error = 0;

                foreach (var nId in f.Neighbors)
                {
                    if (nId < 0) continue;

                    var n = mesh.Faces[nId];

                    float dot = Vector3.Dot(f.Normal, n.Normal);
                    float deviation = 1 - dot;

                    error += n.Area * deviation;
                }

                f.Error = error;
            }
        }
    }
}
