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



        public OptimizationLine BuildLine(int maxIterations = 200)
        {
            ComputeAreaSimilarity();

            // 1. старт
            var start = _mesh.Faces
                .Where(f => !f.IsUsed)
                .OrderBy(f => f.AreaSimilarity)
                .FirstOrDefault();

            if (start == null)
                return null;

            var line = new OptimizationLine();

            AddFace(line, start, true);

            // 2. выбрать второго
            var firstNeighborId = start.Neighbors.FirstOrDefault(n => n >= 0);

            if (firstNeighborId < 0)
                return line;

            var second = _mesh.Faces[firstNeighborId];

            AddFace(line, second, false);

            // направление
            line.LastDirection = GetDirection(start, second);

            line.RecalculateAreaMean();

            int iterations = 0;

            // 3. рост
            while (iterations++ < maxIterations)
            {
                var next = SelectBestCandidate(line);

                if (next == null)
                    break;

                AddFace(line, next.Value.face, next.Value.toHead);

                UpdateLineState(line, next.Value.face);
            }

            return line;
        }


        private void UpdateLineState(OptimizationLine line, Face newFace)
        {
            if (line.Faces.Count < 2)
                return;

            var last = line.Faces.Last.Value;
            var prev = line.Faces.Last.Previous?.Value;

            if (prev == null) return;

            var newDir = GetDirection(prev, last);

            if (line.Faces.Count >= 4)
            {
                float k = SignedCurvature(line.LastDirection, newDir, prev.Normal);

                line.RecentCurvatures.Enqueue(k);

                if (line.RecentCurvatures.Count > 5)
                    line.RecentCurvatures.Dequeue();
            }

            line.LastDirection = newDir;

            line.RecalculateAreaMean();
        }



        private Vector3 GetDirection(Face a, Face b)
        {
            var ca = GetFaceCenter(a);
            var cb = GetFaceCenter(b);

            var dir = cb - ca;
            return Vector3.Normalize(dir);
        }



        private float SignedCurvature(Vector3 prevDir, Vector3 newDir, Vector3 normal)
        {
            var cross = Vector3.Cross(prevDir, newDir);
            float sign = Math.Sign(Vector3.Dot(cross, normal));

            float angle = AngleBetween(prevDir, newDir);

            return angle * sign;
        }



        public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        {
            if (line.Head == null || line.Tail == null)
                return null;

            float tauArea = 0.2f;
            float tauDir = 0.0f;
            float tauCurv = 0.3f;
            //float tauArea = 1.0f;     // было 0.2
            //float tauDir = -0.5f;     // было 0.0
            //float tauCurv = 1.0f;     // было 0.3

            var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
                .Where(f => !line.FaceSet.Contains(f) /*&& !IsBacktracking(f, line)*/)
                .ToList();

            var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
                .Where(f => !line.FaceSet.Contains(f) /*&& !IsBacktracking(f, line)*/)
                .ToList();

            var allCandidates = new List<(Face face, bool toHead)>();
            allCandidates.AddRange(headCandidates.Select(f => (f, true)));
            allCandidates.AddRange(tailCandidates.Select(f => (f, false)));

            List<(Face face, bool toHead)> valid = new();

            foreach (var c in allCandidates)
            {
                var f = c.face;

                if (f.IsUsed)
                    continue;

                // площадь
                float areaDiff = Math.Abs(f.Area - line.AreaMean) / (line.AreaMean + 1e-6f);
                if (areaDiff > tauArea)
                    continue;

                // направление
                var current = c.toHead ? line.Head : line.Tail;
                var newDir = GetDirection(current, f);

                float dot = Vector3.Dot(line.LastDirection, newDir);
                if (dot < tauDir)
                    continue;

                //curvature
                if (line.Faces.Count >= 4)
                {
                    float kNew = SignedCurvature(line.LastDirection, newDir, current.Normal);
                    float kExpected = line.GetExpectedCurvature();

                    float curvError = Math.Abs(kNew - kExpected);

                    if (curvError > tauCurv)
                        continue;
                }

                valid.Add(c);
            }

            // fallback
            if (valid.Count == 0)
            {
                foreach (var c in allCandidates)
                {
                    if (!c.face.IsUsed)
                        valid.Add(c);
                }

                if (valid.Count == 0)
                    return null;
            }

            // выбор
            (Face face, bool toHead)? best = null;

            float bestCurv = float.MaxValue;
            float bestDot = -1f;

            foreach (var c in valid)
            {
                var current = c.toHead ? line.Head : line.Tail;
                var newDir = GetDirection(current, c.face);

                float dot = Vector3.Dot(line.LastDirection, newDir);

                float curvError = 0f;

                if (line.Faces.Count >= 4)
                {
                    float kNew = SignedCurvature(line.LastDirection, newDir, current.Normal);
                    float kExpected = line.GetExpectedCurvature();

                    curvError = Math.Abs(kNew - kExpected);
                }

                if (curvError < bestCurv ||
                   (Math.Abs(curvError - bestCurv) < 1e-5f && dot > bestDot))
                {
                    bestCurv = curvError;
                    bestDot = dot;
                    best = c;
                }
            }

            return best;
        }

        //public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        //{
        //    if (line.Head == null || line.Tail == null)
        //        return null;

        //    var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
        //        .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
        //        .ToList();

        //    var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
        //        .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
        //        .ToList();
        //    (Face face, bool toHead)? best = null;
        //    float bestScore = float.MaxValue;

        //    // кандидаты с головы
        //    foreach (var f in headCandidates)
        //    {
        //        float score = EvaluateCandidate(f, line, true);
        //        if (score > MAX_STEP_ERROR)
        //            continue;
        //        if (score < bestScore)
        //        {
        //            bestScore = score;
        //            best = (f, true);
        //        }
        //    }

        //    // кандидаты с хвоста
        //    foreach (var f in tailCandidates)
        //    {
        //        float score = EvaluateCandidate(f, line, false);
        //        if (score > MAX_STEP_ERROR)
        //            continue;
        //        if (score < bestScore)
        //        {
        //            bestScore = score;
        //            best = (f, false);
        //        }
        //    }

        //    // если после фильтров ничего не осталось — ослабляем условия
        //    if (!headCandidates.Any() && !tailCandidates.Any())
        //    {
        //        headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
        //            .Where(f => !line.FaceSet.Contains(f))
        //            .ToList();

        //        tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
        //            .Where(f => !line.FaceSet.Contains(f))
        //            .ToList();
        //    }

        //    return best;
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

        public void ComputeAreaSimilarity()
        {
            foreach (var f in _mesh.Faces)
            {
                float sum = 0f;
                int count = 0;

                for (int i = 0; i < 3; i++)
                {
                    int nId = f.Neighbors[i];
                    if (nId < 0) continue;

                    var n = _mesh.Faces[nId];

                    float diff = Math.Abs(f.Area - n.Area) / (f.Area + 1e-6f);
                    sum += diff;
                    count++;
                }

                f.AreaSimilarity = count > 0 ? sum / count : float.MaxValue;
            }
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
