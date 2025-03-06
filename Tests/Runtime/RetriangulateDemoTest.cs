using System;
using System.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    public class RetriangulateDemoTest
    {
        [UnityTest]
        public IEnumerator DemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/RetriangulateDemoTest.unity", new(LoadSceneMode.Single));
            var demo = GameObject.FindObjectOfType<RetriangulateDemo>();

            foreach (RetriangulateDemo.Cases c in Enum.GetValues(typeof(RetriangulateDemo.Cases)))
            {
                demo.Case = c;
                demo.Retriangulate();
                yield return new WaitForSeconds(0.334f);
            }
        }
    }
}
