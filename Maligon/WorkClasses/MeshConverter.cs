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

            return mesh;
        }

        private static void ComputeNeighbors(MeshGraph mesh)
        {
            var edgeMap = new Dictionary<(int, int), List<int>>();

            // 🔥 1. строим weld-map
            var weld = BuildWeldMap(mesh);

            // 2. Собираем все рёбра (через weld!)
            foreach (var face in mesh.Faces)
            {
                var edges = new (int, int)[]
                {
            NormalizeEdge(weld[face.V0], weld[face.V1]),
            NormalizeEdge(weld[face.V1], weld[face.V2]),
            NormalizeEdge(weld[face.V2], weld[face.V0])
                };

                foreach (var edge in edges)
                {
                    if (!edgeMap.TryGetValue(edge, out var list))
                    {
                        list = new List<int>();
                        edgeMap[edge] = list;
                    }

                    list.Add(face.Id);
                }
            }

            // 3. Обнуляем соседей
            foreach (var face in mesh.Faces)
            {
                face.Neighbors = new int[] { -1, -1, -1 };
            }

            // 4. Назначаем соседей
            foreach (var face in mesh.Faces)
            {
                var edges = new (int, int)[]
                {
            NormalizeEdge(weld[face.V0], weld[face.V1]),
            NormalizeEdge(weld[face.V1], weld[face.V2]),
            NormalizeEdge(weld[face.V2], weld[face.V0])
                };

                int index = 0;

                foreach (var edge in edges)
                {
                    if (!edgeMap.TryGetValue(edge, out var connectedFaces))
                        continue;

                    foreach (var otherId in connectedFaces)
                    {
                        if (otherId == face.Id)
                            continue;

                        var other = mesh.Faces[otherId];

                        // --- геометрическая фильтрация ---
                        var c1 = GetFaceCenter(face, mesh);
                        var c2 = GetFaceCenter(other, mesh);

                        float dist = Vector3.Distance(c1, c2);
                        float maxDist = GetAdaptiveNeighborDistance(face, mesh);

                        if (dist > maxDist)
                            continue;
                        // --- конец фильтра ---

                        if (!face.Neighbors.Contains(otherId))
                        {
                            face.Neighbors[index++] = otherId;

                            if (index >= 3)
                                break;
                        }
                    }

                    if (index >= 3)
                        break;
                }
            }
        }

        //private static void ComputeNeighbors(MeshGraph mesh)
        //{
        //    var edgeMap = new Dictionary<(int, int), List<int>>();

        //    // 1. Собираем все рёбра
        //    foreach (var face in mesh.Faces)
        //    {
        //        var edges = new (int, int)[]
        //        {
        //    NormalizeEdge(face.V0, face.V1),
        //    NormalizeEdge(face.V1, face.V2),
        //    NormalizeEdge(face.V2, face.V0)
        //        };

        //        foreach (var edge in edges)
        //        {
        //            if (!edgeMap.TryGetValue(edge, out var list))
        //            {
        //                list = new List<int>();
        //                edgeMap[edge] = list;
        //            }

        //            list.Add(face.Id);
        //        }
        //    }

        //    // 2. Обнуляем соседей
        //    foreach (var face in mesh.Faces)
        //    {
        //        face.Neighbors = new int[] { -1, -1, -1 };
        //    }

        //    // 3. Назначаем соседей
        //    foreach (var face in mesh.Faces)
        //    {
        //        var edges = new (int, int)[]
        //        {
        //    NormalizeEdge(face.V0, face.V1),
        //    NormalizeEdge(face.V1, face.V2),
        //    NormalizeEdge(face.V2, face.V0)
        //        };

        //        int index = 0;

        //        foreach (var edge in edges)
        //        {
        //            var connectedFaces = edgeMap[edge];

        //            foreach (var otherId in connectedFaces)
        //            {
        //                if (otherId == face.Id)
        //                    continue;

        //                var other = mesh.Faces[otherId];

        //                // --- 🔴 ЛОГ ПРОВЕРКИ ГЕОМЕТРИИ ---
        //                var c1 = GetFaceCenter(face, mesh);
        //                var c2 = GetFaceCenter(other, mesh);

        //                float dist = Vector3.Distance(c1, c2);

        //                float maxDist = GetAdaptiveNeighborDistance(face, mesh);

        //                if (dist > maxDist)
        //                    continue;
        //                // --- КОНЕЦ ЛОГА ---

        //                if (!face.Neighbors.Contains(otherId))
        //                {
        //                    face.Neighbors[index++] = otherId;

        //                    if (index >= 3)
        //                        break;
        //                }
        //            }

        //            if (index >= 3)
        //                break;
        //        }
        //    }
        //}

        private static float GetAdaptiveNeighborDistance(Face f, MeshGraph mesh)
        {
            var v0 = mesh.Vertices[f.V0];
            var v1 = mesh.Vertices[f.V1];
            var v2 = mesh.Vertices[f.V2];

            float e1 = Vector3.Distance(v0, v1);
            float e2 = Vector3.Distance(v1, v2);
            float e3 = Vector3.Distance(v2, v0);

            float avgEdge = (e1 + e2 + e3) / 3f;

            // 🔑 ключевая эвристика
            return avgEdge * 2.0f;
        }


        private static Vector3 GetFaceCenter(Face f, MeshGraph mesh)
        {
            var v0 = mesh.Vertices[f.V0];
            var v1 = mesh.Vertices[f.V1];
            var v2 = mesh.Vertices[f.V2];

            return (v0 + v1 + v2) / 3f;
        }

        //private static void ComputeNeighbors(MeshGraph mesh)
        //{
        //    // ключ: ребро (упорядоченное)
        //    var edgeMap = new Dictionary<(int, int), int>();

        //    foreach (var face in mesh.Faces)
        //    {
        //        var edges = new (int, int)[]
        //        {
        //    NormalizeEdge(face.V0, face.V1),
        //    NormalizeEdge(face.V1, face.V2),
        //    NormalizeEdge(face.V2, face.V0)
        //        };

        //        for (int i = 0; i < 3; i++)
        //        {
        //            var edge = edges[i];

        //            if (edgeMap.TryGetValue(edge, out int otherFaceId))
        //            {
        //                // связываем два полигона
        //                var other = mesh.Faces[otherFaceId];

        //                face.Neighbors[i] = other.Id;

        //                // находим индекс ребра у соседа
        //                for (int j = 0; j < 3; j++)
        //                {
        //                    var otherEdge = GetEdge(other, j);
        //                    if (NormalizeEdge(otherEdge.Item1, otherEdge.Item2) == edge)
        //                    {
        //                        other.Neighbors[j] = face.Id;
        //                        break;
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                edgeMap[edge] = face.Id;
        //            }
        //        }
        //    }
        //}

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


        private static Dictionary<int, int> BuildWeldMap(MeshGraph mesh)
        {
            var map = new Dictionary<int, int>();
            var spatial = new Dictionary<(int, int, int), int>();

            float eps = 1e-4f;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];

                var key = (
                    (int)(v.X / eps),
                    (int)(v.Y / eps),
                    (int)(v.Z / eps)
                );

                if (spatial.TryGetValue(key, out int existing))
                {
                    map[i] = existing;
                }
                else
                {
                    spatial[key] = i;
                    map[i] = i;
                }
            }

            return map;
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
