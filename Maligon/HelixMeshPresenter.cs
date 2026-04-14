using Assimp;
using HelixToolkit.Wpf;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace Maligon
{
    public sealed class HelixMeshPresenter : IMeshPresenter
    {
        private readonly HelixViewport3D _viewport;
        private ModelVisual3D _currentVisual;

        public HelixMeshPresenter(HelixViewport3D viewport)
        {
            _viewport = viewport;
            InitializeViewport();
        }

        private void InitializeViewport()
        {
            _viewport.Children.Clear();
            _viewport.Children.Add(new DefaultLights());
        }

        public void Show(MeshData mesh)
        {
            Clear();

            var geometry = BuildGeometry(mesh);

            var material = new DiffuseMaterial(
                new SolidColorBrush(Colors.LightGray));

            var model = new GeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                BackMaterial = material
            };

            _currentVisual = new ModelVisual3D
            {
                Content = model
            };

            _viewport.Children.Add(_currentVisual);
            _viewport.ZoomExtents();
        }

        public void Clear()
        {
            if (_currentVisual != null)
            {
                _viewport.Children.Remove(_currentVisual);
                _currentVisual = null;
            }
        }

        private static MeshGeometry3D BuildGeometry(MeshData mesh)
        {
            var geometry = new MeshGeometry3D();

            foreach (var v in mesh.Vertices)
                geometry.Positions.Add(
                    new Point3D(v.X, v.Y, v.Z));

            foreach (var i in mesh.Indices)
                geometry.TriangleIndices.Add(i);

            foreach (var n in mesh.Normals)
                geometry.Normals.Add(
                    new Vector3D(n.X, n.Y, n.Z));

            if (mesh.UVs != null)
            {
                foreach (var uv in mesh.UVs)
                    geometry.TextureCoordinates.Add(
                        new System.Windows.Point(uv.X, uv.Y));
            }

            geometry.Freeze();
            return geometry;
        }
    }


    public interface IMeshPresenter
    {
        void Show(MeshData mesh);
        void Clear();
    }

    public sealed class LodMesh
    {
        public string Name { get; set; }
        public MeshData Mesh { get; set; }

        public int VertexCount => Mesh.Vertices.Length;
        public int TriangleCount => Mesh.Indices.Length / 3;
    }

    public sealed class LodModel
    {
        public List<LodMesh> Lods { get; set; } = new();

        public void AddLod(LodMesh lod)
        {
            Lods.Add(lod);
        }
    }
}
