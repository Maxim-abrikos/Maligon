using Maligon.SubClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

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

        public (Face face, bool toHead)? SelectBestCandidate(OptimizationLine line)
        {
            if (line.Head == null || line.Tail == null)
                return null;

            var headCandidates = MeshUtils.GetNeighbors(line.Head, _mesh)
                .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
                .ToList();

            var tailCandidates = MeshUtils.GetNeighbors(line.Tail, _mesh)
                .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
                .ToList();

            var bestHead = headCandidates.OrderBy(f => f.Error).FirstOrDefault();
            var bestTail = tailCandidates.OrderBy(f => f.Error).FirstOrDefault();

            if (bestHead == null && bestTail == null)
                return null;

            if (bestHead == null)
                return (bestTail, false);

            if (bestTail == null)
                return (bestHead, true);

            return bestHead.Error < bestTail.Error
                ? (bestHead, true)
                : (bestTail, false);
        }

        //public Face SelectBestCandidate(OptimizationLine line)
        //{
        //    if (line.Head == null || line.Tail == null)
        //        return null;
        //    var candidates = new List<Face>();

        //    candidates.AddRange(
        //        MeshUtils.GetNeighbors(line.Head, _mesh)
        //            .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //    );

        //    candidates.AddRange(
        //        MeshUtils.GetNeighbors(line.Tail, _mesh)
        //            .Where(f => _occupancy.IsFree(f) && !line.FaceSet.Contains(f))
        //    );

        //    Debug.WriteLine($"Head neighbors: {MeshUtils.GetNeighbors(line.Head, _mesh).Count}");
        //    Debug.WriteLine($"Tail neighbors: {MeshUtils.GetNeighbors(line.Tail, _mesh).Count}");
        //    Debug.WriteLine($"Line length: {line.Faces.Count}");

        //    return candidates
        //        .OrderBy(f => f.Error)
        //        .FirstOrDefault();
        //}


        private bool IsBacktracking(Face candidate, OptimizationLine line)
        {
            if (line.Faces.Count < 2)
                return false;

            var second = line.Faces.First.Next?.Value;
            var beforeLast = line.Faces.Last.Previous?.Value;

            // если кандидат ведёт "назад" — запрещаем
            if (second != null && MeshUtils.IsNeighbor(candidate, second))
                return true;

            if (beforeLast != null && MeshUtils.IsNeighbor(candidate, beforeLast))
                return true;

            return false;
        }
        public void AddFace(OptimizationLine line, Face f, bool toHead)
        {
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

            foreach (var v in MeshUtils.GetVertices(f))
                line.VertexSet.Add(v);

            line.TotalError += f.Error;
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
    }
}
