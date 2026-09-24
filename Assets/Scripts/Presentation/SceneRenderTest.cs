using System;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Scene-owned, input-independent render proof. The GameObject carrying
    /// this component is serialized in Main.unity, so a bright cube appears
    /// even if gameplay world construction fails.
    /// </summary>
    [DefaultExecutionOrder(-999)]
    public class SceneRenderTest : MonoBehaviour
    {
        private void Awake()
        {
            Debug.Log("[Rat Habitat] SceneRenderTest.Awake started.");
            try
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Guaranteed Bright Render Test Cube";
                cube.transform.SetParent(transform, false);
                cube.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                cube.transform.localScale = new Vector3(1.15f, 1.15f, 1.15f);
                MaterialFactory.Apply(cube.GetComponent<Renderer>(), new Color(1f, 0.08f, 0.92f), true);
                // This temporary render proof is not a gameplay target. Its
                // default primitive collider could otherwise sit in front of
                // a rat/object ray and make an empty diagnostic misleading.
                var collider = cube.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Debug.Log("[Rat Habitat] SceneRenderTest created a bright unlit cube.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
