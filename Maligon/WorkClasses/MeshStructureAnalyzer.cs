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



        //Работа с произвольными сетками

        private bool IsVertexUsageValid(OptimizationLine line, Face candidate, bool toHead)
        {
            var verts = MeshUtils.GetVertices(candidate);

            // --- базовое ограничение: не более 3 использований ---
            foreach (var v in verts)
            {
                int usage = 0;

                if (line.VertexUsage.TryGetValue(v, out int count))
                    usage = count;

                if (usage + 1 > 3)
                    return false;
            }

            // --- направляющее правило (только после разгона линии) ---
            if (line.Faces.Count < 4)
                return true;

            var edgeFace = toHead ? line.Head : line.Tail;

            var prevFace = toHead
                ? line.Faces.First.Next?.Value
                : line.Faces.Last.Previous?.Value;

            if (edgeFace == null || prevFace == null)
                return true;

            int sharedEdge = FindSharedEdgeIndex(edgeFace, prevFace);
            if (sharedEdge == -1)
                return true;

            // 🔴 получаем вершины общей грани (edgeFace <-> candidate)
            int candidateSharedEdge = FindSharedEdgeIndex(edgeFace, candidate);
            if (candidateSharedEdge == -1)
                return false;

            var edgeVerts = GetEdgeVertices(edgeFace, candidateSharedEdge);

            // 🔴 получаем направляющую вершину (не входящую в связь с prevFace)
            var edgeFaceVerts = MeshUtils.GetVertices(edgeFace);

            // вершины грани, по которой edgeFace соединён с prevFace
            var sharedVerts = GetEdgeVertices(edgeFace, sharedEdge);

            // ищем третью вершину (не входящую в sharedEdge)
            int guideVertex = -1;

            foreach (var v in edgeFaceVerts)
            {
                if (v != sharedVerts.Item1 && v != sharedVerts.Item2)
                {
                    guideVertex = v;
                    break;
                }
            }

            // 🔴 ключевая проверка направления
            if (guideVertex != edgeVerts.Item1 && guideVertex != edgeVerts.Item2)
                return false;

            return true;
        }


        private void RemoveTail(OptimizationLine line)
        {
            var f = line.Faces.Last.Value;

            line.Faces.RemoveLast();
            line.FaceSet.Remove(f);

            foreach (var v in MeshUtils.GetVertices(f))
            {
                if (line.VertexUsage.ContainsKey(v))
                    line.VertexUsage[v]--;
            }

            line.RecalculateAreaMean();
        }

        private void RemoveHead(OptimizationLine line)
        {
            var f = line.Faces.First.Value;

            line.Faces.RemoveFirst();
            line.FaceSet.Remove(f);

            foreach (var v in MeshUtils.GetVertices(f))
            {
                if (line.VertexUsage.ContainsKey(v))
                    line.VertexUsage[v]--;
            }

            line.RecalculateAreaMean();
        }

        private OptimizationLine BuildLineFromArbitraryZone(MeshZone zone)
        {
            if (zone.Type != ZoneType.Arbitrary)
                return null;

            var start = FindBestStartFace(zone);

            if (start == null)
                return null;

            var line = new OptimizationLine();
            line.AddToTail(start);

            // --- bootstrap: добавляем второго ---
            Face second = null;

            foreach (var nId in start.Neighbors)
            {
                if (nId < 0)
                    continue;

                var f = mesh.Faces[nId];

                if (!zone.PolygonIds.Contains(f.Id))
                    continue;

                if (!line.CanUseFaceByTopology(f, toHead: false))
                    continue;

                second = f;
                break;
            }

            if (second == null)
                return null;

            line.AddToTail(second);

            // --- основной рост ---
            while (true)
            {
                bool grew = false;

                // --- хвост ---
                var nextTail = FindNextArbitraryFromEnd(line, zone, toHead: false);
                if (nextTail != null)
                {
                    var prev = line.Tail;
                    var prev2 = line.Faces.Last.Previous?.Value;

                    line.AddToTail(nextTail);

                    if (prev2 != null)
                    {
                        var dir1 = GetFaceCenter(prev) - GetFaceCenter(prev2);
                        var dir2 = GetFaceCenter(nextTail) - GetFaceCenter(prev);

                        float step = dir2.Length();

                        line.UpdateMetrics(dir1, dir2, step, toHead: false);
                    }

                    grew = true;
                }

                // --- голова ---
                var nextHead = FindNextArbitraryFromEnd(line, zone, toHead: true);
                if (nextHead != null)
                {
                    var prev = line.Head;
                    var prev2 = line.Faces.First.Next?.Value;

                    line.AddToHead(nextHead);

                    if (prev2 != null)
                    {
                        var dir1 = GetFaceCenter(prev) - GetFaceCenter(prev2);
                        var dir2 = GetFaceCenter(nextHead) - GetFaceCenter(prev);

                        float step = dir2.Length();

                        line.UpdateMetrics(dir1, dir2, step, toHead: true);
                    }

                    grew = true;
                }

                if (!grew)
                    break;
            }

            // --- минимальный размер ---
            if (line.Faces.Count < 4)
                return null;

            // --- нормализация ---
            NormalizeLineForCollapse(line);

            return line;
        }

        private void NormalizeLineForCollapse(OptimizationLine line)
        {
            // --- делаем чётной ---
            if (line.Faces.Count % 2 != 0)
            {
                // удаляем менее "вписанный" край
                var head = line.Head;
                var tail = line.Tail;

                float headScore = MathF.Abs(head.Area - line.AreaMean);
                float tailScore = MathF.Abs(tail.Area - line.AreaMean);

                if (headScore > tailScore)
                    RemoveHead(line);
                else
                    RemoveTail(line);
            }
        }

        private bool AreFacesConnected(Face a, Face b)
        {
            foreach (var n in a.Neighbors)
            {
                if (n == b.Id)
                    return true;
            }

            return false;
        }
        private Face FindNextArbitraryFromEnd(OptimizationLine line, MeshZone zone, bool toHead)
        {
            var edgeFace = toHead ? line.Head : line.Tail;

            if (edgeFace == null)
                return null;

            var prevFace = toHead
                ? line.Faces.First.Next?.Value
                : line.Faces.Last.Previous?.Value;

            var candidates = new List<Face>();

            // --- собираем кандидатов ---
            for (int i = 0; i < 3; i++)
            {
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

                if (!IsVertexUsageValid(line, f, toHead))
                    continue;
                if (!PassesThirdFromEndRule(line, f, toHead))
                    continue;

                candidates.Add(f);
            }

            if (candidates.Count == 0)
                return null;

            // --- если нет prevFace — берём любой допустимый ---
            if (prevFace == null)
                return candidates[0];

            // --- определяем фазу ---
            bool isClosingPhase = (line.Faces.Count % 2 == 1);

            Face best = null;

            foreach (var f in candidates)
            {
                if (isClosingPhase)
                {
                    // 🔴 ФАЗА ЗАКРЫТИЯ КВАДА

                    // кандидат должен быть связан и с edgeFace, и с prevFace
                    if (!AreFacesConnected(f, prevFace))
                        continue;

                    return f; // сразу берём — это приоритет
                }
                else
                {
                    // 🔵 ФАЗА ОТКРЫТИЯ

                    // кандидат НЕ должен быть связан с prevFace
                    if (AreFacesConnected(f, prevFace))
                        continue;

                    // можно дополнительно оставить только ближайшего по площади
                    if (best == null)
                        best = f;
                    else
                    {
                        float d1 = MathF.Abs(f.Area - edgeFace.Area);
                        float d2 = MathF.Abs(best.Area - edgeFace.Area);

                        if (d1 < d2)
                            best = f;
                    }
                }
            }

            return best;
        }

        // Проверка: кандидат не должен "цепляться" за старую часть линии
        // Разрешены связи только с 1–2 крайними полигонами
        private bool PassesThirdFromEndRule(OptimizationLine line, Face candidate, bool toHead)
        {
            if (line == null || candidate == null)
                return false;

            // короткая линия — не ограничиваем
            if (line.Faces.Count < 3)
                return true;

            int connections = 0;

            foreach (var f in line.Faces)
            {
                if (ShareAnyVertex(candidate, f))
                {
                    connections++;

                    // 🔴 ключ: если больше 2 — это уже "заворот"
                    if (connections > 2)
                        return false;
                }
            }

            return true;
        }

        private bool ShareAnyVertex(Face a, Face b)
        {
            if (a == null || b == null)
                return false;

            // Проверяем все 3x3 комбинации
            if (a.V0 == b.V0 || a.V0 == b.V1 || a.V0 == b.V2) return true;
            if (a.V1 == b.V0 || a.V1 == b.V1 || a.V1 == b.V2) return true;
            if (a.V2 == b.V0 || a.V2 == b.V1 || a.V2 == b.V2) return true;

            return false;
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
                //    continue; ВОТ ТУТ ГОВНО
                if (!IsEdgeBasedContinuation(edgeFace, f, line))
                    continue;
                if (!IsVertexUsageValid(line, f, toHead))
                    continue;
                //if (PassesThirdFromEndRule(line, f, toHead))
                //    continue;

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

            // =====================================================
            // 1. ПРОБУЕМ РЕГУЛЯРНЫЕ ЗОНЫ
            // =====================================================

            var regularZones = zones
                .Where(z => z.Type == ZoneType.RegularConstant && z.Polygons.Count >= 4)
                .OrderByDescending(z => z.Polygons.Count)
                .ToList();

            foreach (var zone in regularZones)
            {
                var line = BuildLineFromZone(zone);

                if (line != null && line.Faces.Count >= 4)
                {
                    EnsureEvenLine(line);
                    return line;
                }
            }

            // =====================================================
            // 2. FALLBACK: ПРОИЗВОЛЬНЫЕ ЗОНЫ
            // =====================================================

            var arbitraryZones = zones
                .Where(z => z.Type == ZoneType.Arbitrary && z.Polygons.Count >= 4)
                .OrderByDescending(z => z.Polygons.Count)
                .ToList();

            foreach (var zone in arbitraryZones)
            {
                var line = BuildLineFromArbitraryZone(zone);

                if (line != null && line.Faces.Count >= 4)
                {
                    EnsureEvenLine(line);
                    return line;
                }
            }

            // =====================================================
            // 3. НИЧЕГО НЕ НАШЛИ
            // =====================================================

            return null;
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
