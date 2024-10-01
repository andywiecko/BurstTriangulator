using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class RuntimeDemo : MonoBehaviour
    {
        [SerializeField, Min(3)]
        private int pointsCount = 1024;

        [SerializeField, Min(1e-9f)]
        private float syncTime = 1f / 30;

        private Mesh mesh;
        private Triangulator triangulator;
        private NativeArray<double2> positions;
        private NativeArray<Vector3> vertices;
        private NativeReference<Unity.Mathematics.Random> random;

        private void Start()
        {
            positions = new(pointsCount, Allocator.Persistent);
            vertices = new(pointsCount, Allocator.Persistent);
            random = new(new(seed: 42), Allocator.Persistent);
            triangulator = new(Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = { ValidateInput = false }
            };

            GetComponent<MeshFilter>().mesh = mesh = new Mesh();
            GetComponent<MeshRenderer>().material = new(Shader.Find("Hidden/BurstTriangulator/RuntimeDemo"));
        }

        private void OnDestroy()
        {
            dependencies.Complete();

            positions.Dispose();
            vertices.Dispose();
            triangulator.Dispose();
            random.Dispose();
        }

        private JobHandle dependencies;
        private float t;

        private void Update()
        {
            if (t <= syncTime)
            {
                t += Time.deltaTime;
                return;
            }

            dependencies.Complete();
            mesh.SetVertices(vertices);
            mesh.SetIndices(triangulator.Output.Triangles.AsArray(), MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();

            dependencies = default;
            dependencies = new GeneratePositionsJob(this).Schedule(dependencies);
            dependencies = triangulator.Schedule(dependencies);

            t = 0;
        }

        [BurstCompile]
        private struct GeneratePositionsJob : IJob
        {
            private NativeArray<double2> positions;
            private NativeArray<Vector3> vertices;
            private NativeReference<Unity.Mathematics.Random> random;

            public GeneratePositionsJob(RuntimeDemo demo)
            {
                positions = demo.positions;
                vertices = demo.vertices;
                random = demo.random;
            }

            public void Execute()
            {
                var rnd = random.Value;
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i] = rnd.NextDouble2();
                    vertices[i] = math.float3((float2)p, 0);
                }
                random.Value = rnd;
            }
        }
    }
}