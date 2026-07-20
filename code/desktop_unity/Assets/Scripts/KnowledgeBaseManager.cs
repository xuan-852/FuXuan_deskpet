using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 符玄「藏书阁」— 本地知识库（轻量 RAG）
///
/// 设计目标：
///   将用户指定的文件夹/文件索引到本地知识库中，
///   通过本地 Ollama 嵌入模型实现语义搜索，让符玄能回答
///   关于用户代码、笔记、文档的内容。
///
/// 架构：
///   文档分块 → 调用 Ollama 嵌入 → 存储 embedding → 语义检索
///
/// 用法：
///   1. AutoIndexOnStart 可让启动时自动索引配置的文件夹
///   2. AI 可通过 knowledge_search 工具检索
///   3. ChatManager 在构建 SystemPrompt 时自动注入相关上下文
/// </summary>
public class KnowledgeBaseManager : MonoBehaviour
{
    [Header("◈ Ollama 嵌入配置")]
    [Tooltip("Ollama API 地址")]
    public string ollamaUrl = "http://127.0.0.1:11434";
    [Tooltip("嵌入模型名（推荐 nomic-embed-text，轻量快速）")]
    public string embeddingModel = "nomic-embed-text";

    [Header("◈ 知识库配置")]
    [Tooltip("检索时最多返回多少条结果")]
    public int maxSearchResults = 5;
    [Tooltip("注入 SystemPrompt 时最多带多少条上下文")]
    public int maxContextResults = 3;
    [Tooltip("单个文本块最大字符数")]
    public int maxChunkSize = 800;
    [Tooltip("启动时自动索引的文件夹列表（以 | 分隔）")]
    public string autoIndexFolders = "";

    [Header("◈ 索引过滤")]
    [Tooltip("索引文件扩展名，逗号分隔")]
    public string indexExtensions = ".cs,.py,.js,.ts,.md,.txt,.json,.xml,.yaml,.yml,.html,.css,.sh,.ps1,.bat,.cfg,.ini,.tex,.csv,.lua,.java,.cpp,.h,.hpp";

    [Header("◈ 容量保护")]
    [Tooltip("单文件最大字符数（超过跳过，防日志大文件）")]
    public int maxFileSizeChars = 1_000_000;
    [Tooltip("最大分块数（超出后按时间淘汰最旧文档）")]
    public int maxTotalChunks = 3000;
    [Tooltip("最大原始字符数（所有文档原文总和）")]
    public int maxTotalChars = 5_000_000;

    // ==================================================================

    public static KnowledgeBaseManager Instance { get; private set; }

    /// <summary>文档库：key=文件绝对路径</summary>
    private Dictionary<string, IndexedDocument> _documents = new Dictionary<string, IndexedDocument>();

