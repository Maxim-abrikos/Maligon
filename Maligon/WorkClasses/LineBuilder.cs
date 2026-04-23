using Maligon.SubClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Shapes;
using System.Numerics;

namespace Maligon.WorkClasses
{
    public class LineBuilder
    {
        private MeshGraph _mesh;
        private OccupancyMap _occupancy;

        public LineBuilder(MeshGraph mesh, OccupancyMap occupancy)
        {
            _mesh = mesh;
            _occupancy = occupancy;
        }

        public OptimizationLine BuildLine()
        {
            var start = FindBestStart();

            if (start == null)
                return null;

            var line = new OptimizationLine();

            AddFace(line, start, toHead: true);

            // пробуем взять второго (любой нормальный сосед)
            var second = GetBestNeighbor(start);
            if (second == null)
                return line; // хотя бы 1 полигон

            AddFace(line, second, toHead: false);

            if (second != null)
                AddFace(line, second, toHead: false);

            // рост линии
            while (true)
            {
                var next = SelectBestCandidate(line);

                if (next == null)
                    break;

                AddFace(line, next.Value.face, next.Value.toHead);
            }

            return line;
        }

        // =====================================================

        private Face FindBestStart()
        {
            Face best = null;
            float bestScore = float.MaxValue;

            foreach (var f in _mesh.Faces)
            {
                if (_occupancy.UsedFaces.Contains(f))
                    continue;

                var neighbors = MeshUtils.GetNeighbors(f, _mesh);

                if (neighbors.Count == 0)
                    continue;

                float sum = 0;

                foreach (var n in neighbors)
                {
                    float err = Math.Abs(f.Area - n.Area) / (f.Area + 1e-6f);
                    sum += err;
                }

                if (sum < bestScore)
                {
                    bestScore = sum;
                    best = f;
                }
            }

            return best;
        }

        // =====================================================
        private Face GetBestNeighbor(Face f)
        {
            Face best = null;
            float bestScore = float.MaxValue;

            foreach (var n in MeshUtils.GetNeighbors(f, _mesh))
            {
                if (_occupancy.UsedFaces.Contains(n))
                    continue;

                if (MeshUtils.GetSharedEdge(f, n, _mesh) == null)
                    continue;

                float score = Math.Abs(n.Area - f.Area) / (f.Area + 1e-6f);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = n;
                }
            }

            return best;
        }
        //private Face GetBestNeighbor(Face f, float refArea)
        //{
        //    Face best = null;
        //    float bestErr = float.MaxValue;

        //    foreach (var n in MeshUtils.GetNeighbors(f, _mesh))
        //    {
        //        if (_occupancy.UsedFaces.Contains(n))
        //            continue;

        //        if (MeshUtils.GetSharedEdge(f, n) == null)
        //            continue;

        //        float err = Math.Abs(n.Area - refArea) / (refArea + 1e-6f);

        //        if (err < 0.15f && err < bestErr)
        //        {
        //            bestErr = err;
        //            best = n;
        //        }
        //    }

        //    return best;
        //}

        // =====================================================

