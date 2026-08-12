using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 符玄「太卜阵法图」— 任务模板库（P5.3）
///
/// 设计目标：
///   ▸ 高频任务（查官网公告 / 看仓库更新 / 下载文件等）预置模板，
///     调用 openclaw_task 时只传模板名 + 参数，减少 LLM 重复组织任务描述的 token
///   ▸ 模板支持 {占位符}，ApplyTemplate 填充参数生成任务描述
///   ▸ 预置模板代码内初始化；用户/本座也可通过工具新增模板（持久化）
///   ▸ 数据文件 task_templates.json（DataPathConfig.DataRoot 下）
///
/// 模板字段：
///   name        —— 模板名（snake_case，如 "check_website_updates"）
///   description —— 用途描述（给 LLM 看，何时用此模板）
///   template    —— 任务描述模板（含 {占位符}，如 "访问 {url} 查看最新公告"）
///   category    —— 分类（信息/下载/监控/研究）
///   updatedAt   —— 更新时间
/// </summary>
[Serializable]
public class TaskTemplate
{
    public string name = "";
    public string description = "";
    public string template = "";
    public string category = "";
    public string updatedAt = "";
}

[Serializable]
public class TaskTemplateData
{
    public List<TaskTemplate> templates = new List<TaskTemplate>();
}

public class TaskTemplateManager : MonoBehaviour
{
    [Header("◈ 任务模板库配置")]
    [Tooltip("最大模板数（超出拒绝新增）")]
    public int maxTemplates = 30;

    private TaskTemplateData _data = new TaskTemplateData();

