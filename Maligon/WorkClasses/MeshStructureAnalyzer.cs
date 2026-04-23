using Maligon.SubClasses;
using System.Numerics;
using System.Security.Policy;

namespace Maligon.WorkClasses
{
    // Тип зоны сетки
    public enum ZoneType
    {
        Unknown,
        RegularConstant,
        RegularNonConstant,
        Arbitrary,
        Problematic
    }
    public class MeshStructureAnalyzer
    {
        // Класс: MeshStructureAnalyzer
        // Метод: Analyze
        // Назначение:
        // Основной метод анализа:
        // - разбивает меш на зоны (пока только по площади)
        // - сохраняет список зон
        // - подготавливает данные для дальнейшей классификации
        private readonly MeshGraph mesh;
        private readonly OccupancyMap occupancy; // пока не используется активно, но оставляем

        private List<MeshZone> zones;

        // Допуски (можно будет потом вынести в настройки)
        private const float AREA_TOLERANCE = 0.15f; // было ±7%

        public IReadOnlyList<MeshZone> Zones => zones;

        public MeshStructureAnalyzer(MeshGraph mesh, OccupancyMap occupancy)
        {
            this.mesh = mesh;
            this.occupancy = occupancy;
        }
        public void Analyze()
        {
            zones = new List<MeshZone>();

            // Быстрая проверка принадлежности полигона зоне
            var visited = new bool[mesh.Faces.Count];

            int zoneId = 0;

            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (visited[i])
                    continue;

                var startFace = mesh.Faces[i];

                var zone = new MeshZone(zoneId++);

                // Заполняем зону через flood fill
                FloodFillZone(startFace, zone, visited);

                // Пересчёт метрик зоны
                zone.RecalculateMetrics();
                ClassifyZone(zone);

                zones.Add(zone);
            }
        }








        //Проверка чётности линии в конце
        private void EnsureEvenLine(OptimizationLine line)
        {
            if (line.Faces.Count % 2 == 0)
                return;

            if (line.Faces.Count < 3)
            {
                // слишком короткая — просто очищаем
                line.Faces.Clear();
                line.FaceSet.Clear();
                return;
            }

            var head = line.Head;
            var tail = line.Tail;

            var headNext = line.Faces.First.Next?.Value;
            var tailPrev = line.Faces.Last.Previous?.Value;

            if (headNext == null || tailPrev == null)
                return;

            float headScore = EvaluateEnd(headNext, head);
            float tailScore = EvaluateEnd(tailPrev, tail);

            // удаляем худший конец
            if (headScore < tailScore)
                RemoveHead(line);
            else
                RemoveTail(line);
        }
        private void RebuildVertexData(OptimizationLine line)
        {
            line.VertexUsage.Clear();
            line.VertexEdgeUsage.Clear();

            foreach (var f in line.Faces)
                line.RegisterFaceVertices(f);

            line.RecalculateAreaMean();
        }
        private void RemoveTail(OptimizationLine line)
        {
            var f = line.Tail;

            line.Faces.RemoveLast();
            line.FaceSet.Remove(f);

            // ⚠️ упрощённо: пересчитать всё
            RebuildVertexData(line);
        }

        private void RemoveHead(OptimizationLine line)
        {
            var f = line.Head;

            line.Faces.RemoveFirst();
            line.FaceSet.Remove(f);

            RebuildVertexData(line);
        }

        private float EvaluateEnd(Face prev, Face end)
        {
            var c1 = GetFaceCenter(prev);
            var c2 = GetFaceCenter(end);

            var dir = c2 - c1;

            if (dir.LengthSquared() < 1e-6f)
                return 0;

            dir = Vector3.Normalize(dir);

            // если есть глобальное направление — используем
            // иначе просто длину шага
            return dir.Length();
        }

        //Всё ещё проверка чётности в конце 



        public OptimizationLine BuildLineFromZone(MeshZone zone)
        {
            if (zone.Type != ZoneType.RegularConstant)
                return null;

            var line = new OptimizationLine();

            if (!BuildInitialQuad(line, zone, out _))
                return null;

            //UpdateZoneDirection(zone, line); // Вот тут хлам

            GrowLine(line, zone);

            return line;
        }

