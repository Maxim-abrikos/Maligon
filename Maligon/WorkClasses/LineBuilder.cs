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
        private MeshGraph _mesh;
        private OccupancyMap _occupancy;

        public LineBuilder(MeshGraph mesh, OccupancyMap occupancy)
        {
            _mesh = mesh;
            _occupancy = occupancy;
        }



        public OptimizationLine BuildLine(int maxIterations = 200)
        {
            Debug.WriteLine($"--- NEW LINE START: блять");
            ComputeAreaSimilarity();

            // 1. старт
            var start = _mesh.Faces
                .Where(f => !f.IsUsed)
                .OrderBy(f => f.AreaSimilarity)
                .FirstOrDefault();

            if (start == null)
                return null;

            var line = new OptimizationLine();

            AddFace(line, start, true, 0f);

            // 2. выбрать второго
            var firstNeighborId = start.Neighbors.FirstOrDefault(n => n >= 0);

            if (firstNeighborId < 0)
                return line;

            var second = _mesh.Faces[firstNeighborId];

            float secondScore = EvaluateCandidate(second, line, false);
            AddFace(line, second, false, secondScore);

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

                float score = EvaluateCandidate(
    next.Value.face,
    line,
    next.Value.toHead
);

                AddFace(line, next.Value.face, next.Value.toHead, score);

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

            Face bestFace = null;
            bool bestToHead = false;
            float bestScore = float.MaxValue;

            // --- локальная функция проверки топологии ---
            bool IsTopologicallyValid(Face candidate, Face anchor)
            {
                var candidateNeighbors = MeshUtils.GetNeighbors(candidate, _mesh);

                foreach (var n in candidateNeighbors)
                {
                    // если сосед уже в линии
                    if (line.FaceSet.Contains(n))
                    {
                        // допускается только если это anchor
                        if (n != anchor)
                            return false;
                    }
                }

                return true;
            }

            // --- проверка кандидатов для Head ---
            var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh);

            foreach (var f in headCandidates)
            {
                if (line.FaceSet.Contains(f))
                    continue;

                if (!IsTopologicallyValid(f, line.Head))
                    continue;

                float score = EvaluateCandidate(f, line, true);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFace = f;
                    bestToHead = true;
                }
            }

            // --- проверка кандидатов для Tail ---
            var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh);

            foreach (var f in tailCandidates)
            {
                if (line.FaceSet.Contains(f))
                    continue;

                if (!IsTopologicallyValid(f, line.Tail))
                    continue;

                float score = EvaluateCandidate(f, line, false);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFace = f;
                    bestToHead = false;
                }
            }

            if (bestFace == null)
                return null;

            // --- 🔴 АДАПТИВНАЯ ОСТАНОВКА ---

            if (line.HasLastStep)
            {
                // если качество резко ухудшилось — останавливаем линию
                // включаем контроль только когда линия сформировалась
                if (line.HasLastStep && line.Faces.Count >= 4)
                {
                    float prev = line.LastStepScore;

                    // защита от нулевых значений
                    if (prev > 1e-6f)
                    {
                        if (bestScore > prev * 2.0f)
                        {
                            Debug.WriteLine($"⛔ STOP: score jump {prev} -> {bestScore}");
                            return null;
                        }
                    }
                }
            }

            return (bestFace, bestToHead);

            return (bestFace, bestToHead);
        }

        //public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        //{
        //    if (line.Head == null || line.Tail == null)
        //        return null;

        //    float tauArea = 0.2f;
        //    float tauDir = 0.0f;
        //    float tauCurv = 0.3f;
        //    //float tauArea = 1.0f;     // было 0.2
        //    //float tauDir = -0.5f;     // было 0.0
        //    //float tauCurv = 1.0f;     // было 0.3

        //    var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
        //        .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
        //        .ToList();

        //    var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
        //        .Where(f => !line.FaceSet.Contains(f) && !IsBacktracking(f, line))
        //        .ToList();

        //    var allCandidates = new List<(Face face, bool toHead)>();
        //    allCandidates.AddRange(headCandidates.Select(f => (f, true)));
        //    allCandidates.AddRange(tailCandidates.Select(f => (f, false)));

        //    List<(Face face, bool toHead)> valid = new();

        //    foreach (var c in allCandidates)
        //    {

        //        var f = c.face;

        //        if (f.Id == line.Head.Id || f.Id == line.Tail.Id)
        //            continue;

        //        // 🔴 ЗАЩИТА ОТ ДУБЛИКАТОВ ПО ID
        //        if (line.FaceIds.Contains(f.Id))
        //            continue;

        //        if (f.IsUsed)
        //            continue;

        //        // площадь
        //        float areaDiff = Math.Abs(f.Area - line.AreaMean) / (line.AreaMean + 1e-6f);
        //        if (areaDiff > tauArea)
        //            continue;

        //        // направление
        //        var current = c.toHead ? line.Head : line.Tail;
        //        var newDir = GetDirection(current, f);

        //        float dot = Vector3.Dot(line.LastDirection, newDir);
        //        if (dot < tauDir)
        //            continue;

        //        //curvature
        //        if (line.Faces.Count >= 4)
        //        {
        //            float kNew = SignedCurvature(line.LastDirection, newDir, current.Normal);
        //            float kExpected = line.GetExpectedCurvature();

        //            float curvError = Math.Abs(kNew - kExpected);

        //            if (curvError > tauCurv)
        //                continue;
        //        }

        //        valid.Add(c);
        //    }

        //    // fallback
        //    if (valid.Count == 0)
        //    {
        //        foreach (var c in allCandidates)
        //        {
        //            if (!c.face.IsUsed)
        //                valid.Add(c);
        //        }

        //        if (valid.Count == 0)
        //            return null;
        //    }

        //    // выбор
        //    (Face face, bool toHead)? best = null;

        //    float bestCurv = float.MaxValue;
        //    float bestDot = -1f;

        //    foreach (var c in valid)
        //    {
        //        var current = c.toHead ? line.Head : line.Tail;
        //        var newDir = GetDirection(current, c.face);

        //        float dot = Vector3.Dot(line.LastDirection, newDir);

        //        float curvError = 0f;

        //        if (line.Faces.Count >= 4)
        //        {
        //            float kNew = SignedCurvature(line.LastDirection, newDir, current.Normal);
        //            float kExpected = line.GetExpectedCurvature();

        //            curvError = Math.Abs(kNew - kExpected);
        //        }

        //        if (curvError < bestCurv ||
        //           (Math.Abs(curvError - bestCurv) < 1e-5f && dot > bestDot))
        //        {
        //            bestCurv = curvError;
        //            bestDot = dot;
        //            best = c;
        //        }
        //    }

        //    return best;
        //}

        private float EvaluateCandidate(Face f, OptimizationLine line, bool toHead)
        {
            // --- 1. ПЛОЩАДЬ (относительное отклонение от среднего) ---
            float areaMean = line.Faces.Average(face => face.Area);
            float areaDiff = Math.Abs(f.Area - areaMean) / (areaMean + 1e-6f);

            // --- 2. НАПРАВЛЕНИЕ ---
            float dirPenalty = 0f;

            if (line.Faces.Count >= 2)
            {
                var anchor = toHead ? line.Head : line.Tail;

                var p0 = GetFaceCenter(anchor);
                var p1 = GetFaceCenter(f);

                var newDir = Vector3.Normalize(p1 - p0);

                var prev = toHead
                    ? line.Faces.Skip(1).FirstOrDefault()
                    : line.Faces.Reverse().Skip(1).FirstOrDefault();

                if (prev != null)
                {
                    var pPrev = GetFaceCenter(prev);
                    var prevDir = Vector3.Normalize(p0 - pPrev);

                    float dot = Vector3.Dot(prevDir, newDir);

                    // штраф за отклонение от прямой
                    dirPenalty = 1f - dot; // 0 = идеально, 2 = противоположно
                }
            }

            // --- 3. КРИВИЗНА ---
            float curvaturePenalty = 0f;

            if (line.Faces.Count >= 3)
            {
                var anchor = toHead ? line.Head : line.Tail;

                var p0 = GetFaceCenter(anchor);
                var p1 = GetFaceCenter(f);

                var newDir = Vector3.Normalize(p1 - p0);

                var prev = toHead
                    ? line.Faces.Skip(1).FirstOrDefault()
                    : line.Faces.Reverse().Skip(1).FirstOrDefault();

                if (prev != null)
                {
                    var pPrev = GetFaceCenter(prev);
                    var prevDir = Vector3.Normalize(p0 - pPrev);

                    var cross = Vector3.Cross(prevDir, newDir);

                    float curvature = cross.Length(); // величина изгиба

                    curvaturePenalty = curvature;
                }
            }

            // --- ИТОГОВЫЙ СКОР ---
            // БЕЗ весов — просто сумма (как ты хотел)
            return areaDiff + dirPenalty + curvaturePenalty;
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
        public void AddFace(OptimizationLine line, Face f, bool toHead, float stepScore)
        {

            if (line.Faces.Count > 0)
            {
                var anchor = toHead ? line.Head : line.Tail;

                var neighbors = MeshUtils.GetNeighbors(anchor, _mesh);

                if (!neighbors.Contains(f))
                {
                    Debug.WriteLine($"❌ INVALID ADD: {anchor.Id} -> {f.Id}");
                }
            }


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
            line.FaceIds.Add(f.Id);

            foreach (var v in MeshUtils.GetVertices(f))
                line.VertexSet.Add(v);

            line.TotalError += f.Error;

            line.LastStepScore = stepScore;
            line.HasLastStep = true;
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
