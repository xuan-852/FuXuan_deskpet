using System;
using System.Collections;
using System.IO;
using System.Linq;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Physics;
using UnityEngine;

/// <summary>
/// 独立模型探测窗口的运行时控制器。
/// 它不引用 DesktopPet、Live2DRenderer 或任何桌宠行为组件；每次只控制一个 Cubism 参数。
/// </summary>
public sealed class ProbeWindowController : MonoBehaviour
{
    [SerializeField] private GameObject modelRoot;
    private const int FrameSize = 768;

    [Serializable]
    private sealed class BatchResult
    {
        public int repeats;
        public string writerMode;
        public ParameterResult[] parameters;
    }

    [Serializable]
    private sealed class ParameterResult
    {
        public string parameterId;
        public float baseline;
        public float minimum;
        public float maximum;
        public bool resetStable;
        public float maxResetMeanDifference;
        public float minMeanDifference;
        public float midMeanDifference;
        public float maxMeanDifference;
        public string[] frames;
    }

    private sealed class CapturedFrame { public byte[] png; public Color32[] pixels; }

    private void Start()
    {
        StartCoroutine(RunWithFailureReport());
    }

    private IEnumerator RunWithFailureReport()
    {
        IEnumerator routine = Run();
        while (true)
        {
            object current;
            try
            {
                if (!routine.MoveNext()) break;
                current = routine.Current;
            }
            catch (Exception exception)
            {
                string root = Environment.GetEnvironmentVariable("FU_XUAN_DATA") ?? Application.temporaryCachePath;
                File.WriteAllText(Path.Combine(root, "capability-report-probe-window.failure.txt"), exception.ToString());
                Debug.LogException(exception);
                Application.Quit(1);
                yield break;
            }
            yield return current;
        }
    }