        // формирует первые 4 полигона
        private bool BuildInitialQuad(OptimizationLine line, MeshZone zone, out Face second)
        {
            var start = FindBestStartFace(zone);
            if (start == null)
            {
                second = null;
                return false;
            }

            line.AddToTail(start);

            int edge = FindLongestEdgeIndex(start);

            int nId = start.Neighbors[edge];
            if (nId < 0)
            {
                second = null;
                return false;
            }

            second = mesh.Faces[nId];

            if (!zone.PolygonIds.Contains(second.Id))
                return false;

            line.AddToTail(second);

            var third = FindBestAdjacentCandidate(second, line, zone);
            if (third == null)
                return false;

            line.AddToTail(third);

            var fourth = FindFourthFromStrip(second, third, line, zone);
            if (fourth == null)
                return false;

            line.AddToTail(fourth);

            return true;
        }


        private void GrowLine(OptimizationLine line, MeshZone zone)
        {
            bool grew;

            do
            {
                grew = false;

                // --- рост в хвост ---
                var tailCandidate = FindNextFromEnd(line, zone, toHead: false);
                if (tailCandidate != null)
                {
                    line.AddToTail(tailCandidate);
                    grew = true;
                }

                // --- рост в голову ---
                var headCandidate = FindNextFromEnd(line, zone, toHead: true);
                if (headCandidate != null)
                {
                    line.AddToHead(headCandidate);
                    grew = true;
                }

            } while (grew);
        }


        private Face FindNextFromEnd(OptimizationLine line, MeshZone zone, bool toHead)
        {
            var edgeFace = toHead ? line.Head : line.Tail;

            var prevFace = toHead
                ? line.Faces.First.Next?.Value
                : line.Faces.Last.Previous?.Value;

            if (edgeFace == null || prevFace == null)
                return null;

            int sharedEdge = FindSharedEdgeIndex(edgeFace, prevFace);

            bool expectDiagonal = !IsEdgeDiagonal(edgeFace, sharedEdge);

            for (int i = 0; i < 3; i++)
            {
                if (i == sharedEdge)
                    continue;

                int nId = edgeFace.Neighbors[i];
                if (nId < 0)
                    continue;

                var f = mesh.Faces[nId];

                if (!zone.PolygonIds.Contains(f.Id))
                    continue;

                if (line.Contains(f))
                    continue;

                if (!line.CanUseFaceByTopology(f, toHead))
                    continue;
                //if (!IsStrictStripContinuation(edgeFace, prevFace, f, line))
                //    continue;
                if (!IsEdgeBasedContinuation(edgeFace, f, line))
                    continue;

                bool isDiagonal = IsEdgeDiagonal(edgeFace, i);

                if (isDiagonal != expectDiagonal)
                    continue;

                return f;
            }

            return null;
        }

        private Vector3 GetFaceCenter(Face f)
        {
            var v0 = mesh.GetVertex(f.V0);
            var v1 = mesh.GetVertex(f.V1);
            var v2 = mesh.GetVertex(f.V2);

            return (v0 + v1 + v2) / 3f;
        }

        private bool IsEdgeBasedContinuation(Face edgeFace, Face candidate, OptimizationLine line)
        {
            int edgeIndex = FindSharedEdgeIndex(edgeFace, candidate);

            if (edgeIndex == -1)
                return false;

            var (v0, v1) = GetEdgeVertices(edgeFace, edgeIndex);

            return CheckVertexEdgePattern(v0, line) &&
                   CheckVertexEdgePattern(v1, line);
        }

        private bool CheckVertexEdgePattern(int v, OptimizationLine line)
        {
            if (!line.VertexEdgeUsage.TryGetValue(v, out var edges))
                return false;

            // количество уникальных направлений
            int uniqueEdges = edges.Count;

            // суммарный "вес" (с учётом двойных рёбер)
            int totalWeight = edges.Values.Sum();

            // --- вариант 1: простая вершина (2 рёбра)
            if (uniqueEdges == 2 && totalWeight == 2)
                return true;

            // --- вариант 2: правильная "средняя" вершина полосы
            // 3 направления, одно из них используется дважды
            if (uniqueEdges == 3 && totalWeight == 4 && edges.Values.Any(c => c == 2))
                return true;

            return false;
        }




