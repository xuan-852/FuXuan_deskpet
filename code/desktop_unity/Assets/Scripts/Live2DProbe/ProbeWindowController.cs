using System;
using System.Collections;
using System.Globalization;
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

    [Serializable]
    private sealed class CombinationBatchResult
    {
        public int repeats;
        public string writerMode;
        public CombinationResult[] combinations;
    }

    [Serializable]
    private sealed class CombinationResult
    {
        public string combinationId;
        public string[] parameterIds;
        public bool resetStable;
        public float maxResetMeanDifference;
        public float minCombinedMeanDifference;
        public float maxCombinedMeanDifference;
        public float minMaxCombinedMeanDifference;
        public float maxMinCombinedMeanDifference;
        public string[] frames;
    }

    [Serializable]
    private sealed class SweepResult
    {
        public string parameterId;
        public float baseline;
        public float target;
        public int steps;
        public bool resetStable;
        public float peakMeanDifference;
        public float resetMeanDifference;
        public string[] frames;
    }

    [Serializable]
    private sealed class CombinationSweepResult
    {
        public string[] parameterIds;
        public float[] baselines;
        public float[] targets;
        public int steps;
        public int repeats;
        public bool resetStable;
        public float peakMeanDifference;
        public float maxResetMeanDifference;
        public string[] frames;
    }

    [Serializable]
    private sealed class MotionPlaybackResult
    {
        public string candidateId;
        public string candidateFile;
        public float durationSeconds;
        public int steps;
        public string[] parameterIds;
        public bool resetStable;
        public float peakMeanDifference;
        public float maxAdjacentMeanDifference;
        public float resetMeanDifference;
        public string[] frames;
    }

    [Serializable]
    private sealed class MotionCandidateFile
    {
        public string candidateId;
        public float durationSeconds;
        public MotionCurveEntry[] curves;
    }

    [Serializable]
    private sealed class MotionCurveEntry
    {
        public string parameterId;
        public float[] segments;
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
        string combinations = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_COMBINATIONS");
        bool hasRangeOverride = TryReadProbeRange(out float rangeMinimum, out float rangeMaximum);
        float combinationRangeScale = ReadCombinationRangeScale();
        int sweepSteps = ReadSweepSteps();
        bool sweepToMinimum = ReadSweepToMinimum();
        string requestedCombination = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_COMBINATION_IDS");
        string requestedCombinationSweep = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_COMBINATION_SWEEP_IDS");
        if (!string.IsNullOrWhiteSpace(requestedCombinationSweep))
        {
            if (sweepSteps <= 0)
                throw new InvalidOperationException("FU_XUAN_PROBE_COMBINATION_SWEEP_IDS requires FU_XUAN_PROBE_SWEEP_STEPS.");
            string[] combinationIds = ParseDistinctParameterIds(requestedCombinationSweep,
                "FU_XUAN_PROBE_COMBINATION_SWEEP_IDS");
            CombinationSweepResult captured = null;
            yield return CaptureCombinationSweep(model, camera, combinationIds, sweepSteps, dir, preservePhysics,
                combinationRangeScale, value => captured = value);
            File.WriteAllText(Path.Combine(root, "custom-combination-sweep-report.json"), JsonUtility.ToJson(captured, true));
            Debug.Log("[Live2DProbe] custom combination sweep completed: " + string.Join(",", combinationIds));
            Application.Quit(0);
            yield break;
        }
        string motionCandidate = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_MOTION_PLAYBACK");
        if (!string.IsNullOrWhiteSpace(motionCandidate))
        {
            MotionPlaybackResult playback = null;
            yield return CaptureMotionPlayback(model, camera, motionCandidate, dir, preservePhysics, value => playback = value);
            File.WriteAllText(Path.Combine(root, "motion-playback-report.json"), JsonUtility.ToJson(playback, true));
            Debug.Log("[Live2DProbe] motion playback completed: " + playback.candidateId);
            Application.Quit(0);
            yield break;
        }
        if (!string.IsNullOrWhiteSpace(requestedCombination))
        {
            string[] combinationIds = ParseDistinctParameterIds(requestedCombination, "FU_XUAN_PROBE_COMBINATION_IDS");
            CombinationResult captured = null;
            yield return CaptureCombination(model, camera, combinationIds, "custom_combination", dir, preservePhysics, combinationRangeScale,
                value => captured = value);
            File.WriteAllText(Path.Combine(root, "custom-combination-report.json"), JsonUtility.ToJson(
                new CombinationBatchResult { repeats = 3, writerMode = preservePhysics ? "physics" : "frozen", combinations = new[] { captured } }, true));
            Debug.Log("[Live2DProbe] custom combination completed: " + string.Join(",", combinationIds));
            Application.Quit(0);
            yield break;
        }
        if (string.Equals(combinations, "skeleton", StringComparison.OrdinalIgnoreCase))
        {
            yield return CaptureSkeletonCombinations(model, camera, dir, preservePhysics);
            Application.Quit(0);
            yield break;
        }
        string[] ids = !string.IsNullOrEmpty(requested) ? new[] { requested }
            : string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
                ? model.Parameters.Where(item => item != null).Select(item => item.Id.ToString()).Distinct().ToArray()
                : SkeletonCandidates;
        if (hasRangeOverride && ids.Length != 1)
            throw new InvalidOperationException("FU_XUAN_PROBE_VALUE_RANGE only supports one requested parameter.");
        if (sweepSteps > 0)
        {
            if (ids.Length != 1 || !hasRangeOverride)
                throw new InvalidOperationException("FU_XUAN_PROBE_SWEEP_STEPS requires one parameter and FU_XUAN_PROBE_VALUE_RANGE.");
            CubismParameter sweepParameter = FindParameter(model, ids[0]);
            if (sweepParameter == null || rangeMinimum < sweepParameter.MinimumValue || rangeMaximum > sweepParameter.MaximumValue ||
                rangeMinimum >= rangeMaximum || sweepParameter.Value < rangeMinimum || sweepParameter.Value > rangeMaximum)
                throw new InvalidOperationException("Invalid sweep range.");
            SweepResult sweep = null;
            yield return CaptureSweep(model, camera, sweepParameter, sweepToMinimum ? rangeMinimum : rangeMaximum,
                sweepSteps, dir, preservePhysics, value => sweep = value);
            File.WriteAllText(Path.Combine(root, "parameter-sweep-report.json"), JsonUtility.ToJson(sweep, true));
            Application.Quit(0);
            yield break;
        }
        var results = new ParameterResult[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            CubismParameter parameter = FindParameter(model, ids[i]);
            if (parameter == null) throw new InvalidOperationException("Probe parameter not found: " + ids[i]);
            float minimum = hasRangeOverride ? rangeMinimum : parameter.MinimumValue;
            float maximum = hasRangeOverride ? rangeMaximum : parameter.MaximumValue;
            if (minimum < parameter.MinimumValue || maximum > parameter.MaximumValue || minimum >= maximum ||
                parameter.Value < minimum || parameter.Value > maximum)
                throw new InvalidOperationException("FU_XUAN_PROBE_VALUE_RANGE must be within the native range and include baseline.");
            yield return CaptureParameter(model, camera, parameter, minimum, maximum, dir, preservePhysics, value => results[i] = value);
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

    private static bool TryReadProbeRange(out float minimum, out float maximum)
    {
        minimum = 0f;
        maximum = 0f;
        string raw = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_VALUE_RANGE");
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string[] parts = raw.Split(',');
        if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out maximum))
            throw new InvalidOperationException("FU_XUAN_PROBE_VALUE_RANGE must be two invariant-culture floats: minimum,maximum.");
        return true;
    }

    private static float ReadCombinationRangeScale()
    {
        string raw = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_COMBINATION_RANGE_SCALE");
        if (string.IsNullOrWhiteSpace(raw)) return 1f;
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) || scale <= 0f || scale > 1f)
            throw new InvalidOperationException("FU_XUAN_PROBE_COMBINATION_RANGE_SCALE must be in (0, 1].");
        return scale;
    }

    private static int ReadSweepSteps()
    {
        string raw = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_SWEEP_STEPS");
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) || steps < 2 || steps > 60)
            throw new InvalidOperationException("FU_XUAN_PROBE_SWEEP_STEPS must be an integer in [2, 60].");
        return steps;
    }

    private static string[] ParseDistinctParameterIds(string raw, string variableName)
    {
        string[] ids = raw.Split(',').Select(id => id.Trim()).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
        if (ids.Length < 2)
            throw new InvalidOperationException(variableName + " requires at least two distinct parameter IDs.");
        return ids;
    }

    private static bool ReadSweepToMinimum()
    {
        string raw = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_SWEEP_TARGET");
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "max", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(raw, "min", StringComparison.OrdinalIgnoreCase)) return true;
        throw new InvalidOperationException("FU_XUAN_PROBE_SWEEP_TARGET must be min or max.");
    }

    // These are evidence groups, not semantic mappings or runtime actions.
    // Values are sampled at each member's true min/max and always restored.
    private static readonly string[][] SkeletonCombinations = {
        new[] { "ParamBodyAngleX", "ParamAngleX" },
        new[] { "ParamBodyAngleY", "ParamAngleY" },
        new[] { "ParamBodyAngleZ", "ParamAngleZ" },
        new[] { "Param31", "Param32", "Param33" },
        new[] { "Param34", "Param36", "Param37" },
        new[] { "ParamBodyAngleX", "Param34", "Param36", "Param37" },
        new[] { "ParamBodyAngleX", "Param31", "Param32", "Param33" },
    };

    private IEnumerator CaptureSkeletonCombinations(CubismModel model, Camera camera, string dir, bool preservePhysics)
    {
        var results = new CombinationResult[SkeletonCombinations.Length];
        for (int i = 0; i < SkeletonCombinations.Length; i++)
            yield return CaptureCombination(model, camera, SkeletonCombinations[i], "skeleton_" + (i + 1), dir,
                preservePhysics, 1f, value => results[i] = value);

        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("FU_XUAN_DATA"), "skeleton-combination-report.json"),
            JsonUtility.ToJson(new CombinationBatchResult { repeats = 3, writerMode = preservePhysics ? "physics" : "frozen", combinations = results }, true));
        Debug.Log("[Live2DProbe] skeleton combinations completed: " + results.Length);
    }

    private IEnumerator CaptureCombination(CubismModel model, Camera camera, string[] ids, string combinationId,
        string dir, bool preservePhysics, float rangeScale, Action<CombinationResult> done)
    {
        var parameters = ids.Select(id => FindParameter(model, id)).ToArray();
        if (parameters.Any(item => item == null)) throw new InvalidOperationException("Combination has missing parameter: " + combinationId);
        const int repeats = 3;
        bool capturesCrossCorners = parameters.Length == 2;
        int framesPerRepeat = 2 * parameters.Length + 4 + (capturesCrossCorners ? 2 : 0);
        var result = new CombinationResult { combinationId = combinationId, parameterIds = ids,
            frames = new string[repeats * framesPerRepeat] };
        var baseline = parameters.Select(item => item.Value).ToArray();
        var minimums = parameters.Select((item, index) => Mathf.Lerp(baseline[index], item.MinimumValue, rangeScale)).ToArray();
        var maximums = parameters.Select((item, index) => Mathf.Lerp(baseline[index], item.MaximumValue, rangeScale)).ToArray();
        float minSum = 0f, maxSum = 0f, minMaxSum = 0f, maxMinSum = 0f, resetMax = 0f;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            int frameIndex = repeat * framesPerRepeat;
            SetValues(parameters, baseline);
            CapturedFrame baseFrame = null;
            yield return CapturePoint(model, camera, dir, combinationId, repeat, "baseline", result.frames, frameIndex++, preservePhysics,
                frame => baseFrame = frame);
            for (int i = 0; i < parameters.Length; i++)
            {
                SetValues(parameters, baseline); parameters[i].Value = minimums[i];
                yield return CapturePoint(model, camera, dir, combinationId, repeat, "min_" + ids[i], result.frames, frameIndex++, preservePhysics, _ => { });
            }
            for (int i = 0; i < parameters.Length; i++) parameters[i].Value = minimums[i];
            yield return CapturePoint(model, camera, dir, combinationId, repeat, "combined_min", result.frames, frameIndex++, preservePhysics,
                frame => minSum += MeanPixelDifference(baseFrame.pixels, frame.pixels));
            if (capturesCrossCorners)
            {
                SetValues(parameters, baseline);
                parameters[0].Value = minimums[0];
                parameters[1].Value = maximums[1];
                yield return CapturePoint(model, camera, dir, combinationId, repeat, "combined_min_max", result.frames, frameIndex++, preservePhysics,
                    frame => minMaxSum += MeanPixelDifference(baseFrame.pixels, frame.pixels));
            }
            for (int i = 0; i < parameters.Length; i++)
            {
                SetValues(parameters, baseline); parameters[i].Value = maximums[i];
                yield return CapturePoint(model, camera, dir, combinationId, repeat, "max_" + ids[i], result.frames, frameIndex++, preservePhysics, _ => { });
            }
            for (int i = 0; i < parameters.Length; i++) parameters[i].Value = maximums[i];
            yield return CapturePoint(model, camera, dir, combinationId, repeat, "combined_max", result.frames, frameIndex++, preservePhysics,
                frame => maxSum += MeanPixelDifference(baseFrame.pixels, frame.pixels));
            if (capturesCrossCorners)
            {
                SetValues(parameters, baseline);
                parameters[0].Value = maximums[0];
                parameters[1].Value = minimums[1];
                yield return CapturePoint(model, camera, dir, combinationId, repeat, "combined_max_min", result.frames, frameIndex++, preservePhysics,
                    frame => maxMinSum += MeanPixelDifference(baseFrame.pixels, frame.pixels));
            }
            SetValues(parameters, baseline);
            yield return CapturePoint(model, camera, dir, combinationId, repeat, "reset", result.frames, frameIndex, preservePhysics,
                frame => resetMax = Mathf.Max(resetMax, MeanPixelDifference(baseFrame.pixels, frame.pixels)));
        }
        SetValues(parameters, baseline);
        result.minCombinedMeanDifference = minSum / repeats;
        result.maxCombinedMeanDifference = maxSum / repeats;
        result.minMaxCombinedMeanDifference = minMaxSum / repeats;
        result.maxMinCombinedMeanDifference = maxMinSum / repeats;
        result.maxResetMeanDifference = resetMax;
        result.resetStable = resetMax <= 0.1f;
        done(result);
    }

    private static void SetValues(CubismParameter[] parameters, float[] values)
    {
        for (int i = 0; i < parameters.Length; i++) parameters[i].Value = values[i];
    }

    private IEnumerator CapturePoint(CubismModel model, Camera camera, string dir, string combinationId, int repeat,
        string label, string[] paths, int index, bool preservePhysics, Action<CapturedFrame> done)
    {
        model.ForceUpdateNow();
        int settleFrames = preservePhysics ? 8 : 1;
        for (int settle = 0; settle < settleFrames; settle++) yield return null;
        if (preservePhysics) StabilizePhysics(model.gameObject);
        model.ForceUpdateNow();
        CapturedFrame frame = Capture(camera);
        string path = Path.Combine(dir, combinationId + "_r" + (repeat + 1) + "_" + label + ".png");
        File.WriteAllBytes(path, frame.png);
        paths[index] = path;
        done(frame);
    }

    private IEnumerator CaptureParameter(CubismModel model, Camera camera, CubismParameter parameter, float minimum, float maximum,
        string dir, bool preservePhysics, Action<ParameterResult> done)
    {
        const int repeats = 3;
        float baseline = parameter.Value;
        float mid = (minimum + maximum) * 0.5f;
        float[] values = { baseline, minimum, mid, maximum, baseline };
        string[] labels = { "baseline", "min", "mid", "max", "reset" };
        var result = new ParameterResult { parameterId = parameter.Id, baseline = baseline, minimum = minimum,
            maximum = maximum, frames = new string[repeats * labels.Length] };
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

    private IEnumerator CaptureSweep(CubismModel model, Camera camera, CubismParameter parameter, float target, int steps,
        string dir, bool preservePhysics, Action<SweepResult> done)
    {
        float baseline = parameter.Value;
        var result = new SweepResult { parameterId = parameter.Id, baseline = baseline, target = target, steps = steps,
            frames = new string[steps * 2 + 1] };
        CapturedFrame baseFrame = null;
        float peak = 0f;
        int index = 0;
        for (int point = 0; point <= steps * 2; point++)
        {
            float fraction = point <= steps ? point / (float)steps : (steps * 2 - point) / (float)steps;
            parameter.Value = Mathf.Lerp(baseline, target, fraction);
            model.ForceUpdateNow();
            int settleFrames = preservePhysics ? 8 : 1;
            for (int settle = 0; settle < settleFrames; settle++) yield return null;
            if (preservePhysics) StabilizePhysics(model.gameObject);
            model.ForceUpdateNow();
            CapturedFrame frame = Capture(camera);
            string path = Path.Combine(dir, parameter.Id + "_sweep_" + point.ToString("D3") + ".png");
            File.WriteAllBytes(path, frame.png);
            result.frames[index++] = path;
            if (point == 0) baseFrame = frame;
            else peak = Mathf.Max(peak, MeanPixelDifference(baseFrame.pixels, frame.pixels));
            if (point == steps * 2)
            {
                result.resetMeanDifference = MeanPixelDifference(baseFrame.pixels, frame.pixels);
                result.resetStable = result.resetMeanDifference <= 0.1f;
            }
        }
        parameter.Value = baseline;
        result.peakMeanDifference = peak;
        done(result);
    }

    private IEnumerator CaptureCombinationSweep(CubismModel model, Camera camera, string[] ids, int steps, string dir,
        bool preservePhysics, float rangeScale, Action<CombinationSweepResult> done)
    {
        CubismParameter[] parameters = ids.Select(id => FindParameter(model, id)).ToArray();
        if (parameters.Any(item => item == null))
            throw new InvalidOperationException("Combination sweep has a missing parameter.");

        const int repeats = 3;
        int pointsPerRepeat = steps * 2 + 1;
        float[] baselines = parameters.Select(item => item.Value).ToArray();
        float[] targets = parameters.Select((item, index) =>
            Mathf.Lerp(baselines[index], item.MaximumValue, rangeScale)).ToArray();
        var result = new CombinationSweepResult
        {
            parameterIds = ids,
            baselines = baselines,
            targets = targets,
            steps = steps,
            repeats = repeats,
            frames = new string[pointsPerRepeat * repeats],
        };

        float peakSum = 0f;
        float resetMax = 0f;
        int index = 0;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            CapturedFrame baseFrame = null;
            float repeatPeak = 0f;
            for (int point = 0; point < pointsPerRepeat; point++)
            {
                float fraction = point <= steps ? point / (float)steps : (steps * 2 - point) / (float)steps;
                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                    parameters[parameterIndex].Value = Mathf.Lerp(baselines[parameterIndex], targets[parameterIndex], fraction);
                model.ForceUpdateNow();
                int settleFrames = preservePhysics ? 8 : 1;
                for (int settle = 0; settle < settleFrames; settle++) yield return null;
                if (preservePhysics) StabilizePhysics(model.gameObject);
                model.ForceUpdateNow();
                CapturedFrame frame = Capture(camera);
                string path = Path.Combine(dir, "custom_combination_sweep_r" + (repeat + 1).ToString("D2")
                    + "_" + point.ToString("D3") + ".png");
                File.WriteAllBytes(path, frame.png);
                result.frames[index++] = path;
                if (point == 0) baseFrame = frame;
                else repeatPeak = Mathf.Max(repeatPeak, MeanPixelDifference(baseFrame.pixels, frame.pixels));
                if (point == pointsPerRepeat - 1)
                    resetMax = Mathf.Max(resetMax, MeanPixelDifference(baseFrame.pixels, frame.pixels));
            }
            peakSum += repeatPeak;
        }

        SetValues(parameters, baselines);
        result.peakMeanDifference = peakSum / repeats;
        result.maxResetMeanDifference = resetMax;
        result.resetStable = resetMax <= 0.1f;
        done(result);
    }

    // 外部动作候选回放：按候选定义的多参数曲线随时间写入并采帧，供候选包与双模型评审取证。
    private IEnumerator CaptureMotionPlayback(CubismModel model, Camera camera, string candidatePath, string dir,
        bool preservePhysics, Action<MotionPlaybackResult> done)
    {
        MotionCandidateFile candidate = JsonUtility.FromJson<MotionCandidateFile>(File.ReadAllText(candidatePath));
        if (candidate == null || candidate.curves == null || candidate.curves.Length == 0 || string.IsNullOrEmpty(candidate.candidateId))
            throw new InvalidOperationException("Motion candidate file is missing curves or candidateId: " + candidatePath);
        float duration = candidate.durationSeconds > 0f ? candidate.durationSeconds : 1f;
        var curves = new EmbodiedMotionCurve[candidate.curves.Length];
        var parameters = new CubismParameter[candidate.curves.Length];
        var baselines = new float[candidate.curves.Length];
        for (int i = 0; i < candidate.curves.Length; i++)
        {
            MotionCurveEntry entry = candidate.curves[i];
            parameters[i] = FindParameter(model, entry.parameterId);
            if (parameters[i] == null) throw new InvalidOperationException("Motion parameter missing on model: " + entry.parameterId);
            if (entry.segments == null || entry.segments.Length < 2) throw new InvalidOperationException("Motion curve has no segments: " + entry.parameterId);
            curves[i] = new EmbodiedMotionCurve(entry.parameterId, entry.segments.Select(v => (double)v).ToArray(), duration);
            baselines[i] = parameters[i].Value;
        }
        int steps = ReadMotionPlaybackSteps();
        var result = new MotionPlaybackResult
        {
            candidateId = candidate.candidateId,
            candidateFile = candidatePath,
            durationSeconds = duration,
            steps = steps,
            parameterIds = candidate.curves.Select(item => item.parameterId).ToArray(),
            frames = new string[steps + 2],
        };
        int index = 0;
        CapturedFrame baseFrame = null;
        CapturedFrame previousFrame = null;
        float peak = 0f, maxAdjacent = 0f;
        for (int point = 0; point <= steps + 1; point++)
        {
            bool isReset = point == steps + 1;
            if (isReset)
            {
                for (int i = 0; i < parameters.Length; i++) parameters[i].Value = baselines[i];
            }
            else if (point > 0)
            {
                float time = duration * point / (float)steps;
                for (int i = 0; i < curves.Length; i++) parameters[i].Value = curves[i].Evaluate(time);
            }
            model.ForceUpdateNow();
            int settleFrames = preservePhysics ? 8 : 1;
            for (int settle = 0; settle < settleFrames; settle++) yield return null;
            if (preservePhysics) StabilizePhysics(model.gameObject);
            model.ForceUpdateNow();
            CapturedFrame frame = Capture(camera);
            string path = Path.Combine(dir, candidate.candidateId + "_playback_" + point.ToString("D3") + ".png");
            File.WriteAllBytes(path, frame.png);
            result.frames[index++] = path;
            if (previousFrame != null) maxAdjacent = Mathf.Max(maxAdjacent, MeanPixelDifference(previousFrame.pixels, frame.pixels));
            previousFrame = frame;
            if (point == 0) baseFrame = frame;
            else if (!isReset) peak = Mathf.Max(peak, MeanPixelDifference(baseFrame.pixels, frame.pixels));
            else
            {
                result.resetMeanDifference = MeanPixelDifference(baseFrame.pixels, frame.pixels);
                result.resetStable = result.resetMeanDifference <= 0.1f;
            }
        }
        for (int i = 0; i < parameters.Length; i++) parameters[i].Value = baselines[i];
        result.peakMeanDifference = peak;
        result.maxAdjacentMeanDifference = maxAdjacent;
        done(result);
    }

    private static int ReadMotionPlaybackSteps()
    {
        string raw = Environment.GetEnvironmentVariable("FU_XUAN_PROBE_MOTION_STEPS");
        if (string.IsNullOrWhiteSpace(raw)) return 14;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) || steps < 4 || steps > 60)
            throw new InvalidOperationException("FU_XUAN_PROBE_MOTION_STEPS must be an integer in [4, 60].");
        return steps;
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
