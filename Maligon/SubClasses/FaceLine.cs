using System;
using System.Collections.Generic;
using System.Text;

namespace Maligon.SubClasses
{
    public sealed class FaceLine
    {
        public LinkedList<int> Faces = new(); // порядок важен

        public HashSet<int> FaceSet = new(); // быстрый lookup

        public int First => Faces.First.Value;
        public int Last => Faces.Last.Value;

        public void AddFirst(int faceId)
        {
            Faces.AddFirst(faceId);
            FaceSet.Add(faceId);
        }

        public void AddLast(int faceId)
        {
            Faces.AddLast(faceId);
            FaceSet.Add(faceId);
        }

        public bool Contains(int faceId) => FaceSet.Contains(faceId);

        public int Count => Faces.Count;
    }
}
