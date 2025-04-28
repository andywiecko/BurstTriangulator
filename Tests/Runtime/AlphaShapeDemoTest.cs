using System.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    public class AlphaShapeDemoTest
    {
        [UnityTest]
        public IEnumerator ProceduralDemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/AlphaShapeDemo.unity", new(LoadSceneMode.Single));
            var demo = Object.FindObjectOfType<AlphaShapeProceduralDemo>(includeInactive: true);
            demo.gameObject.SetActive(true);

            for (int i = 0; i < 100; i++) yield return next();

            IEnumerator next()
            {
                demo.Seed++;
                demo.Triangulate();
                yield return new WaitForSeconds(0.01f);
            }
        }

        [UnityTest]
        public IEnumerator ReconstructionDemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/AlphaShapeDemo.unity", new(LoadSceneMode.Single));
            var demo = Object.FindObjectOfType<AlphaShapeReconstructionDemo>(includeInactive: true);
            demo.gameObject.SetActive(true);

            yield return new WaitForSeconds(1f);
            demo.GetComponent<MeshRenderer>().enabled = true;

            var N = 50;
            for (int i = 0; i < N; i++) yield return next((i + 1f) / N);

            yield return new WaitForSeconds(.5f);

            IEnumerator next(float i)
            {
                demo.Settings.AlphaShapeSettings.Alpha = 20 * i;
                demo.Triangulate();
                yield return new WaitForSeconds(0.05f);
            }
        }
    }
}