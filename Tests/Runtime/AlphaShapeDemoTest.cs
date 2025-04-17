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
        public IEnumerator DemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/AlphaShapeDemo.unity", new(LoadSceneMode.Single));
            var demo = Object.FindObjectOfType<AlphaShapeDemo>();

            for (int i = 0; i < 100; i++) yield return next();

            IEnumerator next()
            {
                demo.Seed++;
                demo.Triangulate();
                yield return new WaitForSeconds(0.01f);
            }
        }
    }
}