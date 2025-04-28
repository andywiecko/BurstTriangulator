using System.Globalization;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class AlphaShapeReconstructionDemo : MonoBehaviour
    {
        [field: SerializeField]
        public TriangulationSettings Settings { get; private set; } = new();

        [SerializeField] private Mesh mesh;

        private MeshFilter meshFilter;

        public void Triangulate()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                new(0, 0),
                new(1, 0),
                new(1.75, 2),
                new(4.25, 2),
                new(5, 0),
                new(6, 0),
                new(3.5, 6.5),
                new(2.5, 6.5),

                new(2, 3),
                new(4, 3),
                new(3, 5.5),

            }, Allocator.Persistent);

            using var constraints = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 0,
                8, 9, 9, 10, 10, 8
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints },
                Settings = { RefineMesh = true, RefinementThresholds = { Area = 0.05f }, AutoHolesAndBoundary = true },
            };

            triangulator.Run();

            using var positions2 = new NativeArray<double2>(triangulator.Output.Positions.AsReadOnly().ToArray(), Allocator.Persistent);

            triangulator.Input.Positions = positions2;
            triangulator.Input.ConstraintEdges = default;
            triangulator.Settings = Settings;

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

        private void OnDrawGizmos()
        {
            if (mesh == null) return;

            foreach (var p in mesh.vertices)
            {
                Gizmos.color = Color.black;
                Gizmos.DrawSphere(p, 0.05f);
            }

#if UNITY_EDITOR
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            UnityEditor.Handles.Label(
                position: new(1.2f, -0.5f, 0),
                text: $"alpha = {Settings.AlphaShapeSettings.Alpha:F1}",
                style: new()
                {
                    normal = { textColor = Color.black },
                    fontSize = 30,
                    alignment = TextAnchor.MiddleLeft,
                });
#endif
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