    /// <summary>是否正在索引</summary>
    public bool IsIndexing { get; private set; } = false;
    /// <summary>索引进度描述</summary>
    public string IndexProgress { get; private set; } = "";
    /// <summary>文档总数</summary>
    public int DocumentCount => _documents.Count;
    /// <summary>分块总数</summary>
    public int ChunkCount => _documents.Values.Sum(d => d.chunks.Count);

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "knowledge_base.json");
    private HashSet<string> _validExtensions;

    // ==================================================================

    [Serializable]
    public class DocumentChunk
    {
        public string text;           // 文本内容
        public List<float> embedding; // 向量（float[] 序列化不便）
        public int charIndex;         // 在源文件中的起始字符位置
        public int lineStart;         // 起始行号
        public int lineEnd;           // 结束行号
    }

    [Serializable]
    public class IndexedDocument
    {
        public string filePath;       // 绝对路径
        public string title;          // 文件名（显示用）
        public string lastModified;   // 最后修改时间
        public int totalChars;        // 总字符数
        public List<DocumentChunk> chunks = new List<DocumentChunk>();
    }

    [Serializable]
    public class KnowledgeBaseData
    {
        public List<IndexedDocument> documents = new List<IndexedDocument>();
    }

    public class SearchResult
    {
        public string filePath;
        public string title;
        public string text;
        public float score;
        public int lineStart;
        public int lineEnd;
    }

    // ==================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _validExtensions = new HashSet<string>(
            (indexExtensions ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLower()));
        if (_validExtensions.Count == 0)
            _validExtensions.Add(".txt");

        Load();
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(autoIndexFolders))
        {
            var folders = autoIndexFolders.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var folder in folders)
            {
                string trimmed = folder.Trim();
                if (Directory.Exists(trimmed))
                {
                    StartCoroutine(IndexFolderCoroutine(trimmed, true, null));
                }
            }
        }
    }

    // ==================================================================
    //  公开接口
    // ==================================================================

    /// <summary>索引单个文件</summary>
    public IEnumerator IndexFile(string filePath, Action<bool, string> onComplete = null)
    {
        if (!File.Exists(filePath))
        {
            onComplete?.Invoke(false, $"文件不存在: {filePath}");
            yield break;
        }

        string ext = Path.GetExtension(filePath).ToLower();
        if (!_validExtensions.Contains(ext))
        {
            onComplete?.Invoke(false, $"不支持的文件类型: {ext}");
            yield break;
        }

        IndexProgress = $"正在索引: {Path.GetFileName(filePath)}";
        IsIndexing = true;

        // 读取文件（自动检测编码：先试 UTF-8，若含非法字符则回退到 GBK）
        string content;
        try
        {
            byte[] rawBytes = File.ReadAllBytes(filePath);
            string utf8Text = Encoding.UTF8.GetString(rawBytes);
            // 若 UTF-8 解码没有产生替换字符 � (U+FFFD)，说明编码正确
            if (!utf8Text.Contains('\uFFFD'))
            {
                content = utf8Text;
            }
            else
            {
                // 回退到系统默认编码（中文 Windows = GBK）
                content = Encoding.Default.GetString(rawBytes);
                Debug.Log($"[KnowledgeBase] 文件编码非 UTF-8，已使用 GBK 读取: {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception e)
        {
            IsIndexing = false;
            onComplete?.Invoke(false, $"读取失败: {e.Message}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            IsIndexing = false;
            onComplete?.Invoke(false, "文件为空");
            yield break;
        }

        // —— 单文件大小保护（超过 1MB 跳过，避免日志大文件拖垮）——
        if (content.Length > maxFileSizeChars)
        {
            IsIndexing = false;
            onComplete?.Invoke(false, $"文件过大 ({content.Length / 1024}KB)，仅索引 ≤{maxFileSizeChars / 1024}KB 的文本文件");
            yield break;
        }

        // 分块
        var lines = content.Split('\n');
        var chunks = ChunkText(content, lines);

        if (chunks.Count == 0)
        {
            IsIndexing = false;
            onComplete?.Invoke(false, "分块结果为空");
            yield break;
        }

        // 获取嵌入向量
        var texts = chunks.Select(c => c.text).ToList();
        List<List<float>> embeddings = null;
        yield return GetEmbeddings(texts, result => embeddings = result);

        if (embeddings == null || embeddings.Count == 0)
        {
            IsIndexing = false;
            onComplete?.Invoke(false, "嵌入计算失败（请确认 Ollama 已启动且已 pull " + embeddingModel + "）");
            yield break;
        }

        // 关联 embedding
        for (int i = 0; i < chunks.Count && i < embeddings.Count; i++)
        {
            chunks[i].embedding = embeddings[i];
        }

        // 构建文档对象
        var doc = new IndexedDocument
        {
            filePath = filePath,
            title = Path.GetFileName(filePath),
            lastModified = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm"),
            totalChars = content.Length,
            chunks = chunks
        };

        // 存储
        _documents[filePath] = doc;

        // —— 容量保护：超出上限则按最后修改时间淘汰最旧文档 ——
        EnforceCapacity();

        Save();

        IsIndexing = false;
        IndexProgress = "";
        onComplete?.Invoke(true, $"已索引 {chunks.Count} 个分块");
    }

    /// <summary>索引文件夹（递归/非递归）</summary>
    public IEnumerator IndexFolderCoroutine(string folderPath, bool recursive, Action<bool, string> onComplete)
    {
        if (!Directory.Exists(folderPath))
        {
            onComplete?.Invoke(false, $"文件夹不存在: {folderPath}");
            yield break;
        }

        IsIndexing = true;
        var files = Directory.GetFiles(folderPath, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => _validExtensions.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        int successCount = 0;
        int failCount = 0;
        int total = files.Count;

        for (int i = 0; i < files.Count; i++)
        {
            if (!IsIndexing) // 允许外部取消
            {
                IndexProgress = "已取消";
                break;
            }

            string file = files[i];
            IndexProgress = $"索引中 [{i + 1}/{total}]: {Path.GetFileName(file)}";
            bool done = false;
            yield return IndexFile(file, (ok, msg) =>
            {
                if (ok) successCount++;
                else failCount++;
                done = true;
            });

            // 等待当前文件完成（协程串行）
            yield return new WaitUntil(() => done);
        }

        IsIndexing = false;
        IndexProgress = "";
        onComplete?.Invoke(true, $"索引完成：成功 {successCount}，失败 {failCount}，共 {total} 个文件");
    }

    /// <summary>语义搜索</summary>
    public IEnumerator Search(string query, int topK, Action<List<SearchResult>> onComplete)
    {
        if (_documents.Count == 0 || ChunkCount == 0)
        {
            onComplete?.Invoke(new List<SearchResult>());
            yield break;
        }

        // 获取查询的嵌入向量
        List<List<float>> queryEmbeds = null;
        yield return GetEmbeddings(new List<string> { query }, result => queryEmbeds = result);

        if (queryEmbeds == null || queryEmbeds.Count == 0)
        {
            onComplete?.Invoke(new List<SearchResult>());
            yield break;
        }

        var queryEmbed = queryEmbeds[0];

        // 计算余弦相似度
        var scored = new List<(float score, DocumentChunk chunk, IndexedDocument doc)>();

        foreach (var kv in _documents)
        {
            var doc = kv.Value;
            foreach (var chunk in doc.chunks)
            {
                if (chunk.embedding == null || chunk.embedding.Count == 0) continue;
                float sim = CosineSimilarity(queryEmbed, chunk.embedding);
                scored.Add((sim, chunk, doc));
            }
        }

        // 排序取 topK
        var results = scored
            .OrderByDescending(s => s.score)
            .Take(topK)
            .Select(s => new SearchResult
            {
                filePath = s.doc.filePath,
                title = s.doc.title,
                text = s.chunk.text,
                score = s.score,
                lineStart = s.chunk.lineStart,
                lineEnd = s.chunk.lineEnd
            })
            .ToList();

        onComplete?.Invoke(results);
    }

    /// <summary>获取用于 SystemPrompt 注入的知识上下文</summary>
    public string GetFormattedContext(string query, int maxResults = -1)
    {
        if (maxResults <= 0) maxResults = maxContextResults;
        // 同步方式返回空字符串，实际由协程填充到缓存
        return "";
    }

    /// <summary>协程版：搜索并格式化为 prompt 文本</summary>
    public IEnumerator SearchAndFormat(string query, int maxResults, Action<string> onComplete)
    {
        List<SearchResult> results = null;
        yield return Search(query, maxResults, r => results = r);

        if (results == null || results.Count == 0)
        {
            onComplete?.Invoke("");
            yield break;
        }

        var sb = new StringBuilder();
        sb.AppendLine("\n【藏书阁·检索结果】");
        sb.AppendLine($"本座在藏书阁中查阅了与「{query}」相关的记录：");

        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"\n[{i + 1}] {r.title}（相关度 {(r.score * 100):F0}%）");
            if (r.lineStart > 0 || r.lineEnd > 0)
                sb.AppendLine($"    行 {r.lineStart}-{r.lineEnd}");
            sb.AppendLine($"    {r.text.Trim()}");
        }

        sb.AppendLine("\n（以上为藏书阁中检索到的相关内容，供本座参考。）");

        onComplete?.Invoke(sb.ToString());
    }

    /// <summary>移除文档</summary>
    public void RemoveDocument(string filePath)
    {
        if (_documents.Remove(filePath))
        {
            Save();
        }
    }

    /// <summary>清空知识库</summary>
    public void ClearAll()
    {
        _documents.Clear();
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    /// <summary>获取统计信息</summary>
    public string GetStatistics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📚 藏书阁概要");
        sb.AppendLine($"   文档数: {_documents.Count}");
        sb.AppendLine($"   分块数: {ChunkCount}");
        sb.AppendLine($"   嵌入模型: {embeddingModel}");
        sb.AppendLine($"   索引目录: {(string.IsNullOrEmpty(autoIndexFolders) ? "未设置" : autoIndexFolders)}");

        if (_documents.Count > 0)
        {
            sb.AppendLine("\n   已索引文档:");
            foreach (var kv in _documents.OrderBy(d => d.Value.title))
            {
                var doc = kv.Value;
                sb.AppendLine($"   • {doc.title} ({doc.chunks.Count} 块, {doc.totalChars} 字符)");
            }
        }

        return sb.ToString();
    }

    // ==================================================================
    //  向量计算
    // ==================================================================

    private float CosineSimilarity(List<float> a, List<float> b)
    {
        if (a == null || b == null || a.Count == 0 || a.Count != b.Count) return 0f;

        float dot = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        float mag = Mathf.Sqrt(magA) * Mathf.Sqrt(magB);
        return mag > 0.0001f ? dot / mag : 0f;
    }

    // ==================================================================
    //  分块
    // ==================================================================

    private List<DocumentChunk> ChunkText(string content, string[] lines)
    {
        var chunks = new List<DocumentChunk>();

        // 按空行分割为段落
        var paragraphs = new List<(string text, int startLine, int endLine, int charIdx)>();
        var currentPara = new StringBuilder();
        int paraStartLine = 0;
        int paraStartChar = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimEnd('\r');

            if (i == 0) paraStartChar = 0;

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // 空行 = 段落结束
                if (currentPara.Length > 0)
                {
                    paragraphs.Add((currentPara.ToString(), paraStartLine + 1, i, paraStartChar));
                    currentPara.Clear();
                }
                paraStartLine = i + 1;
                paraStartChar = content.IndexOf('\n', paraStartChar) + 1;
                if (paraStartChar <= 0) paraStartChar = content.Length;
                continue;
            }

            if (currentPara.Length == 0)
            {
                paraStartLine = i;
                paraStartChar = content.IndexOf(trimmed, StringComparison.Ordinal);
                if (paraStartChar < 0) paraStartChar = 0;
            }

            currentPara.AppendLine(trimmed);
        }

        if (currentPara.Length > 0)
        {
            paragraphs.Add((currentPara.ToString(), paraStartLine + 1, lines.Length, paraStartChar));
        }

        // 如果段落太少或文件小，直接整块
        if (paragraphs.Count <= 1 && content.Length <= maxChunkSize)
        {
            chunks.Add(new DocumentChunk
            {
                text = content,
                charIndex = 0,
                lineStart = 1,
                lineEnd = lines.Length
            });
            return chunks;
        }

        // 合并小段落到大块（不超过 maxChunkSize）
        var merged = new StringBuilder();
        int mergeStartLine = 1;
        int mergeStartChar = 0;
        int mergeEndLine = 0;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];

            if (merged.Length == 0)
            {
                mergeStartLine = p.startLine;
                mergeStartChar = p.charIdx;
            }

            if (merged.Length + p.text.Length > maxChunkSize && merged.Length > 0)
            {
                // 当前块已满，保存
                chunks.Add(new DocumentChunk
                {
                    text = merged.ToString().TrimEnd(),
                    charIndex = mergeStartChar,
                    lineStart = mergeStartLine,
                    lineEnd = mergeEndLine
                });
                merged.Clear();
                mergeStartLine = p.startLine;
                mergeStartChar = p.charIdx;
            }

            merged.Append(p.text);
            mergeEndLine = p.endLine;
        }

        if (merged.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                text = merged.ToString().TrimEnd(),
                charIndex = mergeStartChar,
                lineStart = mergeStartLine,
                lineEnd = mergeEndLine
            });
        }

        return chunks;
    }

    // ==================================================================
    //  Ollama 嵌入 API
    // ==================================================================

    private IEnumerator GetEmbeddings(List<string> texts, Action<List<List<float>>> onComplete)
    {
        if (texts == null || texts.Count == 0)
        {
            onComplete?.Invoke(new List<List<float>>());
            yield break;
        }

        string url = ollamaUrl.TrimEnd('/') + "/api/embed";

        // 构建请求体
        var reqObj = new Dictionary<string, object>
        {
            ["model"] = embeddingModel,
            ["input"] = texts.Count == 1 ? texts[0] : (object)texts.ToArray()
        };

        string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(reqObj);

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 120; // 嵌入可能需要一些时间

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string responseText = req.downloadHandler.text;
                var result = ParseEmbeddingResponse(responseText);
                onComplete?.Invoke(result);
            }
            else
            {
                string errBody = req.downloadHandler?.text ?? "";
                string errMsg = !string.IsNullOrEmpty(errBody) ? errBody : req.error;
                Debug.LogWarning($"[KnowledgeBase] 嵌入请求失败: {errMsg}");
                onComplete?.Invoke(null);
            }
        }
    }

    private List<List<float>> ParseEmbeddingResponse(string json)
    {
        try
        {
            var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (obj == null) return null;

            if (obj.TryGetValue("embeddings", out var embedObj) && embedObj is Newtonsoft.Json.Linq.JArray arr)
            {
                var result = new List<List<float>>();
                foreach (var item in arr)
                {
                    if (item is Newtonsoft.Json.Linq.JArray vec)
                    {
                        var list = new List<float>();
                        foreach (var v in vec)
                        {
                            list.Add((float)v);
                        }
                        result.Add(list);
                    }
                }
                return result;
            }
            // 单条嵌入返回
            else if (obj.TryGetValue("embedding", out var singleEmbed) && singleEmbed is Newtonsoft.Json.Linq.JArray singleArr)
            {
                var singleList = new List<float>();
                foreach (var v in singleArr)
                {
                    singleList.Add((float)v);
                }
                return new List<List<float>> { singleList };
            }

            Debug.LogWarning($"[KnowledgeBase] 嵌入响应格式异常: {Truncate(json, 200)}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[KnowledgeBase] 嵌入响应解析失败: {e.Message}");
            return null;
        }
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    private void Save()
    {
        try
        {
            var data = new KnowledgeBaseData();
            data.documents = _documents.Values.ToList();

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented);
            string dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, json, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogError($"[KnowledgeBase] 保存失败: {e.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<KnowledgeBaseData>(json);
            if (data == null || data.documents == null) return;

            _documents.Clear();
            foreach (var doc in data.documents)
            {
                if (!string.IsNullOrEmpty(doc.filePath))
                {
                    _documents[doc.filePath] = doc;
                }
            }

            Debug.Log($"[KnowledgeBase] 已加载 {_documents.Count} 个文档，{ChunkCount} 个分块");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[KnowledgeBase] 加载失败（首次使用或文件损坏）: {e.Message}");
            _documents.Clear();
        }
    }

    // ==================================================================
    //  容量保护
    // ==================================================================

    /// <summary>
    /// 检查当前总量，若超出上限则按"最后修改时间"淘汰最旧文档，
    /// 直到各项指标回到上限以内。
    /// </summary>
    private void EnforceCapacity()
    {
        int evicted = 0;

        while (ChunkCount > maxTotalChunks || TotalChars > maxTotalChars)
        {
            // 找最旧的文档（按 lastModified 排序）
            var oldest = _documents.Values
                .OrderBy(d => d.lastModified)
                .FirstOrDefault();
            if (oldest == null) break;

            _documents.Remove(oldest.filePath);
            evicted++;
        }

        if (evicted > 0)
        {
            Debug.LogWarning($"[KnowledgeBase] 容量上限触发：已自动淘汰 {evicted} 个最旧文档，" +
                $"当前分块={ChunkCount}，总字符={TotalChars}");
        }
    }

    /// <summary>所有文档的原始字符数总和</summary>
    private int TotalChars => _documents.Values.Sum(d => d.totalChars);

    // ==================================================================
    //  工具方法
    // ==================================================================

    /// <summary>将长文本截断到指定长度（用于日志）</summary>
    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text.Substring(0, maxLen) + "...";
    }
}
