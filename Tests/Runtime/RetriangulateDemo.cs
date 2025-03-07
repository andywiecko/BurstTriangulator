using UnityEngine;
using static andywiecko.BurstTriangulator.Editor.Tests.GithubIssuesData;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    public class RetriangulateDemo : MonoBehaviour
    {
        public enum Cases { Case2, Case3, Case4, Case5, Case10, };
        public Cases Case = Cases.Case3;
        public UVMap uvMap = UVMap.Barycentric;
        public bool InsertTriangleMidPoints, InsertEdgeMidPoints;

        [SerializeField]
        private TriangulationSettings settings = new()
        {
            AutoHolesAndBoundary = true,
        };

        public void Retriangulate()
        {
            var (inputPositions, inputTriangles) = Case switch
            {
                Cases.Case2 => Issue134_Case2,
                Cases.Case3 => Issue134_Case3,
                Cases.Case4 => Issue134_Case4,
                Cases.Case5 => Issue134_Case5,
                Cases.Case10 => (Issue134_Case10.positions, Issue134_Case10.triangles),
                _ => throw new()
            };

            var inputUVs = Case switch
            {
                Cases.Case10 => Issue134_Case10.uvs,
                _ => new Vector2[inputPositions.Length],
            };

            var mesh = Application.isPlaying ? GetComponent<MeshFilter>().mesh : GetComponent<MeshFilter>().sharedMesh;

            mesh.Clear();
            mesh.vertices = inputPositions;
            mesh.triangles = inputTriangles;
            mesh.uv = inputUVs;

            mesh.Retriangulate(
                settings: settings,
                axisInput: Axis.XZ,
                uvMap: uvMap,
                generateInitialUVPlanarMap: Case != Cases.Case10,
                insertTriangleMidPoints: InsertTriangleMidPoints,
                insertEdgeMidPoints: InsertEdgeMidPoints
            );
        }

        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += delay;

            void delay()
            {
                if (this == null)
                {
                    return;
                }

                var renderer = GetComponent<MeshRenderer>();
                renderer.sharedMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;

                var filter = GetComponent<MeshFilter>();

                if (Application.isPlaying)
                {
                    filter.mesh = filter.mesh == null ? new() : filter.mesh;
                }
                else
                {
                    filter.sharedMesh = filter.sharedMesh == null ? new() : filter.sharedMesh;
                }

                if (!Application.isPlaying)
                {
                    Retriangulate();
                }

                UnityEditor.EditorApplication.delayCall -= delay;
            }
        }
    }
}