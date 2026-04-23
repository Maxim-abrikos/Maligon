using System.Numerics;
using Maligon.SubClasses;

namespace Maligon.WorkClasses
{
    public class OptimizationLine
    {
        public HashSet<Face> BlockedFaces = new();

        public LinkedList<Face> Faces = new();
        public HashSet<Face> FaceSet = new();

        public HashSet<int> VertexSet = new();
        public Dictionary<int, int> VertexUsage = new();
        public Dictionary<int, List<Face>> VertexFaces = new();

        public Dictionary<int, Dictionary<int, int>> VertexEdgeUsage = new();

        public float TotalError = 0;

        public float AreaMean = 0;

        public Face Head => Faces.First?.Value;
        public Face Tail => Faces.Last?.Value;

        // --- STEP ---
        public float MeanStep = 0;
        public float LastStep = 0;
        public float PrevStep = 0;

        // --- ANGLE ---
        public float MeanAngle = 0;
        public float LastAngle = 0;
        public float PrevAngle = 0;

        // --- DIRECTION ---
        public Vector3 HeadDirection = Vector3.Zero;
        public Vector3 TailDirection = Vector3.Zero;

        // --- SIGN ---
        public int HeadLastSign = 0;
        public int HeadPrevSign = 0;

        public int TailLastSign = 0;
        public int TailPrevSign = 0;

        //

        public void AddToTail(Face face)
        {
            Faces.AddLast(face);
            FaceSet.Add(face);

            RegisterFaceVertices(face);

            RecalculateAreaMean();
        }

        // Класс: OptimizationLine
        // Метод: AddToHead
        // Назначение:
        // Добавляет полигон в начало линии

        public void AddToHead(Face face)
        {
            Faces.AddFirst(face);
            FaceSet.Add(face);

            RegisterFaceVertices(face);

            RecalculateAreaMean();
        }

        public bool Contains(Face face)
        {
            return FaceSet.Contains(face);
        }



        //public void RegisterFaceVerticesWithLimit(Face f)
        //{
        //    var verts = MeshUtils.GetVertices(f);

        //    foreach (var v in verts)
        //    {
        //        if (!VertexUsage.ContainsKey(v))
        //            VertexUsage[v] = 0;

        //        VertexUsage[v]++;

        //        // 🔴 если достигли 3 — блокируем
        //        if (VertexUsage[v] == 3)
        //        {
        //            LockVertex(v);
        //        }
        //    }
        //}

        //private void LockVertex(int vertex)
        //{
        //    // 🔴 ничего не делаем здесь — логика будет в builder
        //    // просто факт, что вершина достигла лимита
        //}

        public void RecalculateAreaMean()
        {
            if (Faces.Count == 0)
                return;

            AreaMean = Faces.Average(f => f.Area);
        }



        public void RegisterFaceVertices(Face f)
        {
            var verts = MeshUtils.GetVertices(f); // [v0, v1, v2]

            // --- вершины ---
            foreach (var v in verts)
            {
                if (!VertexUsage.ContainsKey(v))
                    VertexUsage[v] = 0;

                VertexUsage[v]++;
            }

            // --- рёбра ---
            RegisterEdge(verts[0], verts[1]);
            RegisterEdge(verts[1], verts[2]);
            RegisterEdge(verts[2], verts[0]);
        }

        private void RegisterEdge(int a, int b)
        {
            if (!VertexEdgeUsage.ContainsKey(a))
                VertexEdgeUsage[a] = new Dictionary<int, int>();

            if (!VertexEdgeUsage[a].ContainsKey(b))
                VertexEdgeUsage[a][b] = 0;

            VertexEdgeUsage[a][b]++;

            // --- в обе стороны ---
            if (!VertexEdgeUsage.ContainsKey(b))
                VertexEdgeUsage[b] = new Dictionary<int, int>();

            if (!VertexEdgeUsage[b].ContainsKey(a))
                VertexEdgeUsage[b][a] = 0;

            VertexEdgeUsage[b][a]++;
        }
        //public void RegisterFaceVertices(Face f)
        //{
        //    var verts = MeshUtils.GetVertices(f);

        //    foreach (var v in verts)
        //    {
        //        // --- VertexFaces ---
        //        if (!VertexFaces.ContainsKey(v))
        //            VertexFaces[v] = new List<Face>();

        //        VertexFaces[v].Add(f);

        //        // --- VertexUsage ---
        //        if (!VertexUsage.ContainsKey(v))
        //            VertexUsage[v] = 0;

        //        VertexUsage[v]++;
        //    }
        //}

        //public void RegisterFaceVertices(Face f)
        //{
        //    var verts = MeshUtils.GetVertices(f);

        //    foreach (var v in verts)
        //    {
        //        if (!VertexFaces.ContainsKey(v))
        //            VertexFaces[v] = new List<Face>();

        //        VertexFaces[v].Add(f);

        //        VertexUsage[v]++;
        //        //if (!VertexUsage.ContainsKey(v))
        //        //    VertexUsage[v] = 0;

        //        //VertexUsage[v]++;
        //    }
        //}

        public bool CanUseFaceByTopology(Face f, bool toHead)
        {
            var verts = MeshUtils.GetVertices(f);

            // определяем край линии
            Face edgeFace = toHead ? Head : Tail;

            if (edgeFace == null)
                return true;

            var edgeVerts = MeshUtils.GetVertices(edgeFace);

            foreach (var v in verts)
            {
                if (VertexUsage.TryGetValue(v, out int count))
                {
                    if (count >= 4)
                        return false; // 🔴 уже использовали 2 раза

                    if (count == 1)
                    {
                        continue;
                        // 🔴 разрешаем ТОЛЬКО если вершина принадлежит краю линии
                        //if (!edgeVerts.Contains(v))
                        //    return false;
                    }
                }
            }

            return true;
        }

        // =====================================================

        public void UpdateMetrics(Vector3 prevDir, Vector3 newDir, float step, bool toHead)
        {
            int n = Faces.Count;

            if (n < 2)
                return;

            // --- STEP ---
            PrevStep = LastStep;
            LastStep = step;

            if (n == 2)
                MeanStep = step;
            else
                MeanStep = (MeanStep * (n - 2) + step) / (n - 1);

            // --- ANGLE ---
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(prevDir, newDir), -1f, 1f));

            PrevAngle = LastAngle;
            LastAngle = angle;

            // --- SIGN ---
            var cross = Vector3.Cross(prevDir, newDir);
            int sign = cross.Length() < 1e-6f ? 0 : Math.Sign(cross.Z); // упрощённо

            if (toHead)
            {
                HeadPrevSign = HeadLastSign;
                HeadLastSign = sign;
            }
            else
            {
                TailPrevSign = TailLastSign;
                TailLastSign = sign;
            }
        }
    }
}
