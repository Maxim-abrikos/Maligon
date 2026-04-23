using Assimp;
using SharpGLTF;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.IO;
using System.Numerics;
using SharpGLTF.Transforms;

namespace Maligon
{
    public sealed class ModelLoader : IDisposable
    {
        private readonly AssimpContext _context = new();


        public ModelImportResult Load(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            if (ext == ".gltf" || ext == ".glb")
            {
                return LoadWithSharpGLTF(sourcePath);
            }

            return LoadWithAssimp(sourcePath);
        }


        private ModelImportResult LoadWithAssimp(string sourcePath)
        {
            Scene scene;

            try
            {
                scene = _context.ImportFile(
                    sourcePath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateSmoothNormals |
                    PostProcessSteps.FixInFacingNormals |
                    PostProcessSteps.CalculateTangentSpace |
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
                throw new InvalidOperationException("Меши отсутствуют");

            string gltfPath = EnsureGltf(scene, sourcePath);
            var lodModel = ConvertSceneToLodModel(scene);

            return new ModelImportResult
            {
                GltfPath = gltfPath,
                WorkingDirectory = Path.GetDirectoryName(gltfPath)!,
                LodModel = lodModel,
                Mesh = lodModel.Lods.First().Mesh
            };
        }


        private ModelImportResult LoadWithSharpGLTF(string path)
        {
            var model = SharpGLTF.Schema2.ModelRoot.Load(path);

            var lodModel = new LodModel();

            int lodIndex = 0;

            foreach (var mesh in model.LogicalMeshes)
            {
                var meshData = ConvertSharpMeshToMeshData(mesh);

                lodModel.Lods.Add(new LodMesh
                {
                    Mesh = meshData
                });

                lodIndex++;
            }

            if (lodModel.Lods.Count == 0)
                throw new InvalidOperationException("GLTF не содержит мешей");

            return new ModelImportResult
            {
                GltfPath = path,
                WorkingDirectory = Path.GetDirectoryName(path)!,
                LodModel = lodModel,
                Mesh = lodModel.Lods.First().Mesh
            };
        }

        private MeshData ConvertSharpMeshToMeshData(SharpGLTF.Schema2.Mesh mesh)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            foreach (var prim in mesh.Primitives)
            {
                var posAccessor = prim.GetVertexAccessor("POSITION");
                var normAccessor = prim.GetVertexAccessor("NORMAL");
                var uvAccessor = prim.GetVertexAccessor("TEXCOORD_0");

                var positions = posAccessor.AsVector3Array();
                var norms = normAccessor != null ? normAccessor.AsVector3Array() : null;
                var tex = uvAccessor != null ? uvAccessor.AsVector2Array() : null;

                int baseIndex = vertices.Count;

                for (int i = 0; i < positions.Count; i++)
                {
                    vertices.Add(positions[i]);

                    if (norms != null && i < norms.Count)
                        normals.Add(norms[i]);
                    else
                        normals.Add(Vector3.UnitY);

                    if (tex != null && i < tex.Count)
                        uvs.Add(tex[i]);
                    else
                        uvs.Add(Vector2.Zero);
                }

                var idx = prim.GetIndices();

                foreach (var i in idx)
                {
                    indices.Add(baseIndex + (int)i);
                }
            }

            return new MeshData
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                UVs = uvs.ToArray(),
                Indices = indices.ToArray()
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

            var scene = new SceneBuilder();

            var root = new NodeBuilder("Root");

            int lodIndex = 0;

            foreach (var lod in model.LodModel.Lods)
            {
                var mesh = BuildMesh(lod.Mesh, $"LOD{lodIndex}");

                var node = new NodeBuilder($"LOD{lodIndex}");

                scene.AddRigidMesh(mesh, node);

                root.AddNode(node);

                lodIndex++;
            }

            scene.AddNode(root);

            var sourceName = Path.GetFileNameWithoutExtension(model.GltfPath);
            string path = Path.Combine(targetDirectory, sourceName + ".gltf");

            scene.ToGltf2().Save(path);
        }


        private MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> BuildMesh(
    MeshData data,
    string name)
        {
            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(name);

            var material = new MaterialBuilder()
                .WithDoubleSide(true);

            var prim = mesh.UsePrimitive(material);

            for (int i = 0; i < data.Indices.Length; i += 3)
            {
                int i0 = data.Indices[i];
                int i1 = data.Indices[i + 1];
                int i2 = data.Indices[i + 2];

                var v0 = CreateVertex(data, i0);
                var v1 = CreateVertex(data, i1);
                var v2 = CreateVertex(data, i2);

                prim.AddTriangle(v0, v1, v2);
            }

            return mesh;
        }


        private (VertexPositionNormal, VertexTexture1, VertexEmpty) CreateVertex(
    MeshData data,
    int index)
        {
            var pos = data.Vertices[index];
            var normal = data.Normals[index];

            var uv = (data.UVs != null && data.UVs.Length > index)
                ? data.UVs[index]
                : Vector2.Zero;

            var vPosNorm = new VertexPositionNormal(pos, normal);
            var vTex = new VertexTexture1(uv);

            return (vPosNorm, vTex, new VertexEmpty());
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

            // 🔥 карта "позиция → индекс новой вершины"
            var vertexMap = new Dictionary<(int, int, int), int>();

            float epsilon = 1e-6f;

            (int, int, int) Quantize(Vector3 v)
            {
                return (
                    (int)(v.X / epsilon),
                    (int)(v.Y / epsilon),
                    (int)(v.Z / epsilon)
                );
            }

            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount != 3)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Non-triangle face detected: {face.IndexCount}"
                    );
                    continue;
                }

                for (int i = 0; i < 3; i++)
                {
                    int originalIndex = face.Indices[i];

                    var pos = mesh.Vertices[originalIndex];
                    var vertex = new Vector3(pos.X, pos.Y, pos.Z);

                    var key = Quantize(vertex);

                    if (!vertexMap.TryGetValue(key, out int newIndex))
                    {
                        newIndex = vertices.Count;

                        vertices.Add(vertex);

                        // нормаль
                        if (mesh.HasNormals)
                        {
                            var n = mesh.Normals[originalIndex];
                            normals.Add(new Vector3(n.X, n.Y, n.Z));
                        }
                        else
                        {
                            normals.Add(Vector3.UnitY);
                        }

                        // UV
                        if (mesh.HasTextureCoords(0))
                        {
                            var uv = mesh.TextureCoordinateChannels[0][originalIndex];
                            uvs.Add(new Vector2(uv.X, uv.Y));
                        }
                        else
                        {
                            uvs.Add(Vector2.Zero);
                        }

                        vertexMap[key] = newIndex;
                    }

                    indices.Add(newIndex);
                }
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
