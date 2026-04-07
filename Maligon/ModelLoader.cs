using Assimp;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.IO;

namespace Maligon
{
    public sealed class ModelLoader : IDisposable
    {
        private readonly AssimpContext _context = new();

        public ModelImportResult Load(string sourcePath)
        {
            Scene scene;

            try
            {
                scene = _context.ImportFile(
                    sourcePath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.JoinIdenticalVertices |
                    PostProcessSteps.GenerateNormals |
                    PostProcessSteps.ImproveCacheLocality |
                    PostProcessSteps.SortByPrimitiveType
                );
            }
            catch (AssimpException ex)
            {
                throw new NotSupportedException(
                    "Assimp не смог загрузить файл", ex);
            }

            RenameMeshesAsLods(scene);

            if (scene == null || !scene.HasMeshes)
                throw new InvalidOperationException(
                    "Файл загружен, но меши отсутствуют");

            string gltfPath = EnsureGltf(scene, sourcePath);
            //MeshData mesh = ConvertSceneToMeshData(scene);
            var lodModel = ConvertSceneToLodModel(scene);
            return new ModelImportResult
            {
                GltfPath = gltfPath,
                WorkingDirectory = Path.GetDirectoryName(gltfPath)!,

                LodModel = lodModel,
                Mesh = lodModel.Lods.First().Mesh
            };
        }

        private string CreateWorkingDirectory()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "ModelApp",
                Guid.NewGuid().ToString()
            );

            Directory.CreateDirectory(dir);
            return dir;
        }

        private void CopyAssociatedBin(string gltfPath, string workingDir)
        {
            string json = File.ReadAllText(gltfPath);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);

            var buffers = node?["buffers"]?.AsArray();
            if (buffers == null || buffers.Count == 0)
                return;

            string? uri = buffers[0]?["uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uri))
                return;

            string sourceBin = Path.Combine(
                Path.GetDirectoryName(gltfPath)!,
                uri
            );

            string targetBin = Path.Combine(
                workingDir,
                Path.GetFileName(uri)
            );

            if (File.Exists(sourceBin))
                File.Copy(sourceBin, targetBin, true);
        }

        private string EnsureGltf(Scene scene, string sourcePath)
        {
            string workingDir = CreateWorkingDirectory();

            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            string targetGltfPath = Path.Combine(
                workingDir,
                Path.GetFileNameWithoutExtension(sourcePath) + ".gltf"
            );

            if (ext == ".gltf")
            {
                File.Copy(sourcePath, targetGltfPath, true);
                CopyAssociatedBin(sourcePath, workingDir);

                return targetGltfPath;
            }

            if (ext == ".glb")
            {
                _context.ExportFile(scene, targetGltfPath, "gltf2");
                FixGltfBufferUri(targetGltfPath);

                return targetGltfPath;
            }

            _context.ExportFile(scene, targetGltfPath, "gltf2");
            FixGltfBufferUri(targetGltfPath);

            return targetGltfPath;
        }

        private static void RenameMeshesAsLods(Scene scene)
        {
            if (scene == null || !scene.HasMeshes)
                return;
            if (scene.MeshCount == 1)
            {
                scene.Meshes[0].Name = "LOD0";
                return;
            }
            var meshes = scene.Meshes
                .Select((mesh, index) => new
                {
                    Mesh = mesh,
                    VertexCount = mesh.VertexCount,
                    OriginalIndex = index
                })
                .OrderByDescending(m => m.VertexCount)
                .ToList();

            for (int i = 0; i < meshes.Count; i++)
            {
                meshes[i].Mesh.Name = $"LOD{i}";
            }
        }

        private MeshData ConvertSceneToMeshData(Scene scene)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            int vertexOffset = 0;

            foreach (var mesh in scene.Meshes)
            {
                // Вершины
                foreach (var v in mesh.Vertices)
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));

                // Нормали
                if (mesh.HasNormals)
                {
                    foreach (var n in mesh.Normals)
                        normals.Add(new Vector3(n.X, n.Y, n.Z));
                }
                else
                {
                    normals.AddRange(
                        Enumerable.Repeat(Vector3.UnitY, mesh.VertexCount));
                }

                // UV (канал 0)
                if (mesh.HasTextureCoords(0))
                {
                    foreach (var uv in mesh.TextureCoordinateChannels[0])
                        uvs.Add(new Vector2(uv.X, uv.Y));
                }
                else
                {
                    uvs.AddRange(
                        Enumerable.Repeat(Vector2.Zero, mesh.VertexCount));
                }

                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3)
                        continue;

                    indices.Add(vertexOffset + face.Indices[0]);
                    indices.Add(vertexOffset + face.Indices[1]);
                    indices.Add(vertexOffset + face.Indices[2]);
                }

                vertexOffset += mesh.VertexCount;
            }

            return new MeshData
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                Indices = indices.ToArray()
            };
        }

        public void Dispose()
        {
            _context.Dispose();
        }


        private void FixGltfBufferUri(string gltfPath)
        {
            string json = File.ReadAllText(gltfPath);

            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (node == null)
                return;

            var buffers = node["buffers"]?.AsArray();
            if (buffers == null || buffers.Count == 0)
                return;

            string? uri = buffers[0]?["uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(uri))
                return;

            string fileName = Path.GetFileName(uri);
            buffers[0]["uri"] = fileName;

            File.WriteAllText(gltfPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }



        public void Export(ModelImportResult model, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);

            foreach (var file in Directory.GetFiles(model.WorkingDirectory))
            {
                string dest = Path.Combine(
                    targetDirectory,
                    Path.GetFileName(file)
                );

                File.Copy(file, dest, true);
            }
        }




        private LodModel ConvertSceneToLodModel(Scene scene)
        {
            var result = new LodModel();

            foreach (var mesh in scene.Meshes)
            {
                result.Lods.Add(new LodMesh
                {
                    Name = mesh.Name,
                    Mesh = ConvertSingleMesh(mesh)
                });
            }

            return result;
        }


        private MeshData ConvertSingleMesh(Assimp.Mesh mesh)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            foreach (var v in mesh.Vertices)
                vertices.Add(new Vector3(v.X, v.Y, v.Z));

            if (mesh.HasNormals)
            {
                foreach (var n in mesh.Normals)
                    normals.Add(new Vector3(n.X, n.Y, n.Z));
            }
            else
            {
                normals.AddRange(
                    Enumerable.Repeat(Vector3.UnitY, mesh.VertexCount));
            }

            if (mesh.HasTextureCoords(0))
            {
                foreach (var uv in mesh.TextureCoordinateChannels[0])
                    uvs.Add(new Vector2(uv.X, uv.Y));
            }
            else
            {
                uvs.AddRange(
                    Enumerable.Repeat(Vector2.Zero, mesh.VertexCount));
            }

            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount != 3)
                    continue;

                indices.Add(face.Indices[0]);
                indices.Add(face.Indices[1]);
                indices.Add(face.Indices[2]);
            }

            return new MeshData
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                Indices = indices.ToArray()
            };
        }


    }

    public sealed class ModelImportResult
    {
        public string GltfPath;
        public string WorkingDirectory;
        public MeshData Mesh;
        public LodModel LodModel;
    }


    public sealed class MeshData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Indices;
    }
}
