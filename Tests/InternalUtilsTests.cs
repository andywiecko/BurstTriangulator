using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using NUnit.Framework;
using System.Linq;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class InternalUtilsTests
    {
        private static readonly TestCaseData[] angleTestData = new TestCaseData[]
        {
            new(math.double2(1, 0), math.double2(0, 1), math.double2(0,0), math.PI_DBL / 4) { TestName = "Test case 1 - canonical vectors" },
            new(math.double2(0, 1), math.double2(1, 0), math.double2(0,0), math.PI_DBL / 4){ TestName = "Test case 2 - canonical vectors (swapped)" },
            new(math.double2(2, 2), math.double2(3, 2), math.double2(2, 4), math.atan(1.0/2.0)){ TestName = "Test case 3 - no vertex at origin" },
            // A completely degenerate triangle (all vertices are the same) should also return 0, but the current implementation thinks all 3 inner angles are 180 degrees. Fix in the future.
            new(math.double2(1, 0), math.double2(1, 0), math.double2(1, 0), 0){ TestName = "Test case 4 - 3 identical vertices", RunState = NUnit.Framework.Interfaces.RunState.Ignored },
            new(math.double2(1, 0), math.double2(1, 0), math.double2(10, 0), 0){ TestName = "Test case 5 - 2 identical vertices" },
            new(math.double2(1, 0), math.double2(0, 0), math.double2(-1, 0), 0){ TestName = "Test case 6 - colinear vertices" },
            new(math.double2(0, 0), math.double2(10, 0), math.double2(5, 73), 2*math.atan(5.0/73.0)){ TestName = "Test case 7 - arbitrary angle" },
        };

        [Test, TestCaseSource(nameof(angleTestData))]
        public void AngleTest(double2 a, double2 b, double2 c, double expectedSmallestInnerAngle)
        {
            var upper = expectedSmallestInnerAngle + 0.001;
            var lower = expectedSmallestInnerAngle - 0.001;

            Assert.IsTrue(new DoubleUtils().SmallestInnerAngleIsBelowThreshold(a,b,c, (float)upper));
            Assert.IsFalse(new DoubleUtils().SmallestInnerAngleIsBelowThreshold(a,b,c, (float)lower));

            Assert.IsTrue(new FloatUtils().SmallestInnerAngleIsBelowThreshold((float2)a,(float2)b,(float2)c, (float)upper));
            Assert.IsFalse(new FloatUtils().SmallestInnerAngleIsBelowThreshold((float2)a,(float2)b,(float2)c, (float)lower));

            // Integers do not support this right now. Commit ad22afe has an implementation.
            // Assert.IsTrue(new IntUtils().SmallestInnerAngleIsBelowThreshold((int2)a,(int2)b,(int2)c, (float)upper));
            // Assert.IsFalse(new IntUtils().SmallestInnerAngleIsBelowThreshold((int2)a,(int2)b,(int2)c, (float)lower));
        }

        private static readonly TestCaseData[] area2testData = new TestCaseData[]
        {
            new(math.double2(0, 0), math.double2(1, 0), math.double2(0, 1), 1) { TestName = "Test case 1" },
            new(math.double2(0, 0), math.double2(2, 0), math.double2(0, 1), 2) { TestName = "Test case 1" },
            new(math.double2(0, 0), math.double2(6, 0), math.double2(0, 7), 42) { TestName = "Test case 1" },
        }.SelectMany(i =>
        {
            var args = i.Arguments;
            return new TestCaseData[]
            {
                new(args[0], args[1], args[2], args[3]) { TestName = i.TestName + ": abc" },
                new(args[2], args[0], args[1], args[3]) { TestName = i.TestName + ": cab" },
                new(args[1], args[2], args[0], args[3]) { TestName = i.TestName + ": bca" },
                new(args[2], args[1], args[0], args[3]) { TestName = i.TestName + ": cba" },
                new(args[1], args[0], args[2], args[3]) { TestName = i.TestName + ": bac" },
                new(args[0], args[2], args[1], args[3]) { TestName = i.TestName + ": acb" },
            };
        }
        ).ToArray();

        [Test, TestCaseSource(nameof(area2testData))]
        public void Area2Test(double2 a, double2 b, double2 c, double expected)
        {
            var result = UnsafeTriangulator<double, double2, double, AffineTransform64, DoubleUtils>.Area2(a, b, c);
            Assert.That(result, Is.EqualTo(expected).Within(1e-9));
        }

        private static readonly TestCaseData[] edegeVsEdgeTestData = new TestCaseData[]
        {
            new(
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(2, 0),
                math.double2(2, 1)
            ) { TestName = "Case 1 (ortogonal without intersection)", ExpectedResult = false},
            new(
                math.double2(-1, 0),
                math.double2(1, 0),
                math.double2(0, -1),
                math.double2(0, 1)
            ) { TestName = "Case 2 (ortogonal with intersection)", ExpectedResult = true},
            new(
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(2, 0),
                math.double2(3, 0)
            ) { TestName = "Case 3 (collinear without intersection)", ExpectedResult = false},
            new(
                math.double2(-703d, 290.42d),
                math.double2(-701.15d, 288.57d),
                math.double2(-193.43d, -219.15d),
                math.double2(-186.58d, -226d)
            ) { TestName = "Case 4 (gh issue #173)", ExpectedResult = false},
            ///Collinearity is not required to test in package, since it is checked in validation already.
            ///new (math.double2(0, 0), math.double2(2, 0), math.double2(1, 0), math.double2(3, 0))
            /// { TestName = "Case 4 (collinear with intersection)", ExpectedResult = true, },
        }.SelectMany(i =>
        {
            var args = i.Arguments;
            return new TestCaseData[]
            {
                new(args[0], args[1], args[2], args[3]) { TestName = i.TestName + ": abcd", ExpectedResult = i.ExpectedResult},
                new(args[0], args[1], args[3], args[2]) { TestName = i.TestName + ": abdc", ExpectedResult = i.ExpectedResult},
                new(args[1], args[0], args[2], args[3]) { TestName = i.TestName + ": bacd", ExpectedResult = i.ExpectedResult},
                new(args[1], args[0], args[3], args[2]) { TestName = i.TestName + ": badc", ExpectedResult = i.ExpectedResult},
                new(args[2], args[3], args[0], args[1]) { TestName = i.TestName + ": cdab", ExpectedResult = i.ExpectedResult},
                new(args[3], args[2], args[0], args[1]) { TestName = i.TestName + ": dcab", ExpectedResult = i.ExpectedResult},
                new(args[2], args[3], args[1], args[0]) { TestName = i.TestName + ": cdba", ExpectedResult = i.ExpectedResult},
                new(args[3], args[2], args[1], args[0]) { TestName = i.TestName + ": dcba", ExpectedResult = i.ExpectedResult},
            };
        }
        ).ToArray();

        [Test, TestCaseSource(nameof(edegeVsEdgeTestData))]
        public bool EdgeEdgeIntersectionTest(double2 a0, double2 a1, double2 b0, double2 b1) => UnsafeTriangulator<double, double2, double, AffineTransform64, DoubleUtils>.EdgeEdgeIntersection(a0, a1, b0, b1);

        private static readonly TestCaseData[] isConvexQuadrilateralTestData = new TestCaseData[]
        {
            new (math.double2(0, 0), math.double2(4, 0), math.double2(4, 1), math.double2(0, 1))
            { TestName = "Case 1 (convex, rectangle", ExpectedResult = true },
            new (math.double2(0, 0), math.double2(8, 0), math.double2(1, 1), math.double2(0, 8))
            { TestName = "Case 2 (concave, dart", ExpectedResult = false },
            new (math.double2(0, 0), math.double2(2, 1), math.double2(2, 0), math.double2(0, 2))
            { TestName = "Case 3 (reflect", ExpectedResult = false },
            new (math.double2(0, 0), math.double2(1, 0), math.double2(2, 0), math.double2(2, 2))
            { TestName = "Case 4 (colinear 3-point", ExpectedResult = false },
            new (math.double2(0, 0), math.double2(1, 0), math.double2(2, 0), math.double2(3, 0))
            { TestName = "Case 5 (colinear 4-point", ExpectedResult = false },
        }.SelectMany(i =>
        {
            var args = i.Arguments;
            var (a, b, c, d) = (args[0], args[1], args[2], args[3]);
            return new TestCaseData[]
            {
                new(a, b, c, d) { TestName = i.TestName + ", perm 0)", ExpectedResult = i.ExpectedResult },
                new(d, a, b, c) { TestName = i.TestName + ", perm 1)", ExpectedResult = i.ExpectedResult },
                new(c, d, a, b) { TestName = i.TestName + ", perm 2)", ExpectedResult = i.ExpectedResult },
                new(b, c, d, a) { TestName = i.TestName + ", perm 3)", ExpectedResult = i.ExpectedResult },
                new(d, c, b, a) { TestName = i.TestName + ", perm 4)", ExpectedResult = i.ExpectedResult },
                new(c, b, a, d) { TestName = i.TestName + ", perm 5)", ExpectedResult = i.ExpectedResult },
                new(b, a, d, c) { TestName = i.TestName + ", perm 6)", ExpectedResult = i.ExpectedResult },
                new(a, d, c, b) { TestName = i.TestName + ", perm 7)", ExpectedResult = i.ExpectedResult },
            };
        }).ToArray();

        [Test, TestCaseSource(nameof(isConvexQuadrilateralTestData))]
        public bool IsConvexQuadrilateralTest(double2 a, double2 b, double2 c, double2 d) => UnsafeTriangulator<double, double2, double, AffineTransform64, DoubleUtils>.IsConvexQuadrilateral(a, b, c, d);

        private static readonly TestCaseData[] pointLineSegmentIntersectionTestData = new TestCaseData[]
        {
            new(math.double2(0, 0), math.double2(1, 0), math.double2(2, 0)) { TestName = "Case 1 (no intersection and collinear", ExpectedResult = false },
            new(math.double2(1.15, 0), math.double2(1, 0), math.double2(2, 0)) { TestName = "Case 2 (with intersection", ExpectedResult = true },
            new(math.double2(1.5, 1.5), math.double2(1, 0), math.double2(2, 0)) { TestName = "Case 3 (no intersection and non-collinear", ExpectedResult = false },
            new(
                // NOTE: this lerp policy b + t(a-b) introduce smaller error than ta + (1-t)b, which can cause this check to fail!
                math.double2(7.21, -5.347) + 0.3673 * (math.double2(-2.542, 6.32346) - math.double2(7.21, -5.347)),
                math.double2(-2.542, 6.32346),
                math.double2(7.21, -5.347)) { TestName = "Case 4 (generic intersection", ExpectedResult = true },
        }.SelectMany(i =>
        {
            var args = i.Arguments;
            var (a, b, c) = (args[0], args[1], args[2]);
            return new TestCaseData[]
            {
                new(a, b, c) { TestName = i.TestName + ", ab)", ExpectedResult = i.ExpectedResult },
                new(a, c, b) { TestName = i.TestName + ", ba)", ExpectedResult = i.ExpectedResult },
            };
        }).ToArray();

        [Test, TestCaseSource(nameof(pointLineSegmentIntersectionTestData))]
        public bool PointLineSegmentIntersectionTest(double2 a, double2 b0, double2 b1) => UnsafeTriangulator<double, double2, double, AffineTransform64, DoubleUtils>.PointLineSegmentIntersection(a, b0, b1);

        private static readonly TestCaseData[] pointInsideTriangleTestData = new TestCaseData[]
        {
            new(math.double2(-1, 0), math.double2(0, 0), math.double2(2, 0), math.double2(0, 2)) { TestName = "Test case 1", ExpectedResult = false },
            new(math.double2(1, 1), math.double2(0, 0), math.double2(2, 0), math.double2(0, 2)) { TestName = "Test case 2", ExpectedResult = true },
        }.SelectMany(i =>
        {
            var args = i.Arguments;
            var (p, a, b, c) = (args[0], args[1], args[2], args[3]);
            return new TestCaseData[]
            {
                new(p, a, b, c) { TestName = i.TestName + ": p-abc", ExpectedResult = i.ExpectedResult },
                new(p, c, a, b) { TestName = i.TestName + ": p-cab", ExpectedResult = i.ExpectedResult },
                new(p, b, c, a) { TestName = i.TestName + ": p-bca", ExpectedResult = i.ExpectedResult },
                new(p, c, b, a) { TestName = i.TestName + ": p-cba", ExpectedResult = i.ExpectedResult },
                new(p, b, a, c) { TestName = i.TestName + ": p-bac", ExpectedResult = i.ExpectedResult },
                new(p, a, c, b) { TestName = i.TestName + ": p-acb", ExpectedResult = i.ExpectedResult },
            };
        }).ToArray();

        [Test, TestCaseSource(nameof(pointInsideTriangleTestData))]
        public bool PointInsideTriangleTest(double2 p, double2 a, double2 b, double2 c) => UnsafeTriangulator<double, double2, double, AffineTransform64, DoubleUtils>.PointInsideTriangle(p, a, b, c);
    }
}