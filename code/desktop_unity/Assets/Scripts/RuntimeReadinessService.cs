using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Runtime dependency status. Cloud readiness is configuration-only.</summary>
public sealed class RuntimeReadinessService : MonoBehaviour
{
    public enum CheckState { Unknown, Checking, Ready, Warning, Offline }
    public static RuntimeReadinessService Instance { get; private set; }
    public CheckState LocalState { get; private set; } = CheckState.Unknown;
    public CheckState BridgeState { get; private set; } = CheckState.Unknown;
    public CheckState CloudState { get; private set; } = CheckState.Unknown;
    public string LocalMessage { get; private set; } = "";
    public string BridgeMessage { get; private set; } = "";
    public string CloudMessage { get; private set; } = "";
    public string ShortStatus { get; private set; } = "检查中";
    public string DetailSummary { get; private set; } = "";
    public bool NeedsAttention => LocalState == CheckState.Warning || LocalState == CheckState.Offline
        || BridgeState == CheckState.Warning || BridgeState == CheckState.Offline
        || CloudState == CheckState.Warning || CloudState == CheckState.Offline;
    public event Action Changed;

    private bool _refreshRequested;
    private bool _refreshRunning;

    public static RuntimeReadinessService EnsureExists()
    {
        if (Instance != null) return Instance;
        GameObject go = new GameObject("RuntimeReadinessService");
        Instance = go.AddComponent<RuntimeReadinessService>();
        DontDestroyOnLoad(go);
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() { StartCoroutine(RefreshLoop()); }
    public void RequestRefresh() { _refreshRequested = true; }

    private IEnumerator RefreshLoop()
    {
        yield return new WaitForSecondsRealtime(0.8f);
        while (true)
        {
            if (!_refreshRunning) yield return StartCoroutine(RefreshNow());
            float elapsed = 0f;
            while (elapsed < 30f && !_refreshRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _refreshRequested = false;
        }
    }

    private IEnumerator RefreshNow()
    {
        _refreshRunning = true;
        LocalState = CheckState.Checking;
        BridgeState = CheckState.Checking;
        CloudState = CheckState.Checking;
        Publish();

        if (ChatConfig.UseOllamaMode)
        {
            bool localOk = false;
            string message = "";
            yield return LocalLLMClient.CheckHealthAsync((ok, text) => { localOk = ok; message = text ?? ""; });
            LocalState = localOk ? CheckState.Ready : CheckState.Warning;
            LocalMessage = message;
        }
        else
        {
            LocalState = CheckState.Unknown;
            LocalMessage = "未启用本地模式";
        }

        if (!ChatConfig.CloudRequestsEnabled)
        {
            CloudState = CheckState.Warning;
            CloudMessage = "云端调用已关闭";
        }
        else if (ChatConfig.UseOllamaMode)
        {
            CloudState = CheckState.Unknown;
            CloudMessage = "本地模式已锁定云端调用";
        }
        else if (!string.IsNullOrWhiteSpace(ChatConfig.ApiKey))
        {
            CloudState = CheckState.Ready;
            CloudMessage = "云端 API Key 已配置（未发起计费检查）";
        }
        else
        {
            CloudState = CheckState.Warning;
            CloudMessage = "未配置 DEEPSEEK_API_KEY";
        }

        string bridgeToken = OpenClawBridge.ConfiguredBridgeToken;
        if (string.IsNullOrWhiteSpace(bridgeToken))
        {
            BridgeState = CheckState.Warning;
            BridgeMessage = "未配置 BRIDGE_TOKEN";
        }
        else
        {
            Task<bool> healthTask = null;
            try { healthTask = OpenClawBridge.CheckHealthAsync(); }
            catch (Exception ex) { BridgeState = CheckState.Offline; BridgeMessage = ex.Message; }
            if (healthTask != null)
            {
                while (!healthTask.IsCompleted) yield return null;
                bool ok = healthTask.Status == TaskStatus.RanToCompletion && healthTask.Result;
                BridgeState = ok ? CheckState.Ready : CheckState.Offline;
                BridgeMessage = ok ? "桥接服务已就绪" : (OpenClawBridge.LastError ?? "桥接服务不可用");
            }
        }

        UpdateSummary();
        _refreshRunning = false;
        Publish();
    }

    private void UpdateSummary()
    {
        if (ChatConfig.UseOllamaMode)
        {
            ShortStatus = LocalState == CheckState.Ready ? "本地·就绪" : "本地·未就绪";
            DetailSummary = $"本地模型：{LocalLLMClient.ModelName} | {LocalMessage}";
        }
        else
        {
            ShortStatus = CloudState == CheckState.Ready ? "云端·已配置" : "云端·需检查";
            DetailSummary = $"云端：{CloudMessage}";
        }
    }

    private void Publish() { UpdateSummary(); Changed?.Invoke(); }
}