    public static TaskTemplateManager Instance { get; private set; }

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "task_templates.json");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ==================================================================
    //  预置模板（首次 Load 无文件时写入）
    // ==================================================================

    private static TaskTemplate[] BuiltinTemplates()
    {
        return new[]
        {
            new TaskTemplate
            {
                name = "check_website_updates",
                description = "检查某网站/页面是否有最新内容或公告更新，返回更新摘要",
                template = "访问 {url}，查看页面上最新的公告/更新内容（标题、日期、要点），用中文简洁汇总返回。如果页面无法访问，说明原因。",
                category = "信息"
            },
            new TaskTemplate
            {
                name = "check_repo_releases",
                description = "查看 GitHub 仓库的最新 Release 发布说明",
                template = "访问 GitHub 仓库 {repo} 的 releases 页面，列出最新一条 Release 的版本号、发布日期和主要更新内容（用中文总结）。",
                category = "信息"
            },
            new TaskTemplate
            {
                name = "download_file",
                description = "从 URL 下载文件到指定本地路径（大文件需耐心等待）",
                template = "从 {url} 下载文件，保存到 {save_path}。下载完成后确认文件存在并返回文件大小。若下载失败，尝试镜像或备用方案，最多重试2次后如实汇报。",
                category = "下载"
            },
            new TaskTemplate
            {
                name = "monitor_price",
                description = "查询某商品/物品的当前价格并汇总",
                template = "查询「{item}」的当前价格：搜索 {query}，返回找到的最低价/官方价对比（至少2个来源），用中文汇总。",
                category = "信息"
            },
            new TaskTemplate
            {
                name = "summarize_page",
                description = "抓取网页正文并总结内容要点",
                template = "抓取网页 {url} 的正文内容，总结其核心要点（3-6条），用中文输出。若页面需要登录或无法访问，说明情况。",
                category = "研究"
            }
        };
    }

    // ==================================================================
    //  公开接口 — 模板 CRUD
    // ==================================================================

    /// <summary>获取模板（不存在返回 null）</summary>
    public TaskTemplate GetTemplate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _data.templates.FirstOrDefault(t => t.name == name.Trim());
    }

    /// <summary>获取全部模板（只读副本）</summary>
    public List<TaskTemplate> GetAllTemplates()
    {
        return new List<TaskTemplate>(_data.templates);
    }

    /// <summary>模板总数</summary>
    public int Count => _data.templates.Count;

    /// <summary>
    /// 应用模板生成任务描述：把 {占位符} 替换为参数值。
    /// 未提供的占位符保持原样（LLM 会自行处理）。
    /// </summary>
    /// <param name="name">模板名</param>
    /// <param name="args">占位符 → 值 映射（如 {"url": "https://..."}）</param>
    /// <returns>生成的任务描述；模板不存在返回空串</returns>
    public string ApplyTemplate(string name, Dictionary<string, string> args)
    {
        var tpl = GetTemplate(name);
        if (tpl == null) return "";

        string result = tpl.template;
        if (args != null)
        {
            foreach (var kv in args)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                result = result.Replace("{" + kv.Key + "}", kv.Value ?? "");
            }
        }
        return result;
    }

    /// <summary>
    /// 新增或覆盖模板（同名覆盖）
    /// </summary>
    /// <returns>true=成功；false=参数非法或超上限</returns>
    public bool SaveTemplate(string name, string description, string template, string category = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(template))
            return false;

        name = name.Trim();
        var existing = GetTemplate(name);
        if (existing != null)
        {
            existing.description = description ?? "";
            existing.template = template.Trim();
            existing.category = category ?? "";
            existing.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            if (_data.templates.Count >= maxTemplates)
                return false;
            _data.templates.Add(new TaskTemplate
            {
                name = name,
                description = description ?? "",
                template = template.Trim(),
                category = category ?? "",
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });
        }

        Save();
        Debug.Log($"[TaskTemplateManager] 📋 模板已保存: {name}");
        return true;
    }

    /// <summary>删除模板，返回是否删除成功</summary>
    public bool RemoveTemplate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        int removed = _data.templates.RemoveAll(t => t.name == name.Trim());
        if (removed > 0)
        {
            Save();
            Debug.Log($"[TaskTemplateManager] 🗑 模板已删除: {name}");
            return true;
        }
        return false;
    }

    // ==================================================================
    //  为 SystemPrompt / 工具构建文本
    // ==================================================================

    /// <summary>
    /// 生成模板清单，注入 ChatManager SystemPrompt（让 LLM 知道有哪些模板可用）
    /// </summary>
    public string FormatForPrompt()
    {
        if (_data.templates.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n【太卜阵法图 · 任务模板】");
        sb.AppendLine("（以下任务模板可通过 openclaw_task 的 template 参数直接调用，省去重新组织任务描述的 token）");
        foreach (var t in _data.templates)
        {
            string cat = string.IsNullOrEmpty(t.category) ? "" : $"（{t.category}）";
            sb.AppendLine($"  • {t.name}{cat}: {t.description}");
        }
        sb.AppendLine("（调用模板时，把模板内的 {占位符} 替换为实际内容传入 template_args。）");
        return sb.ToString();
    }

    /// <summary>列出模板清单的紧凑文本（query_task_templates 工具用）</summary>
    public string ListTemplatesText()
    {
        if (_data.templates.Count == 0)
            return "📋 尚无任务模板。";

        var sb = new StringBuilder();
        sb.AppendLine("【任务模板清单】");
        foreach (var t in _data.templates)
        {
            string cat = string.IsNullOrEmpty(t.category) ? "" : $"（{t.category}）";
            sb.AppendLine($"• {t.name}{cat} — {t.description}");
        }
        sb.Append("使用方式：openclaw_task 传 template=模板名 + template_args={占位符:值}。");
        return sb.ToString();
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TaskTemplateManager] ❌ 保存失败: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                // 首次运行：写入预置模板
                _data.templates.AddRange(BuiltinTemplates());
                Save();
                return;
            }
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) { Save(); return; }
            _data = JsonUtility.FromJson<TaskTemplateData>(json);
            if (_data == null) _data = new TaskTemplateData();
            // 补齐缺失的预置模板（升级场景）
            EnsureBuiltinTemplates();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TaskTemplateManager] ❌ 载入失败: {e.Message}");
            _data = new TaskTemplateData();
        }
    }

    /// <summary>补录代码中新增的预置模板（不覆盖用户同名自定义模板）</summary>
    private void EnsureBuiltinTemplates()
    {
        bool changed = false;
        foreach (var builtin in BuiltinTemplates())
        {
            if (_data.templates.All(t => t.name != builtin.name))
            {
                _data.templates.Add(builtin);
                changed = true;
            }
        }
        if (changed) Save();
    }
}
