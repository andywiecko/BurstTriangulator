using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    [Explicit, Category("Quality Test")]
    public class TriangulatorQualityTests
    {
        private static IEnumerable<TestCaseData> BuildLakeTestData()
        {
            foreach (var area in new[] { 10f, 0.001f, 0.0001f })
            {
                foreach (var angle in new[] { 0f, 5f, 10f, 15f, 20f, 33f })
                {
                    yield return new TestCaseData(area, math.radians(angle))
                    {
                        TestName = $"area = {area}; angle = {angle}"
                    };
                }
            }
        }

        private static readonly TestCaseData[] lakeTestData = BuildLakeTestData().ToArray();

        [Test, TestCaseSource(nameof(lakeTestData))]
        public void LakeTest(float area, float angle)
        {
            using var positions = new NativeArray<float2>(LakeSuperior.Points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holeSeeds = new NativeArray<float2>(LakeSuperior.Holes, Allocator.Persistent);
            using var triangulator = new Triangulator<float2>(1024 * 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = false,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds =
                    {
                        Area = area,
                        Angle = angle,
                    }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holeSeeds,
                }
            };

            triangulator.Run();

            Debug.Log($"triangles: {triangulator.Output.Triangles.Length / 3}");

            triangulator.Draw(color: new Color(0.1f, 0.1f, 0.1f, 1f));
            var p = triangulator.Output.Positions;
            var constraints = triangulator.Input.ConstraintEdges;
            for (int i = 0; i < constraints.Length / 2; i++)
            {
                Debug.DrawLine(math.float3(p[constraints[2 * i]], 0), math.float3(p[constraints[2 * i + 1]], 0), Color.white, 5f);
            }

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            triangulator.LaTeXify(
                path: $"lake-superior-area{area}-angle{math.degrees(angle)}.tex",
                appendTikz: @$"
\node[right] at (0.1, -0.30) {{$C_\theta= {math.round(math.degrees(angle))}^\circ$}};
\node[right] at (0.1, -0.35) {{$C_\triangle=10^{{{math.round(math.log10(area))}}}$}};",
                loops: LakeSuperior.Loops
            );

            return;
        }
    }
}