using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using NUnit.Framework;
using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class InternalUtilsTests
    {
        private static readonly TestCaseData[] alphaTestData =
        {
            new(1d, 2d) { ExpectedResult = 0.50, TestName = "Test case 1 (D = 1, d = 2)"},
            new(1d, 4d) { ExpectedResult = 0.50, TestName = "Test case 2 (D = 1, d = 4)"},
            new(1d, 8d) { ExpectedResult = 0.50, TestName = "Test case 3 (D = 1, d = 8)"},
            new(1d, 1.5d) { ExpectedResult = 2.0 / 3, TestName = "Test case 4 (D = 1, d = 1.5)"},
            new(2d, 1d) { ExpectedResult = 0.5, TestName = "Test case 5 (D = 2, d = 1)"},
            new(1.5, 1d) { ExpectedResult = 0.375, TestName = "Test case 6 (D = 1.5, d = 1)"},
            new(1d, 0d) { ExpectedResult = float.NaN, TestName = "Test case 7 (D = 1, d = 0)"},
        };

        [Test, TestCaseSource(nameof(alphaTestData))]
        public double AlphaTest(double D, double d) => default(UtilsDouble).alpha(D, d * d);

        private static readonly TestCaseData[] angleTestData = new TestCaseData[]
        {
            new(math.double2(0, 0), math.double2(1, 0), math.double2(0, 1), math.PI_DBL / 4 + 1e-9) { ExpectedResult = true, TestName = "Test case 1a - canonical vectors" },
            new(math.double2(0, 0), math.double2(1, 0), math.double2(0, 1), math.PI_DBL / 4 - 1e-9) { ExpectedResult = false, TestName = "Test case 1b - canonical vectors" },
            new(math.double2(0, 0), math.double2(0, 1), math.double2(1, 0), math.PI_DBL / 4 + 1e-9) { ExpectedResult = true, TestName = "Test case 2a - canonical vectors (swapped)" },
            new(math.double2(0, 0), math.double2(0, 1), math.double2(1, 0), math.PI_DBL / 4 - 1e-9) { ExpectedResult = false, TestName = "Test case 2b - canonical vectors (swapped)" },
            new(math.double2(0, 0), math.double2(2, 1), math.double2(-1, 2), math.PI_DBL / 4 + 1e-9) { ExpectedResult = true, TestName = "Test case 3a" },
            new(math.double2(0, 0), math.double2(2, 1), math.double2(-1, 2), math.PI_DBL / 4 - 1e-9) { ExpectedResult = false, TestName = "Test case 3b" },
            new(math.double2(0, 0), math.double2(2, 0), math.double2(-1, 2), 0.51914611424652 + 1e-9) { ExpectedResult = true, TestName = "Test case 4a - triangle with angle larger than pi/2" },
            new(math.double2(0, 0), math.double2(2, 0), math.double2(-1, 2), 0.51914611424652 - 1e-9) { ExpectedResult = false, TestName = "Test case 4b - triangle with angle larger than pi/2" },
            new(math.double2(0, 0), math.double2(1, 0), math.double2(-1, 0), 0 + 1e-4){ ExpectedResult = true, TestName = "Test case 5 - opposite dir" },
            new(math.double2(0, 0), math.double2(1, 0), math.double2(1, 0), 0 + 1e-4){ ExpectedResult = true, TestName = "Test case 6 - duplicated vertex" },
            new(math.double2(0, 0), math.double2(1, 0), math.double2(math.cos(0.5698235), math.sin(0.5698235)), 0.5698235 + 1e-9){ ExpectedResult = true, TestName = "Test case 7a - arbitrary angle" },
            new(math.double2(0, 0), math.double2(1, 0), math.double2(math.cos(0.5698235), math.sin(0.5698235)), 0.5698235 - 1e-9){ ExpectedResult = false, TestName = "Test case 7b - arbitrary angle" },
        };

        [Test, TestCaseSource(nameof(angleTestData))]
        public bool AngleTest(double2 a, double2 b, double2 c, double minAngle) => UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>.AngleIsTooSmall(a, b, c, minAngle);

        private static readonly TestCaseData[] area2testData = new TestCaseData[]
        {
            new(math.double2(0, 0), math.double2(1, 0), math.double2(0, 1), 1) { TestName = "Test case 1" },
            new(math.double2(0, 0), math.double2(2, 0), math.double2(0, 1), 2) { TestName = "Test case 2" },
            new(math.double2(0, 0), math.double2(6, 0), math.double2(0, 7), 42) { TestName = "Test case 3" },
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
            var result = UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>.Area2(a, b, c);
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
        public bool EdgeEdgeIntersectionTest(double2 a0, double2 a1, double2 b0, double2 b1) => UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>.EdgeEdgeIntersection(a0, a1, b0, b1);

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
        public bool IsConvexQuadrilateralTest(double2 a, double2 b, double2 c, double2 d) => UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>.IsConvexQuadrilateral(a, b, c, d);

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
        public bool PointLineSegmentIntersectionTest(double2 a, double2 b0, double2 b1) => UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>.PointLineSegmentIntersection(a, b0, b1);

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
        public bool PointInsideTriangleTest(double2 p, double2 a, double2 b, double2 c) => default(UtilsDouble).PointInsideTriangle(p, a, b, c);
    }

    public class NativeListQueueTests
    {
        [Test]
        public void IsCreatedTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            Assert.That(queue.IsCreated, Is.True);
        }

        [Test]
        public void CountTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);

            var count0 = queue.Count;
            queue.Enqueue(default);
            queue.Enqueue(default);
            queue.Enqueue(default);
            var count3 = queue.Count;
            queue.Dequeue();
            queue.Dequeue();
            var count1 = queue.Count;

            Assert.That(count0, Is.EqualTo(0));
            Assert.That(count3, Is.EqualTo(3));
            Assert.That(count1, Is.EqualTo(1));
        }

        [Test]
        public void AsReadOnlySpanTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(0);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);
            queue.Dequeue();

            var span = queue.AsReadOnlySpan();
            Assert.That(span.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void AsSpanTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(0);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);
            queue.Dequeue();

            var span = queue.AsSpan();
            Assert.That(span.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ClearTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(0);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            queue.Clear();

            Assert.That(queue.Count, Is.EqualTo(0));
            Assert.That(queue.AsReadOnlySpan().ToArray(), Is.Empty);
        }

        [Test]
        public void DisposeTest()
        {
            var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Dispose();
            Assert.That(queue.IsCreated, Is.False);
        }

        [Test]
        public void EnqueueTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(42);
            Assert.That(queue.AsReadOnlySpan()[0], Is.EqualTo(42));
        }

        [Test]
        public void DequeueTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(1);
            queue.Enqueue(2);
            var el = queue.Dequeue();
            Assert.That(el, Is.EqualTo(1));
        }

        [Test]
        public void IsEmptyTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(1);
            queue.Dequeue();
            Assert.That(queue.IsEmpty(), Is.True);
        }

        [Test]
        public void TryDequeueTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            queue.Enqueue(0);
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            using var tmp = new NativeList<int>(Allocator.Persistent);
            while (queue.TryDequeue(out var el))
            {
                tmp.Add(el);
            }

            Assert.That(tmp.AsReadOnly(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void ThrowDequeueTest()
        {
            using var queue = new NativeQueueList<int>(Allocator.Persistent);
            Assert.Throws<IndexOutOfRangeException>(() => queue.Dequeue());
        }
    }
}