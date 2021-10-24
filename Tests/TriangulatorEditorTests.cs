using NUnit.Framework;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class TriangulatorTestExtensions
    {
        public static (int, int, int)[] ToTrisTuple(this NativeArray<int>.ReadOnly triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .ToArray();
    }

    public class TriangulatorEditorTests
    {
        private (int, int, int)[] Triangles => triangulator.Triangles.ToTrisTuple();
        private float2[] Positions => triangulator.Positions.ToArray();

        private Triangulator triangulator;

        [SetUp]
        public void SetUp()
        {
            triangulator = new Triangulator(capacity: 1024, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            triangulator?.Dispose();
        }

        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            triangulator.Settings.RefineMesh = false;

            triangulator
                .Schedule(positions.AsReadOnly(), default)
                .Complete();

            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1

            Assert.That(Triangles, Is.EqualTo(new[] { (1, 0, 2), (2, 0, 3) }));
        }

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.RefineMesh = true;
            settings.MinimumAngle = math.radians(30);
            settings.MinimumArea = 0.3f;
            settings.MaximumArea = 0.3f;

            triangulator
                .Schedule(positions.AsReadOnly(), default)
                .Complete();

            ///  3 ------- 2
            ///  |` .   . `|
            ///  |    4    |
            ///  |. `   ` .|
            ///  0 ------- 1

            var expectedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1),
                math.float2(0.5f, 0.5f)
            };
            Assert.That(Positions, Is.EqualTo(expectedPositions));

            var expectedTriangles = new[]
            {
                (2, 1, 4), (1, 0, 4), (4, 0, 3), (3, 2, 4)
            };
            Assert.That(Triangles, Is.EqualTo(expectedTriangles));
        }
    }
}
