using System;
using System.IO;
using Live2D.Cubism.Core;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only batch bootstrap for the generic Live2D capability probe.</summary>
public static class UnityCubismProbeBatch
{
    private const string PrefabPath = "Assets/Live2D/Models/Fuxuan/符玄.prefab";

    [Serializable]
    private class Report
    {
        public string modelHash;
        public string mapHash;
        public int parameterCount;
        public string[] skeletonCandidates;
        public ParameterResult[] results;
    }

    [Serializable]
    private class ParameterResult
    {
        public string id;
        public float min;
        public float max;
        public float baseline;
        public bool passed;
        public float maxReadError;
        public float maxResetError;
    }

    public static void RunMechanicalBootstrap()
    {
        string dataRoot = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        if (string.IsNullOrEmpty(dataRoot)) throw new InvalidOperationException("FU_XUAN_DATA is required for probe isolation.");
        if (!File.Exists(Path.Combine(dataRoot, ".test_mode"))) throw new InvalidOperationException(".test_mode is required for probe isolation.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new FileNotFoundException("Cubism prefab not found", PrefabPath);
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            CubismModel model = instance.GetComponentInChildren<CubismModel>(true);
            if (model == null) throw new InvalidOperationException("Prefab has no CubismModel.");
            CubismParameter[] candidates = Array.FindAll(model.Parameters, p =>
                p.Id.ToString().Contains("Angle") || p.Id.ToString().Contains("31") || p.Id.ToString().Contains("34"));
            string[] ids = Array.ConvertAll(candidates, p => p.Id.ToString());
            string mapPath = Path.Combine(Application.dataPath, "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");
            Report report = new Report { modelHash = AssetDatabase.GetAssetDependencyHash(PrefabPath).ToString(), mapHash = HashFile(mapPath), parameterCount = model.Parameters.Length, skeletonCandidates = ids };
            string output = Path.Combine(dataRoot, "capability-report-bootstrap.json");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[Live2DProbe] bootstrap report: " + output);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    public static void RunSkeletonMechanicalBatch()
    {
        string dataRoot = RequireIsolatedDataRoot();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new FileNotFoundException("Cubism prefab not found", PrefabPath);
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            CubismModel model = instance.GetComponentInChildren<CubismModel>(true);
            if (model == null) throw new InvalidOperationException("Prefab has no CubismModel.");
            CubismParameter[] candidates = Array.FindAll(model.Parameters, IsSkeletonCandidate);
            ParameterResult[] results = Array.ConvertAll(candidates, TestParameter);
            string mapPath = Path.Combine(Application.dataPath, "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");
            Report report = new Report { modelHash = AssetDatabase.GetAssetDependencyHash(PrefabPath).ToString(), mapHash = HashFile(mapPath), parameterCount = model.Parameters.Length, skeletonCandidates = Array.ConvertAll(candidates, p => p.Id.ToString()), results = results };
            string output = Path.Combine(dataRoot, "capability-report-skeleton-mechanical.json");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[Live2DProbe] mechanical report: " + output);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    private static string RequireIsolatedDataRoot()
    {
        string root = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        if (string.IsNullOrEmpty(root) || !File.Exists(Path.Combine(root, ".test_mode"))) throw new InvalidOperationException("FU_XUAN_DATA and .test_mode are required.");
        return root;
    }

    private static bool IsSkeletonCandidate(CubismParameter p)
    {
        string id = p.Id.ToString();
        return id.StartsWith("ParamAngle") || id.StartsWith("ParamBodyAngle") || id == "Param31" || id == "Param32" || id == "Param33" || id == "Param34" || id == "Param36" || id == "Param37";
    }

    private static ParameterResult TestParameter(CubismParameter parameter)
    {
        float baseline = parameter.Value, maxReadError = 0f, maxResetError = 0f;
        float[] points = { parameter.MinimumValue, (parameter.MinimumValue + parameter.MaximumValue) * 0.5f, parameter.MaximumValue };
        for (int repeat = 0; repeat < 3; repeat++) foreach (float point in points)
        {
            parameter.Value = point;
            maxReadError = Mathf.Max(maxReadError, Mathf.Abs(parameter.Value - point));
            parameter.Value = baseline;
            maxResetError = Mathf.Max(maxResetError, Mathf.Abs(parameter.Value - baseline));
        }
        return new ParameterResult { id = parameter.Id.ToString(), min = parameter.MinimumValue, max = parameter.MaximumValue, baseline = baseline, maxReadError = maxReadError, maxResetError = maxResetError, passed = maxReadError <= 0.0001f && maxResetError <= 0.0001f };
    }

    private static string HashFile(string path)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}
