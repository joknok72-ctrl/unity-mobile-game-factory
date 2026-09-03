// Template scene content — REPLACE with the real game.
// Shows a spinning cube + a label so the pipeline can be verified end-to-end on a phone.
using UnityEngine;

public class TemplateBootstrap : MonoBehaviour
{
    Transform _cube;
    GUIStyle _style;

    void Start()
    {
        Application.targetFrameRate = 60;
        var cam = Camera.main;
        if (cam == null) { var go = new GameObject("Main Camera") { tag = "MainCamera" }; cam = go.AddComponent<Camera>(); }
        cam.transform.position = new Vector3(0, 1.5f, -4f); cam.transform.LookAt(Vector3.zero);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        // Materials must be ASSETS (here in Resources) — a runtime Shader.Find("Universal Render Pipeline/Lit") is stripped
        // from the build when no asset references the shader and everything renders magenta.
        var mat = Resources.Load<Material>("TemplateMaterial");
        if (mat != null) _cube.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void Update() { if (_cube != null) _cube.Rotate(30f * Time.deltaTime, 45f * Time.deltaTime, 0f); }

    void OnGUI()
    {
        if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = Screen.height / 30, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, Screen.height * 0.1f), $"{Application.productName} v{Application.version}\nUnity Mobile Game Factory — pipeline OK", _style);
    }
}