        private bool CheckVertex(int v, Face edgeFace, Face prevFace, OptimizationLine line)
        {
            if (!line.VertexFaces.TryGetValue(v, out var faces))
                return false;

            int count = faces.Count;

            int edgeCount = faces.Count(f => f.Id == edgeFace.Id);
            int prevCount = faces.Count(f => f.Id == prevFace.Id);

            // --- вариант 1: (2) от edgeFace
            if (count == 2 && edgeCount == 2)
                return true;

            // --- вариант 2: (3) = 2 от edgeFace + 1 от prev
            if (count == 3 && edgeCount == 2 && prevCount == 1)
                return true;

            return false;
        }




        private (int, int) GetEdgeVertices(Face f, int edgeIndex)
        {
            return edgeIndex switch
            {
                0 => (f.V0, f.V1),
                1 => (f.V1, f.V2),
                2 => (f.V2, f.V0),
                _ => throw new ArgumentOutOfRangeException(nameof(edgeIndex))
            };
        }

        //private bool IsValidStripContinuation(Face edgeFace, Face candidate, OptimizationLine line)
        //{
        //    int sharedEdge = FindSharedEdgeIndex(edgeFace, candidate);

        //    if (sharedEdge == -1)
        //        return false;

        //    var edgeVerts = GetEdgeVertices(edgeFace, sharedEdge);

        //    int v0 = edgeVerts.Item1;
        //    int v1 = edgeVerts.Item2;

        //    int count0 = line.VertexUsage.TryGetValue(v0, out var c0) ? c0 : 0;
        //    int count1 = line.VertexUsage.TryGetValue(v1, out var c1) ? c1 : 0;

        //    // 🔴 ключевое правило: (2 + 3)
        //    return (count0 == 2 && count1 == 3) ||
        //           (count0 == 3 && count1 == 2);
        //}


        private Face FindFourthFromStrip(Face second, Face third, OptimizationLine line, MeshZone zone)
        {
            int sharedEdge = FindSharedEdgeIndex(third, second);

            Face best = null;

            for (int i = 0; i < 3; i++)
            {
                if (i == sharedEdge)
                    continue;

                int nId = third.Neighbors[i];

                if (nId < 0)
                    continue;

                var f = mesh.Faces[nId];

                if (!zone.PolygonIds.Contains(f.Id))
                    continue;

                if (line.Contains(f))
                    continue;

                if (f.Id == second.Id)
                    continue;

                // 🔴 КЛЮЧЕВОЙ ФИЛЬТР
                bool isDiagonal = IsEdgeDiagonal(third, i);

                if (!isDiagonal)
                    continue;

                return f; // берём первый валидный диагональный
            }

            return null;
        }

        private Face FindBestAdjacentCandidate(Face from, OptimizationLine line, MeshZone zone)
        {
            Face best = null;
            float bestScore = float.MaxValue;

            foreach (var nId in from.Neighbors)
            {
                if (nId < 0)
                    continue;

                var f = mesh.Faces[nId];

                if (!zone.PolygonIds.Contains(f.Id))
                    continue;

                if (line.Contains(f))
                    continue;

                float diff = MathF.Abs(f.Area - from.Area) / from.Area;

                if (diff < bestScore)
                {
                    bestScore = diff;
                    best = f;
                }
            }

            return best;
        }

        public OptimizationLine BuildLine()
        {
            if (zones == null || zones.Count == 0)
                return null;

            // фильтруем зоны
            var candidates = zones
                .Where(z => z.Type == ZoneType.RegularConstant && z.Polygons.Count >= 4)
                .ToList();

            if (candidates.Count == 0)
                return null;

            // пока выбираем самую большую
            var bestZone = candidates
                .OrderByDescending(z => z.Polygons.Count)
                .First();

            var line = BuildLineFromZone(bestZone);

            EnsureEvenLine(line);

            return line;
            //return BuildLineFromZone(bestZone);
        }

