using System.IO;
using UnityEngine;

/// <summary>
/// 符玄「天机簿」— 配置持久化
/// 保存/加载运行时设置到 pet_config.json（persistentDataPath）
/// 重启后保留上次的权重调节、API 地址、天气城市等配置
/// </summary>
public class PetConfig : MonoBehaviour
{
    [System.Serializable]
    public class ConfigData
    {
        // ===== API =====
        public string apiUrl = "https://api.deepseek.com";
        public string model = "deepseek-chat";

        // ===== 天气 =====
        public int weatherSource = 0;          // 0=WttrIn, 1=QWeather
        public float weatherUpdateInterval = 300f;
        public string cityCode = "Nanjing";
        public int debugHourOverride = -1;

        // ===== 地面任务权重 =====
        public int weightLeftEdge = 1;
        public int weightRightEdge = 1;
        public int weightLeftTime = 1;
        public int weightRightTime = 1;
        public int weightStop = 6;

        // ===== 初始位置 =====
        public int startX = 50;
        public int startY = -1;

        // ===== MotionAgent（分神化身）配置 =====
        public bool motionAgentEnabled = true;
        public string localModel = "qwen2.5:0.5b";
        public string localApiUrl = "http://127.0.0.1:11434/v1";
    }

    [Header("当前配置（保存后持久化）")]
    public ConfigData data = new ConfigData();

    public static PetConfig Instance { get; private set; }

    private string FilePath => Path.Combine(Application.persistentDataPath, "pet_config.json");

    void Awake()
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

    /// <summary>保存配置到磁盘</summary>
    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
            Debug.Log($"[PetConfig] ✅ 天机簿已存于 {FilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PetConfig] ❌ 保存失败: {e.Message}");
        }
    }

    /// <summary>从磁盘读取配置</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Debug.Log("[PetConfig] 无已有配置，使用默认值");
                return;
            }
            string json = File.ReadAllText(FilePath);
            var loaded = JsonUtility.FromJson<ConfigData>(json);
            if (loaded != null)
            {
                data = loaded;
                Debug.Log($"[PetConfig] ✅ 天机簿已载入 ({FilePath})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PetConfig] ❌ 载入失败: {e.Message}");
        }
    }

    /// <summary>将当前配置应用到各组件</summary>
    public void ApplyAll()
    {
        // ChatManager
        var chat = FindObjectOfType<ChatManager>();
        if (chat != null)
        {
            chat.apiUrl = data.apiUrl;
            chat.model = data.model;
        }

        // TimeWeatherController
        var twc = FindObjectOfType<TimeWeatherController>();
        if (twc != null)
        {
            twc.weatherSource = (TimeWeatherController.WeatherSource)data.weatherSource;
            twc.weatherUpdateInterval = data.weatherUpdateInterval;
            twc.cityCode = data.cityCode;
            twc.debugHourOverride = data.debugHourOverride;
        }

        // DesktopPet
        var pet = FindObjectOfType<DesktopPet>();
        if (pet != null)
        {
            pet.taskWeightMoveLeftEdge = data.weightLeftEdge;
            pet.taskWeightMoveRightEdge = data.weightRightEdge;
            pet.taskWeightMoveLeftTime = data.weightLeftTime;
            pet.taskWeightMoveRightTime = data.weightRightTime;
            pet.taskWeightStopTime = data.weightStop;
            pet.startX = data.startX;
            pet.startY = data.startY;
        }

        // MotionAgent
        var agent = FindObjectOfType<MotionAgent>();
        if (agent != null)
        {
            agent.enabled = data.motionAgentEnabled;
            agent.localModel = data.localModel;
            agent.localApiUrl = data.localApiUrl;
        }

        Debug.Log("[PetConfig] 已应用全部配置");
    }

    /// <summary>从各组件读取当前值到 data</summary>
    public void CollectAll()
    {
        var chat = FindObjectOfType<ChatManager>();
        if (chat != null)
        {
            data.apiUrl = chat.apiUrl;
            data.model = chat.model;
        }

        var twc = FindObjectOfType<TimeWeatherController>();
        if (twc != null)
        {
            data.weatherSource = (int)twc.weatherSource;
            data.weatherUpdateInterval = twc.weatherUpdateInterval;
            data.cityCode = twc.cityCode;
            data.debugHourOverride = twc.debugHourOverride;
        }

        var pet = FindObjectOfType<DesktopPet>();
        if (pet != null)
        {
            data.weightLeftEdge = pet.taskWeightMoveLeftEdge;
            data.weightRightEdge = pet.taskWeightMoveRightEdge;
            data.weightLeftTime = pet.taskWeightMoveLeftTime;
            data.weightRightTime = pet.taskWeightMoveRightTime;
            data.weightStop = pet.taskWeightStopTime;
            data.startX = pet.startX;
            data.startY = pet.startY;
        }

        var agent = FindObjectOfType<MotionAgent>();
        if (agent != null)
        {
            data.motionAgentEnabled = agent.enabled;
            data.localModel = agent.localModel;
            data.localApiUrl = agent.localApiUrl;
        }
    }
}
