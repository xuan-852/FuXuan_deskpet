using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Live2D 模型参数映射器
///
/// 职责：
/// 1. 加载 JSON 映射文件（语义名 ↔ 参数 ID）
/// 2. 按语义名设置/读取参数值
/// 3. 验证参数完整性（启动时检查缺失参数）
/// 4. 支持运行时加载不同模型的映射表
///
/// 使用方式：
///   var mapper = new Live2DParameterMapper(cubismModel);
///   mapper.LoadMapping(jsonString);
///   mapper.Set("eye_l_open", 0.5f);
///   mapper.SetBulk(("arm_right_upper", 1f), ("arm_right_mid", 0.5f));
/// </summary>
public class Live2DParameterMapper
{
    private CubismModel _model;
    private Dictionary<string, string> _semanticToId;   // 语义名 → 参数 ID
    private Dictionary<string, string> _idToSemantic;   // 参数 ID → 语义名（反向查询）
    private Dictionary<string, float> _defaultValues;   // 参数 ID → 默认值
    private Dictionary<string, ParameterRange> _ranges; // 参数 ID → 范围
    private HashSet<string> _missingParams;             // 映射了但模型中不存在的参数
    private HashSet<string> _unknownWarned;              // 已报过警的未知语义名（防刷屏）

    private bool _loaded = false;

    /// <summary>映射是否已加载</summary>
    public bool IsLoaded => _loaded;

    /// <summary>当前模型名称</summary>
    public string ModelName { get; private set; }

    /// <summary>缺失参数列表（映射表里有但模型里没有）</summary>
    public IReadOnlyCollection<string> MissingParams => _missingParams;

    /// <summary>参数范围信息</summary>
    public struct ParameterRange
    {
        public float Min;
        public float Max;
        public float Default;
    }

    /// <summary>参数 ID → 语义名</summary>
    public IReadOnlyDictionary<string, string> IdToSemantic => _idToSemantic;

    /// <summary>语义名 → 参数 ID</summary>
    public IReadOnlyDictionary<string, string> SemanticToId => _semanticToId;

    public Live2DParameterMapper(CubismModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _semanticToId = new Dictionary<string, string>();
        _idToSemantic = new Dictionary<string, string>();
        _defaultValues = new Dictionary<string, float>();
        _ranges = new Dictionary<string, ParameterRange>();
        _missingParams = new HashSet<string>();
        _unknownWarned = new HashSet<string>();
        ModelName = "Unknown";
    }

    /// <summary>更换模型（运行时切换模型用）</summary>
    public void SwitchModel(CubismModel newModel)
    {
        _model = newModel ?? throw new ArgumentNullException(nameof(newModel));
        RefreshRanges();
    }

    #region 加载 / 保存映射

