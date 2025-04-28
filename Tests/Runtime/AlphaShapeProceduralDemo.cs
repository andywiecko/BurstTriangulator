using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class AlphaShapeProceduralDemo : MonoBehaviour
    {
        [Min(3)] public int Count = 128;
        [Min(1)] public int Seed = 42;

        [field: SerializeField]
        public TriangulationSettings Settings { get; private set; } = new()
        {
            UseAlphaShapeFilter = true,
            AlphaShapeSettings =
            {
                Alpha = 500,
                PreventWindmills = true,
                ProtectPoints = true,
            },
        };

        [SerializeField] private Mesh mesh;

        private MeshFilter meshFilter;

        public void Triangulate()
        {
            var random = new Unity.Mathematics.Random((uint)Seed);
            using var positions = new NativeArray<double2>(Count, Allocator.Persistent);
            void GeneratePositions(Span<double2> positions)
            {
                for (int i = 0; i < Count; i++) positions[i] = random.NextDouble2();
            }
            GeneratePositions(positions);

            using var triangulator = new Triangulator(Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = Settings,
            };

            triangulator.Run();

            mesh = new()
            {
                vertices = triangulator.Output.Positions.AsReadOnly().Select(i => (Vector3)math.float3((float2)i, 0)).ToArray(),
                triangles = triangulator.Output.Triangles.AsReadOnly().ToArray()
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            meshFilter = meshFilter == null ? GetComponent<MeshFilter>() : meshFilter;
            meshFilter.mesh = mesh;
        }

        private void Reset()
        {
            GetComponent<MeshRenderer>().material = new(Shader.Find("Hidden/BurstTriangulator/AlphaShapeDemo"));
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += Once;
            void Once()
            {
                Triangulate();
                UnityEditor.EditorApplication.update -= Once;
            }
#endif
        }
    }
}