        private bool IsEdgeDiagonal(Face face, int edgeIndex)
        {
            float d0 = Distance(face.V0, face.V1);
            float d1 = Distance(face.V1, face.V2);
            float d2 = Distance(face.V2, face.V0);

            float max = MathF.Max(d0, MathF.Max(d1, d2));

            float edgeLength = edgeIndex switch
            {
                0 => d0,
                1 => d1,
                _ => d2
            };

            return edgeLength >= max * 0.85f; // допуск
        }

        // Находит индекс ребра в face, которое общее с other

        private int FindSharedEdgeIndex(Face face, Face other)
        {
            for (int i = 0; i < 3; i++)
            {
                if (face.Neighbors[i] == other.Id)
                    return i;
            }

            return -1;
        }

        // Находит индекс самой длинной грани треугольника

        private int FindLongestEdgeIndex(Face face)
        {
            float d0 = Distance(face.V0, face.V1);
            float d1 = Distance(face.V1, face.V2);
            float d2 = Distance(face.V2, face.V0);

            if (d0 >= d1 && d0 >= d2) return 0;
            if (d1 >= d0 && d1 >= d2) return 1;
            return 2;
        }






        private Face FindBestStartFace(MeshZone zone)
        {
            Face bestFace = null;
            float bestScore = float.MaxValue;

            foreach (var face in zone.Polygons)
            {
                // пропускаем граничные
                if (zone.IsBoundary(face))
                    continue;

                float score = CalculateFaceSimilarity(face, zone);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFace = face;
                }
            }

