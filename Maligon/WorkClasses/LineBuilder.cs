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
    class LineBuilder
    {
        private const float W_CURV = 1.0f;
        private const float W_DIR = 2.0f;
        private const float W_PLANE = 1.0f;
        private const float W_SMOOTH = 0.5f;
        private const float MAX_STEP_ERROR = 0.5f;
        private MeshGraph _mesh;
        private OccupancyMap _occupancy;

        public LineBuilder(MeshGraph mesh, OccupancyMap occupancy)
        {
            _mesh = mesh;
            _occupancy = occupancy;
        }

        public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        {
            if (line.Head == null || line.Tail == null)
                return null;

            var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
                .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
                .ToList();

            var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
                .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
                .ToList();
            (Face face, bool toHead)? best = null;
            float bestScore = float.MaxValue;

            // кандидаты с головы
            foreach (var f in headCandidates)
            {
                float score = EvaluateCandidate(f, line, true);
                if (score > MAX_STEP_ERROR)
                    continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (f, true);
                }
            }

            // кандидаты с хвоста
            foreach (var f in tailCandidates)
            {
                float score = EvaluateCandidate(f, line, false);
                if (score > MAX_STEP_ERROR)
                    continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = (f, false);
                }
            }

            // если после фильтров ничего не осталось — ослабляем условия
            if (!headCandidates.Any() && !tailCandidates.Any())
            {
                headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
                    .Where(f => !line.FaceSet.Contains(f))
                    .ToList();

                tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
                    .Where(f => !line.FaceSet.Contains(f))
                    .ToList();
            }

            return best;
            //var bestHead = headCandidates.OrderBy(f => f.Error).FirstOrDefault();
            //var bestTail = tailCandidates.OrderBy(f => f.Error).FirstOrDefault();

            //if (bestHead == null && bestTail == null)
            //    return null;

            //if (bestHead == null)
            //    return (bestTail, false);

            //if (bestTail == null)
            //    return (bestHead, true);

            //return bestHead.Error < bestTail.Error
            //    ? (bestHead, true)
            //    : (bestTail, false);
        }

        //public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        //{
        //    if (line.Head == null || line.Tail == null)
        //        return null;

        //    var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
        //        .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //        .ToList();

        //    var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
        //        .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //        .ToList();

        //    var bestHead = headCandidates.OrderBy(f => f.Error).FirstOrDefault();
        //    var bestTail = tailCandidates.OrderBy(f => f.Error).FirstOrDefault();

        //    if (bestHead == null && bestTail == null)
        //        return null;

        //    if (bestHead == null)
        //        return (bestTail, false);

        //    if (bestTail == null)
        //        return (bestHead, true);

        //    return bestHead.Error < bestTail.Error
        //        ? (bestHead, true)
        //        : (bestTail, false);
        //}

        //public Face SelectBestCandidate(OptimizationLine line)
        //{
        //    if (line.Head == null || line.Tail == null)
        //        return null;
        //    var candidates = new List<Face>();

        //    candidates.AddRange(
        //        MeshUtils.GetNeighbors(line.Head, _mesh)
        //            .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //    );

        //    candidates.AddRange(
        //        MeshUtils.GetNeighbors(line.Tail, _mesh)
        //            .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //    );

        //    Debug.WriteLine($"Head neighbors: {MeshUtils.GetNeighbors(line.Head, _mesh).Count}");
        //    Debug.WriteLine($"Tail neighbors: {MeshUtils.GetNeighbors(line.Tail, _mesh).Count}");
        //    Debug.WriteLine($"Line length: {line.Faces.Count}");

        //    return candidates
        //        .OrderBy(f => f.Error)
        //        .FirstOrDefault();
        //}


        private float EvaluateCandidate(Face candidate, OptimizationLine line, bool toHead)
        {
            float eCurv = candidate.Error;

            float eDir = 0f;

            if (line.Faces.Count >= 2)
            {
                var head = GetFaceCenter(line.Head);
                var tail = GetFaceCenter(line.Tail);

                var dir = tail - head;
                dir = Vector3.Normalize(dir);

                var basePoint = toHead ? head : tail;
                var candidateDir = GetFaceCenter(candidate) - basePoint;
                candidateDir = Vector3.Normalize(candidateDir);

                eDir = 1f - Vector3.Dot(dir, candidateDir);
            }

            float ePlane = 0f;

            if (line.Faces.Count >= 1)
            {
                var avgNormal = GetAverageNormal(line);
                ePlane = AngleBetween(avgNormal, candidate.Normal);
            }

            float eSmooth = 0f;

            if (line.Faces.Count >= 2)
            {
                Face prev = null;

                if (toHead)
                    prev = line.Faces.First.Next?.Value;
                else
                    prev = line.Faces.Last.Previous?.Value;

                if (prev != null)
                {
                    var basePoint = toHead
                        ? GetFaceCenter(line.Head)
                        : GetFaceCenter(line.Tail);

                    var v1 = basePoint - GetFaceCenter(prev);
                    v1 = Vector3.Normalize(v1);

                    var v2 = GetFaceCenter(candidate) - basePoint;
                    v2 = Vector3.Normalize(v2);

                    eSmooth = AngleBetween(v1, v2);
                }
            }

            return
                W_CURV * eCurv +
                W_DIR * eDir +
                W_PLANE * ePlane +
                W_SMOOTH * eSmooth;
        }

        private Vector3 GetAverageNormal(OptimizationLine line)
        {
            Vector3 sum = Vector3.Zero;

            foreach (var f in line.Faces)
                sum += f.Normal;

            if (sum.LengthSquared() > 0)
                sum = Vector3.Normalize(sum);

            return sum;
        }

        private float AngleBetween(Vector3 a, Vector3 b)
        {
            a = Vector3.Normalize(a);
            b = Vector3.Normalize(b);

            float dot = Vector3.Dot(a, b);
            dot = Math.Clamp(dot, -1f, 1f);

            return (float)Math.Acos(dot);
        }

        private Vector3 GetFaceCenter(Face f)
        {
            var v0 = _mesh.Vertices[f.V0];
            var v1 = _mesh.Vertices[f.V1];
            var v2 = _mesh.Vertices[f.V2];

            return (v0 + v1 + v2) / 3f;
        }

        private bool IsBacktracking(Face candidate, OptimizationLine line)
        {
            if (line.Faces.Count < 2)
                return false;

            // предпоследний элемент с головы
            var secondFromHead = line.Faces.First.Next?.Value;

            // предпоследний с хвоста
            var secondFromTail = line.Faces.Last.Previous?.Value;

            return candidate == secondFromHead || candidate == secondFromTail;
        }

        //private bool IsBacktracking(Face candidate, OptimizationLine line)
        //{
        //    if (line.Faces.Count < 2)
        //        return false;

        //    var second = line.Faces.First.Next?.Value;
        //    var beforeLast = line.Faces.Last.Previous?.Value;

        //    // если кандидат ведёт "назад" — запрещаем
        //    if (second != null && MeshUtils.IsNeighbor(candidate, second))
        //        return true;

        //    if (beforeLast != null && MeshUtils.IsNeighbor(candidate, beforeLast))
        //        return true;

        //    return false;
        //}
        public void AddFace(OptimizationLine line, Face f, bool toHead)
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

            foreach (var v in MeshUtils.GetVertices(f))
                line.VertexSet.Add(v);

            line.TotalError += f.Error;
        }

        public void EnsureEvenLength(OptimizationLine line)
        {
            if (line.Faces.Count % 2 != 0)
            {
                var last = line.Faces.Last.Value;

                line.Faces.RemoveLast();
                line.FaceSet.Remove(last);

                foreach (var v in MeshUtils.GetVertices(last))
                    line.VertexSet.Remove(v);
            }
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
