using System.Numerics;
using Maligon.SubClasses;

namespace Maligon.WorkClasses
{
    public class OptimizationLine
    {
        public LinkedList<Face> Faces = new();
        public HashSet<int> VertexSet = new(); // теперь int!
        public HashSet<Face> FaceSet = new();

        public float LastStepScore = 0f;
        public bool HasLastStep = false;

        public float TotalError = 0;

        public Face Head => Faces.First?.Value;
        public Face Tail => Faces.Last?.Value;

        public bool CanGrow = true;


        public HashSet<int> FaceIds = new();


        public float AreaMean;

        public Queue<float> RecentCurvatures = new Queue<float>();

        public Vector3 LastDirection;

        public void RecalculateAreaMean()
        {
            if (Faces.Count == 0)
            {
                AreaMean = 0;
                return;
            }

            float sum = 0f;
            foreach (var f in Faces)
                sum += f.Area;

            AreaMean = sum / Faces.Count;
        }

        public float GetExpectedCurvature()
        {
            if (RecentCurvatures.Count == 0)
                return 0f;

            return RecentCurvatures.Average();
        }
    }
}