        private (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        {
            var candidates = new List<(Face face, bool toHead)>();

            // --- два конца линии ---
            var ends = new[]
            {
        (face: line.Head, toHead: true),
        (face: line.Tail, toHead: false)
    };

            foreach (var (endFace, toHead) in ends)
            {
                if (endFace == null)
                    continue;

                foreach (var nId in endFace.Neighbors)
                {
                    if (nId < 0) continue;

                    var f = _mesh.Faces[nId];

                    if (f.IsUsed) continue;
                    if (line.FaceSet.Contains(f)) continue;

                    // 🔴 главный фикс — топология
                    if (!IsValidTopologically(line, f, toHead))
                        continue;

                    candidates.Add((f, toHead));
                }
            }

            if (candidates.Count == 0)
                return null;

            // =====================================================
            // 🔴 СТАРТОВЫЙ СЛУЧАЙ (нужно гарантировать 2 полигона)
            // =====================================================
            if (line.Faces.Count == 1)
            {
                var start = line.Head;

                var best = candidates
                    .OrderBy(c =>
                        Math.Abs(c.face.Area - start.Area) / (start.Area + 1e-6f))
                    .First();

                return best;
            }

            // =====================================================
            // 🔴 ОСНОВНОЙ ФИЛЬТР ПО ПЛОЩАДИ
            // =====================================================

            float tauArea = 0.30f; // ослабленный фильтр

            float meanArea = line.AreaMean;

            var areaFiltered = candidates
                .Where(c =>
                {
                    float err = Math.Abs(c.face.Area - meanArea) / (meanArea + 1e-6f);
                    return err < tauArea;
                })
                .ToList();

            // 🔴 fallback — не душим линию
            if (areaFiltered.Count == 0)
                return candidates.First();

            // =====================================================
            // 🔴 ВЫБОР ЛУЧШЕГО
            // =====================================================

            var bestCandidate = areaFiltered
                .OrderBy(c =>
                    Math.Abs(c.face.Area - meanArea) / (meanArea + 1e-6f))
                .First();

            return bestCandidate;
        }

        // =====================================================
        private float AreaError(Face f, OptimizationLine line)
        {
            float mean = line.AreaMean;

            if (mean < 1e-6f)
                return 0f;

            return Math.Abs(f.Area - mean) / mean;
        }

        private Face SelectByArea(List<Face> candidates, OptimizationLine line)
        {
            if (candidates.Count == 0)
                return null;

            float tau = 0.3f;

            Face best = null;
            float bestErr = float.MaxValue;

            foreach (var f in candidates)
            {
                float err = AreaError(f, line);

                if (err > tau)
                    continue;

                if (err < bestErr)
                {
                    bestErr = err;
                    best = f;
                }
            }

            return best;
        }


        private bool IsValidTopologically(OptimizationLine line, Face candidate, bool toHead)
        {
            var head = line.Head;
            var tail = line.Tail;

            int sharedCount = 0;
            (int, int)? sharedEdge = null;

            foreach (var f in line.Faces)
            {
                var shared = MeshUtils.GetSharedEdge(f, candidate);

                if (shared != null)
                {
                    sharedCount++;
                    sharedEdge = shared;
                }
            }

            // 🔴 нельзя иметь больше одной общей грани
            if (sharedCount > 1)
                return false;

            bool isNeighborHead = head != null && MeshUtils.GetSharedEdge(head, candidate) != null;
            bool isNeighborTail = tail != null && MeshUtils.GetSharedEdge(tail, candidate) != null;

            // 🔴 должен быть соседом нужного конца
            if (toHead && !isNeighborHead)
                return false;

            if (!toHead && !isNeighborTail)
                return false;

            // 🔴 запрет на замыкание
            if (isNeighborHead && isNeighborTail)
                return false;

            // =====================================================
            // 🔴 НОВЫЙ КРИТИЧЕСКИЙ ФИЛЬТР
            // =====================================================

            if (sharedEdge != null)
            {
                var verts = MeshUtils.GetVertices(candidate);

                foreach (var v in verts)
                {
                    // вершины ребра пропускаем
                    if (v == sharedEdge.Value.Item1 || v == sharedEdge.Value.Item2)
                        continue;

                    // 🔴 третья вершина уже в линии → нельзя
                    if (line.VertexSet.Contains(v))
                        return false;
                }
            }

            return true;
        }
        //private bool IsValidTopologically(OptimizationLine line, Face candidate, bool toHead)
        //{
        //    var head = line.Head;
        //    var tail = line.Tail;

        //    int sharedCount = 0;

        //    foreach (var f in line.Faces)
        //    {
        //        var shared = MeshUtils.GetSharedEdge(f, candidate);

        //        if (shared != null)
        //            sharedCount++;
        //    }

        //    // 🔴 нельзя иметь больше одной общей грани с линией
        //    if (sharedCount > 1)
        //        return false;

        //    bool isNeighborHead = head != null && MeshUtils.GetSharedEdge(head, candidate) != null;
        //    bool isNeighborTail = tail != null && MeshUtils.GetSharedEdge(tail, candidate) != null;

        //    // 🔴 должен быть соседом нужного конца
        //    if (toHead && !isNeighborHead)
        //        return false;

        //    if (!toHead && !isNeighborTail)
        //        return false;

        //    // 🔴 запрет на замыкание
        //    if (isNeighborHead && isNeighborTail)
        //        return false;

        //    return true;
        //}





        private void AddFace(OptimizationLine line, Face f, bool toHead)
        {
            if (line.Faces.Count == 0)
            {
                line.Faces.AddFirst(f);
            }
            else if (toHead)
            {
                line.Faces.AddFirst(f);
            }
            else
            {
                line.Faces.AddLast(f);
            }

            line.FaceSet.Add(f);
            line.RegisterFaceVertices(f);

            line.TotalError += f.Error;

            line.RecalculateAreaMean();
        }

        private static Vector3 GetFaceCenter(Face f, MeshGraph mesh)
        {
            var v0 = mesh.Vertices[f.V0];
            var v1 = mesh.Vertices[f.V1];
            var v2 = mesh.Vertices[f.V2];

            return (v0 + v1 + v2) / 3f;
        }

        private bool SharesVertexWithNonEdge(Face candidate, OptimizationLine line, bool toHead)
        {
            var verts = MeshUtils.GetVertices(candidate);

            Face edgeFace = toHead ? line.Head : line.Tail;

            foreach (var face in line.FaceSet)
            {
                if (face == edgeFace)
                    continue;

                var fVerts = MeshUtils.GetVertices(face);

                foreach (var v in verts)
                {
                    if (fVerts.Contains(v))
                        return true; // 🔴 делит вершину с серединой линии
                }
            }

            return false;
        }


        private bool IsNeighborOfLineInterior(Face candidate, OptimizationLine line)
        {
            foreach (var face in line.FaceSet)
            {
                // пропускаем только края линии
                if (face == line.Head || face == line.Tail)
                    continue;

                if (MeshUtils.GetNeighbors(face, _mesh).Contains(candidate))
                    return true;
            }

            return false;
        }

        public void RemoveFace(OptimizationLine line, bool fromHead)
        {
            if (line.Faces.Count == 0)
                return;

            Face face;

            if (fromHead)
            {
                face = line.Head;
                line.Faces.RemoveFirst();
            }
            else
            {
                face = line.Tail;
                line.Faces.RemoveLast();
            }

            if (face != null)
            {
                line.FaceSet.Remove(face);
                line.TotalError -= face.Error;
            }
        }
    }
}
