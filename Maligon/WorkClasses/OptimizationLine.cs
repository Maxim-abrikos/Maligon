using System;
using System.Collections.Generic;
using System.Text;
using Maligon.SubClasses;

namespace Maligon.WorkClasses
{
    public class OptimizationLine
    {
        public LinkedList<Face> Faces = new();
        public HashSet<int> VertexSet = new(); // теперь int!
        public HashSet<Face> FaceSet = new();

        public float TotalError = 0;

        public Face Head => Faces.First?.Value;
        public Face Tail => Faces.Last?.Value;

        public bool CanGrow = true;
    }
}
