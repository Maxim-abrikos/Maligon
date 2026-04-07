using System;
using System.Collections.Generic;
using System.Text;
using Maligon.SubClasses;

namespace Maligon.WorkClasses
{
    public class LineCollapser
    {
        private MeshGraph _mesh;

        public LineCollapser(MeshGraph mesh)
        {
            _mesh = mesh;
        }

        public void Collapse(OptimizationLine line)
        {
            // 1. Разбиваем линию на пары
            var pairs = BuildPairs(line);

            // 2. Строим map вершин (старые -> новые)
            var vertexMap = BuildVertexMap(pairs);

            // 3. Применяем remap ко всему мешу
            RemapVertices(vertexMap);

            // 4. Удаляем полигоны линии
            RemoveLineFaces(line);

            // 5. Очистка (опционально)
            CleanupUnusedVertices();
        }


        public List<(Face, Face)> BuildPairs(OptimizationLine line)
        {
            var result = new List<(Face, Face)>();

            var node = line.Faces.First;

            while (node != null && node.Next != null)
            {
                result.Add((node.Value, node.Next.Value));
                node = node.Next.Next;
            }

            return result;
        }

        private Dictionary<int, int> BuildVertexMap(List<(Face, Face)> pairs)
        {
            var map = new Dictionary<int, int>();

            foreach (var (a, b) in pairs)
            {
                var (v1, v2) = MeshUtils.GetSharedEdge(a, b);

                int newV = CreateMidpoint(v1, v2);

                map[v1] = newV;
                map[v2] = newV;
            }

            return map;
        }

        private int CreateMidpoint(int v1, int v2)
        {
            var p1 = _mesh.Vertices[v1];
            var p2 = _mesh.Vertices[v2];

            var mid = (p1 + p2) * 0.5f;

            int index = _mesh.Vertices.Count;
            _mesh.Vertices.Add(mid);

            return index;
        }


        private void RemapVertices(Dictionary<int, int> map)
        {
            foreach (var face in _mesh.Faces)
            {
                if (face == null) continue;

                if (map.TryGetValue(face.V0, out var nv0)) face.V0 = nv0;
                if (map.TryGetValue(face.V1, out var nv1)) face.V1 = nv1;
                if (map.TryGetValue(face.V2, out var nv2)) face.V2 = nv2;
            }
        }


        private void RemoveLineFaces(OptimizationLine line)
        {
            foreach (var f in line.Faces)
            {
                f.IsUsed = true; // помечаем как удалённый
            }
        }

        private void CleanupUnusedVertices()
        {
            // пока ничего не делаем
            // позже можно добавить compact mesh
        }
    }
}