    /// <summary>从 JSON 字符串加载映射</summary>
    public void LoadMappingFromJson(string jsonString)
    {
        if (string.IsNullOrEmpty(jsonString))
            throw new ArgumentException("JSON 内容不能为空");

        try
        {
            var wrapper = JsonUtility.FromJson<ParamMapWrapper>(jsonString);
            if (wrapper?.entries == null || wrapper.entries.Length == 0)
                throw new Exception("JSON 格式错误：缺少 'entries' 数组");

            _semanticToId.Clear();
            _idToSemantic.Clear();
            _missingParams.Clear();

            ModelName = wrapper.modelName ?? "Unknown";

            foreach (var entry in wrapper.entries)
            {
                if (string.IsNullOrEmpty(entry.s) || string.IsNullOrEmpty(entry.p)) continue;
                _semanticToId[entry.s] = entry.p;
                _idToSemantic[entry.p] = entry.s;
            }

            RefreshRanges();
            _loaded = true;
            Debug.Log($"[Live2DParameterMapper] 已加载映射: {ModelName}, {_semanticToId.Count} 条目");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Live2DParameterMapper] 加载映射失败: {ex.Message}");
            _loaded = false;
        }
    }

    /// <summary>从 Resources 路径加载映射文件</summary>
    public bool LoadMappingFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogError($"[Live2DParameterMapper] Resources 中未找到映射文件: {resourcePath}");
            return false;
        }
        LoadMappingFromJson(asset.text);
        return _loaded;
    }

    /// <summary>从文件路径加载映射文件</summary>
    public bool LoadMappingFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[Live2DParameterMapper] 文件不存在: {filePath}");
            return false;
        }
        string json = File.ReadAllText(filePath);
        LoadMappingFromJson(json);
        return _loaded;
    }

    /// <summary>刷新参数范围和默认值（换模型或首次加载后调用）</summary>
    public void RefreshRanges()
    {
        if (_model == null) return;
        _defaultValues.Clear();
        _ranges.Clear();
        _missingParams.Clear();

        foreach (var kv in _semanticToId)
        {
            string paramId = kv.Value;
            var param = _model.Parameters.FindById(paramId);
            if (param != null)
            {
                _ranges[paramId] = new ParameterRange
                {
                    Min = param.MinimumValue,
                    Max = param.MaximumValue,
                    Default = param.DefaultValue
                };
                _defaultValues[paramId] = param.DefaultValue;
            }
            else
            {
                _missingParams.Add(paramId);
            }
        }

        if (_missingParams.Count > 0)
        {
            Debug.LogWarning($"[Live2DParameterMapper] {ModelName}: {_missingParams.Count} 个映射参数在模型中不存在: " +
                string.Join(", ", _missingParams));
        }
    }

    /// <summary>导出当前映射为 JSON（便于为新模型创建映射模板）</summary>
    public string ExportMappingTemplate()
    {
        if (_model == null) return "{}";

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"modelName\": \"{ModelName}_template\",");
        sb.AppendLine($"  \"description\": \"自动生成的映射模板，请填写每个语义名对应的实际参数 ID\",");
        sb.AppendLine("  \"entries\": [");

        bool first = true;
        foreach (var kv in _semanticToId)
        {
            if (!first) sb.AppendLine(",");
            sb.Append("    {\"s\": \"" + kv.Key + "\", \"p\": \"" + kv.Value + "\"}");
            first = false;
        }
        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.Append("}");

        return sb.ToString();
    }

    #endregion

    #region 设置 / 读取参数

    /// <summary>按语义名设置参数值（自动钳制到有效范围）</summary>
    public void Set(string semanticName, float value)
    {
        if (!_loaded || _model == null) return;

        if (!_semanticToId.TryGetValue(semanticName, out string paramId))
        {
            // ★ 每个未知语义名只报一次，防止 DeepSeek 脑补不存在的参数时刷几十万行日志
            if (_unknownWarned.Add(semanticName))
                Debug.LogWarning($"[Live2DParameterMapper] 未知语义名: {semanticName}");
            return;
        }

        var param = _model.Parameters.FindById(paramId);
        if (param == null) return;

        // 钳制到有效范围
        if (_ranges.TryGetValue(paramId, out var range))
        {
            param.Value = Mathf.Clamp(value, range.Min, range.Max);
        }
        else
        {
            param.Value = value;
        }
    }

    /// <summary>按语义名读取参数当前值</summary>
    public float Get(string semanticName)
    {
        if (!_loaded || _model == null) return 0f;

        if (!_semanticToId.TryGetValue(semanticName, out string paramId))
            return 0f;

        var param = _model.Parameters.FindById(paramId);
        return param != null ? param.Value : 0f;
    }

    /// <summary>批量设置参数值</summary>
    public void SetBulk(Dictionary<string, float> semanticValues)
    {
        if (!_loaded || _model == null) return;
        foreach (var kv in semanticValues)
            Set(kv.Key, kv.Value);
    }

    /// <summary>批量设置参数值（用匿名对象/内联字典语法）</summary>
    public void SetBulk(params (string name, float value)[] values)
    {
        if (!_loaded || _model == null) return;
        foreach (var (name, value) in values)
            Set(name, value);
    }

    /// <summary>按语义名将参数恢复到默认值</summary>
    public void ResetToDefault(string semanticName)
    {
        if (!_semanticToId.TryGetValue(semanticName, out string paramId)) return;
        if (_defaultValues.TryGetValue(paramId, out float def))
            Set(semanticName, def);
    }

    /// <summary>重置所有参数到默认值</summary>
    public void ResetAllToDefault()
    {
        foreach (var kv in _semanticToId)
            ResetToDefault(kv.Key);
    }

    /// <summary>重置一组参数到默认值</summary>
    public void ResetGroupToDefault(params string[] semanticNames)
    {
        foreach (var name in semanticNames)
            ResetToDefault(name);
    }

    /// <summary>获取参数范围</summary>
    public bool TryGetRange(string semanticName, out ParameterRange range)
    {
        range = default;
        if (!_semanticToId.TryGetValue(semanticName, out string paramId))
            return false;
        return _ranges.TryGetValue(paramId, out range);
    }

    /// <summary>获取语义名对应的参数 ID</summary>
    public string GetParamId(string semanticName)
    {
        return _semanticToId.TryGetValue(semanticName, out string id) ? id : null;
    }

    #endregion

    #region 工具

    /// <summary>验证模型是否包含映射表中所有参数</summary>
    public List<string> ValidateModel()
    {
        var issues = new List<string>();
        if (_model == null)
        {
            issues.Add("模型为空");
            return issues;
        }

        int totalExpected = _semanticToId.Count;
        int totalFound = totalExpected - _missingParams.Count;
        int totalModel = _model.Parameters.Length;

        issues.Add($"[{ModelName}] 映射表: {totalExpected} 个语义参数, 模型实际: {totalModel} 个参数");
        issues.Add($"[{ModelName}] 匹配成功: {totalFound}, 缺失: {_missingParams.Count}");

        if (_missingParams.Count > 0)
        {
            issues.Add("缺失参数: " + string.Join(", ", _missingParams));
        }

        // 检查模型中是否还有未映射的参数
        var mappedIds = new HashSet<string>(_idToSemantic.Keys);
        var unmapped = new List<string>();
        foreach (var p in _model.Parameters)
        {
            if (!mappedIds.Contains(p.Id))
                unmapped.Add(p.Id);
        }
        if (unmapped.Count > 0)
        {
            issues.Add($"未映射参数 ({unmapped.Count} 个): " + string.Join(", ", unmapped));
        }

        return issues;
    }

    /// <summary>获取完整诊断信息</summary>
    public string GetDiagnosticReport()
    {
        var lines = new List<string>();
        lines.Add($"=== Live2D 参数映射诊断: {ModelName} ===");
        lines.Add($"加载状态: {(_loaded ? "已加载" : "未加载")}");
        lines.Add($"映射条目: {_semanticToId.Count}");
        lines.Add($"缺失参数: {_missingParams.Count}");
        lines.Add("");

        foreach (var kv in _semanticToId)
        {
            string paramId = kv.Value;
            bool exists = !_missingParams.Contains(paramId);
            float current = 0f;
            string rangeStr = "N/A";
            if (exists)
            {
                current = Get(kv.Key);
                if (_ranges.TryGetValue(paramId, out var r))
                    rangeStr = $"[{r.Min:F2}, {r.Max:F2}] def={r.Default:F2}";
            }
            lines.Add($"  {kv.Key,-25} → {paramId,-15} {(exists ? $"当前={current:F3} {rangeStr}" : "❌ 缺失")}");
        }

        return string.Join("\n", lines);
    }

    #endregion

    #region JSON 序列化辅助

    [Serializable]
    private class ParamMapWrapper
    {
        public string formatVersion;
        public string modelName;
        public string description;
        public MapEntry[] entries;
    }

    [Serializable]
    private class MapEntry
    {
        /// <summary>semantic — 语义名</summary>
        public string s;
        /// <summary>paramId — 模型参数 ID</summary>
        public string p;
    }

    #endregion
}