            return bestFace;
        }

        private float CalculateFaceSimilarity(Face face, MeshZone zone)
        {
            float sumDiff = 0f;
            int count = 0;

            foreach (var neighborIndex in face.Neighbors)
            {
                if (neighborIndex < 0)
                    continue;

                var neighbor = mesh.Faces[neighborIndex];

                // учитываем только соседей внутри зоны
                if (!zoneContains(zone, neighbor.Id))
                    continue;

                float diff = MathF.Abs(face.Area - neighbor.Area) / face.Area;

                sumDiff += diff;
                count++;
            }

            if (count == 0)
                return float.MaxValue;

            return sumDiff / count;
        }

        // Проверка принадлежности полигона зоне (через HashSet)

        private bool zoneContains(MeshZone zone, int faceId)
        {
            return zone.PolygonIds.Contains(faceId);
        }

        // Формирует одну зону через flood fill:
        // - сначала сравнение с стартовым полигоном
        // - затем со средним по зоне
        // - учитывает допуск по площади

        private void FloodFillZone(Face startFace, MeshZone zone, bool[] visited)
        {
            var queue = new Queue<Face>();

            queue.Enqueue(startFace);
            visited[startFace.Id] = true;

            zone.Add(startFace);

            float startArea = startFace.Area;

            // будем обновлять среднюю динамически
            float areaSum = startArea;
            int count = 1;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var neighborIndex in current.Neighbors)
                {
                    if (neighborIndex < 0)
                        continue;

                    if (visited[neighborIndex])
                        continue;

                    var neighbor = mesh.Faces[neighborIndex];

                    // --- Проверка площади ---

                    float neighborArea = neighbor.Area;

                    // 1. Сравнение со стартовым (на раннем этапе)
                    bool matchesStart = IsAreaSimilar(neighborArea, startArea);

                    // 2. Сравнение со средним (если уже есть хотя бы 3 полигона)
                    float avgArea = areaSum / count;
                    bool matchesAverage = count >= 3 && IsAreaSimilar(neighborArea, avgArea);

                    if (matchesStart || matchesAverage)
                    {
                        visited[neighborIndex] = true;

                        zone.Add(neighbor);
                        queue.Enqueue(neighbor);

                        areaSum += neighborArea;
                        count++;
                    }
                }
            }
        }

        // Класс: MeshStructureAnalyzer
        // Метод: ClassifyZone
        // Назначение:
        // Определяет тип зоны на основе анализа её структуры:
        // - количество рёбер у вершин
        // - наличие "длинных" рёбер (диагоналей)
        // - стабильность структуры

        private void ClassifyZone(MeshZone zone)
        {
            // 1. Маленькие зоны сразу проблемные
            if (zone.Polygons.Count < 4)
            {
                zone.Type = ZoneType.Problematic;
                return;
            }

            int totalVerticesChecked = 0;

            int regularLikeVertices = 0;     // похожи на регулярную
            int constantLikeVertices = 0;    // похожи на постоянную
            int nonConstantLikeVertices = 0; // похожи на непостоянную

            // Соберём уникальные вершины зоны
            var vertexToFaces = new Dictionary<int, List<Face>>();

            foreach (var face in zone.Polygons)
            {
                AddVertex(vertexToFaces, face.V0, face);
                AddVertex(vertexToFaces, face.V1, face);
                AddVertex(vertexToFaces, face.V2, face);
            }

            foreach (var pair in vertexToFaces)
            {
                var vertexIndex = pair.Key;
                var faces = pair.Value;

                // анализируем только если достаточно данных
                if (faces.Count < 2)
                    continue;

                totalVerticesChecked++;

                // собираем рёбра, исходящие из вершины (внутри зоны)
                var edgeLengths = CollectEdgeLengths(vertexIndex, faces);

                if (edgeLengths.Count < 3)
                    continue;

                // определяем длинные рёбра
                float avg = edgeLengths.Average();

                int longEdges = 0;
                int shortEdges = 0;

                foreach (var len in edgeLengths)
                {
                    if (len > avg * 1.2f)
                        longEdges++;
                    else
                        shortEdges++;
                }

                // --- Классификация вершины ---

                // Регулярная: есть разделение длинных/коротких
                if (longEdges >= 1 && shortEdges >= 2)
                {
                    regularLikeVertices++;

                    // Постоянная: ожидаем ~2 длинных
                    if (longEdges == 2)
                        constantLikeVertices++;
                    else
                        nonConstantLikeVertices++;
                }
            }

            // --- Принятие решения ---

            if (totalVerticesChecked == 0)
            {
                zone.Type = ZoneType.Problematic;
                return;
            }

            float regularRatio = (float)regularLikeVertices / totalVerticesChecked;
            float constantRatio = (float)constantLikeVertices / totalVerticesChecked;

            // Порог можно будет настраивать
            if (regularRatio > 0.6f)
            {
                if (constantRatio > 0.5f)
                    zone.Type = ZoneType.RegularConstant;
                else
                    zone.Type = ZoneType.RegularNonConstant;
            }
            else
            {
                zone.Type = ZoneType.Arbitrary;
            }
        }
        // Собирает длины рёбер, исходящих из вершины, внутри зоны
        // Длина ребра

        private float Distance(int vA, int vB)
        {
            var a = mesh.GetVertex(vA);
            var b = mesh.GetVertex(vB);

            return Vector3.Distance(a, b);
        }
        private List<float> CollectEdgeLengths(int vertexIndex, List<Face> faces)
        {
            var result = new List<float>();

            foreach (var face in faces)
            {
                int v0 = face.V0;
                int v1 = face.V1;
                int v2 = face.V2;

                if (v0 == vertexIndex)
                {
                    result.Add(Distance(v0, v1));
                    result.Add(Distance(v0, v2));
                }
                else if (v1 == vertexIndex)
                {
                    result.Add(Distance(v1, v0));
                    result.Add(Distance(v1, v2));
                }
                else if (v2 == vertexIndex)
                {
                    result.Add(Distance(v2, v0));
                    result.Add(Distance(v2, v1));
                }
            }

            return result;
        }

        // Добавляет связь вершина → полигон

        private void AddVertex(Dictionary<int, List<Face>> map, int vertex, Face face)
        {
            if (!map.TryGetValue(vertex, out var list))
            {
                list = new List<Face>();
                map[vertex] = list;
            }

            list.Add(face);
        }

        // Класс: MeshStructureAnalyzer
        // Метод: IsAreaSimilar
        // Назначение:
        // Проверяет, находятся ли площади в пределах допуска

        private bool IsAreaSimilar(float a, float b)
        {
            float min = b * (1f - AREA_TOLERANCE);
            float max = b * (1f + AREA_TOLERANCE);

            return a >= min && a <= max;
        }

    
    }
}