    private IEnumerator Run()
    {
        string root = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, ".test_mode")))
            throw new InvalidOperationException("Probe window requires isolated FU_XUAN_DATA and .test_mode.");
        if (modelRoot == null) throw new InvalidOperationException("Probe model root is missing.");

        bool preservePhysics = string.Equals(Environment.GetEnvironmentVariable("FU_XUAN_PROBE_WRITER_MODE"),
            "physics", StringComparison.OrdinalIgnoreCase);
        FreezeModelWriters(modelRoot, preservePhysics);
        CubismModel model = modelRoot.GetComponentInChildren<CubismModel>(true);
        if (model == null) throw new InvalidOperationException("Probe prefab has no CubismModel.");
        Camera camera = CreateCamera();
        yield return null;
        FrameModel(camera, modelRoot);
        yield return null;

        string dir = Path.Combine(root, "probe_window");
        Directory.CreateDirectory(dir);
        string requested = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_PARAMETER");
        string scope = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_SCOPE");
        string[] ids = !string.IsNullOrEmpty(requested) ? new[] { requested }
            : string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
                ? model.Parameters.Where(item => item != null).Select(item => item.Id.ToString()).Distinct().ToArray()
                : SkeletonCandidates;
        var results = new ParameterResult[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            CubismParameter parameter = FindParameter(model, ids[i]);
            if (parameter == null) throw new InvalidOperationException("Probe parameter not found: " + ids[i]);
            yield return CaptureParameter(model, camera, parameter, dir, preservePhysics, value => results[i] = value);
        }

        File.WriteAllText(Path.Combine(root, "capability-report-probe-window.json"),
            JsonUtility.ToJson(new BatchResult { repeats = 3, writerMode = preservePhysics ? "physics" : "frozen", parameters = results }, true));
        Debug.Log("[Live2DProbe] standalone window completed: " + ids.Length + " parameters");
        Application.Quit(0);
    }

    private static readonly string[] SkeletonCandidates = {
        "ParamAngleX", "ParamAngleY", "ParamAngleZ", "ParamBodyAngleX", "ParamBodyAngleY", "ParamBodyAngleZ",
        "ParamBodyAngleX2", "ParamBodyAngleY2", "ParamBodyAngleZ2", "Param31", "Param32", "Param33", "Param34", "Param36", "Param37"
    };

    private IEnumerator CaptureParameter(CubismModel model, Camera camera, CubismParameter parameter, string dir, bool preservePhysics, Action<ParameterResult> done)
    {
        const int repeats = 3;
        float baseline = parameter.Value;
        float mid = (parameter.MinimumValue + parameter.MaximumValue) * 0.5f;
        float[] values = { baseline, parameter.MinimumValue, mid, parameter.MaximumValue, baseline };
        string[] labels = { "baseline", "min", "mid", "max", "reset" };
        var result = new ParameterResult { parameterId = parameter.Id, baseline = baseline, minimum = parameter.MinimumValue,
            maximum = parameter.MaximumValue, frames = new string[repeats * labels.Length] };
        float resetMax = 0f, minSum = 0f, midSum = 0f, maxSum = 0f;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            CapturedFrame baseFrame = null;
            for (int point = 0; point < values.Length; point++)
            {
                parameter.Value = values[point];
                model.ForceUpdateNow();
                int settleFrames = preservePhysics ? 8 : 1;
                for (int settle = 0; settle < settleFrames; settle++) yield return null;
                if (preservePhysics) StabilizePhysics(model.gameObject);
                model.ForceUpdateNow();
                CapturedFrame frame = Capture(camera);
                string path = Path.Combine(dir, parameter.Id + "_r" + (repeat + 1) + "_" + labels[point] + ".png");
                File.WriteAllBytes(path, frame.png);
                result.frames[repeat * labels.Length + point] = path;
                if (point == 0) baseFrame = frame;
                else
                {
                    float diff = MeanPixelDifference(baseFrame.pixels, frame.pixels);
                    if (point == 1) minSum += diff;
                    else if (point == 2) midSum += diff;
                    else if (point == 3) maxSum += diff;
                    else resetMax = Mathf.Max(resetMax, diff);
                }
            }
        }
        parameter.Value = baseline;
        result.minMeanDifference = minSum / repeats;
        result.midMeanDifference = midSum / repeats;
        result.maxMeanDifference = maxSum / repeats;
        result.maxResetMeanDifference = resetMax;
        result.resetStable = resetMax <= 0.1f;
        done(result);
    }

    private static CubismParameter FindParameter(CubismModel model, string id)
    {
        foreach (CubismParameter item in model.Parameters)
            if (item != null && item.Id == id) return item;
        return null;
    }

    private static void StabilizePhysics(GameObject root)
    {
        foreach (CubismPhysicsController controller in root.GetComponentsInChildren<CubismPhysicsController>(true))
            if (controller != null && controller.enabled) controller.Stabilization();
    }

    private static void FreezeModelWriters(GameObject root, bool preservePhysics)
    {
        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            string name = component.GetType().Name;
            bool requiredForRender = name == "CubismModel" || name == "CubismUpdateController" || name == "CubismRenderController" || name == "CubismDrawVertices"
                || name == "CubismRenderer" || name == "CubismRendererArray" || name == "CubismParameters"
                || name.StartsWith("CubismRender", StringComparison.Ordinal)
                || (preservePhysics && name == "CubismPhysicsController");
            if (!requiredForRender) component.enabled = false;
        }
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
    }

    private static Camera CreateCamera()
    {
        GameObject go = new GameObject("ProbeCamera");
        Camera camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        return camera;
    }

    private static void FrameModel(Camera camera, GameObject root)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!found) throw new InvalidOperationException("Probe model has no enabled renderers.");
        camera.transform.position = bounds.center + Vector3.back * 10f;
        camera.transform.LookAt(bounds.center);
        camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f;
        camera.aspect = 1f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 30f;
    }

    private static CapturedFrame Capture(Camera camera)
    {
        RenderTexture rt = RenderTexture.GetTemporary(FrameSize, FrameSize, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D texture = new Texture2D(FrameSize, FrameSize, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0);
        texture.Apply();
        var result = new CapturedFrame { png = texture.EncodeToPNG(), pixels = texture.GetPixels32() };
        Destroy(texture);
        camera.targetTexture = null;
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private static float MeanPixelDifference(Color32[] a, Color32[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return float.PositiveInfinity;
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
        return (float)(sum / (a.Length * 3.0));
    }
}
