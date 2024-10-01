using System.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    public class RuntimeDemoTest
    {
        [UnityTest]
        public IEnumerator DemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/RuntimeDemoTest.unity", new(LoadSceneMode.Single));
            yield return new WaitForSeconds(3f);
        }
    }
}