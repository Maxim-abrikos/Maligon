using System;
using System.Collections.Generic;
using System.Text;

namespace Maligon.SubClasses
{
    public readonly struct Edge : IEquatable<Edge>
    {
        public readonly int A;
        public readonly int B;

        public Edge(int a, int b)
        {
            if (a < b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(Edge other)
        => A == other.A && B == other.B;

        public override bool Equals(object obj)
            => obj is Edge e && Equals(e);

        public override int GetHashCode()
            => HashCode.Combine(A, B);
    }
}
