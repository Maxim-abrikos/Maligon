using Maligon.WorkClasses;
using System.Numerics;

namespace Maligon.SubClasses
{
    // Класс: MeshZone
    // Назначение:
    // Представляет одну зону сетки (регулярную, произвольную или проблемную).
    // Хранит полигоны зоны и базовые агрегированные метрики (например, среднюю площадь).
    // Также позволяет определить, является ли полигон граничным (имеет соседа вне зоны).

    public class MeshZone
    {
        public int Id { get; }
        public ZoneType Type { get; set; }

        public Vector3 DirA = Vector3.Zero;
        public Vector3 DirB = Vector3.Zero;
        public bool HasDirection = false;

        public List<Face> Polygons { get; } = new List<Face>();
        private HashSet<int> polygonIds = new HashSet<int>();
        public HashSet<int> PolygonIds => polygonIds;
        // Кэш средней площади (обновляется после формирования зоны)
        public float AverageArea { get; private set; }

        public MeshZone(int id)
        {
            Id = id;
        }

        // Добавление полигона в зону
        public void Add(Face face)
        {
            Polygons.Add(face);
            polygonIds.Add(face.Id);
        }

        // Пересчёт средней площади зоны
        public void RecalculateMetrics()
        {
            if (Polygons.Count == 0)
            {
                AverageArea = 0;
                return;
            }

            float sum = 0f;
            foreach (var f in Polygons)
            {
                sum += f.Area;
            }

            AverageArea = sum / Polygons.Count;
        }

        // Проверка: является ли полигон граничным
        // Полигон граничный, если у него есть сосед вне зоны
        public bool IsBoundary(Face face)
        {
            foreach (var neighborIndex in face.Neighbors)
            {
                if (!polygonIds.Contains(neighborIndex))
                    return true;
            }

            return false;
        }
    }
}
