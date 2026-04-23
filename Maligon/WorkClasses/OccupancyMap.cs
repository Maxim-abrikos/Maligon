using System;
using System.Collections.Generic;
using System.Text;
using Maligon.SubClasses;


namespace Maligon.WorkClasses
{
    public class OccupancyMap
    {
        public HashSet<Face> UsedFaces = new();
        public HashSet<int> UsedVertices = new();

        //public bool IsFree(Face f)
        //{
        //    //if (UsedFaces.Contains(f))
        //    //    return false;

        //    //foreach (var v in MeshUtils.GetVertices(f))
        //    //{
        //    //    if (UsedVertices.Contains(v))
        //    //        return false;
        //    //}
        //    return !UsedFaces.Contains(f);

        //    //return true;
        //}

        //public void Reserve(OptimizationLine line)
        //{
        //    foreach (var f in line.Faces)
        //        UsedFaces.Add(f);

        //    foreach (var v in line.VertexSet)
        //        UsedVertices.Add(v);
        //}
    }
}
