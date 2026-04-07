using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Maligon.SubClasses
{
    public sealed class Face
    {
        public int Id;

        public int V0;
        public int V1;
        public int V2;

        public int[] Neighbors = new int[3]; // индексы соседей (-1 если нет)

        public Vector3 Normal;
        public float Area;
        public float Error;

        public bool IsUsed; // занят линией
    }
}
