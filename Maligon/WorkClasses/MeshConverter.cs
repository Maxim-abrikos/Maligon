using Maligon.SubClasses;
using System;
using System.Collections.Generic;
using System.Text;
using Maligon.SubClasses;
using System.Numerics;

namespace Maligon.WorkClasses
{
    public static class MeshConverter
    {
        public static MeshGraph ToMeshGraph(MeshData data)
        {
            var mesh = new MeshGraph(data);

            ComputeNormalsAndAreas(mesh);
            ComputeErrors(mesh);

            return mesh;
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
