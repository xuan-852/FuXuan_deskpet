using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 时间与天气控制器 — 驱动宠物的昼夜/天气反应
///
/// 职责：
/// - 每帧检测系统时间
/// - 定期从 wttr.in 获取天气（可配置间隔）
/// - 公开状态供 Live2DRenderer/DesktopPet 查询
/// </summary>
public class TimeWeatherController : MonoBehaviour
{
    [Header("天气更新")]
    [Tooltip("天气数据源：WttrIn=自动IP定位（无需Key），QWeather=和风天气（需注册Key）")]
    public WeatherSource weatherSource = WeatherSource.WttrIn;
    [Tooltip("天气轮询间隔（秒），0=不查询天气")]
    public float weatherUpdateInterval = 300f; // 5分钟

    [Tooltip("城市代码（用于 wttr.in），空=自动IP定位")]
    public string cityCode = "Nanjing";

    // 和风天气 Key — 从 ChatConfig 读取环境变量
    public string qWeatherKey => ChatConfig.QWeatherApiKey;

    public enum WeatherSource { WttrIn, QWeather }

    [Header("调试")]
    [Tooltip("强制指定小时（-1=跟随系统）")]
    public int debugHourOverride = -1;

    // ===== 公开状态 =====
    [System.NonSerialized] public int hour;           // 当前小时 0~23
    [System.NonSerialized] public bool isNight;        // 18~6 夜间
    [System.NonSerialized] public float dayPhase;      // 0~1 (6点日出→18点日落)
    [System.NonSerialized] public bool isSleepyTime;   // 22~5 犯困时段

    public enum WeatherType
    {
        Unknown,
        Clear,      // ☀️ 晴
        Cloudy,     // ☁️ 多云
        Overcast,   // 🌥 阴
        Rain,       // 🌧 雨
        Drizzle,    // 🌦 小雨
        Thunder,    // ⛈ 雷雨
        Snow,       // ❄️ 雪
        Fog,        // 🌫 雾
    }

    [System.NonSerialized] public WeatherType weather = WeatherType.Unknown;
    [System.NonSerialized] public float temperatureC = 20f;   // 默认室温
    [System.NonSerialized] public int windSpeedKmh = 0;       // 风速 km/h
    [System.NonSerialized] public string windDirection = "";  // 风向箭头符号（←↑→↓等）
    [System.NonSerialized] public int humidityPercent = 50;   // 湿度 %
    [System.NonSerialized] public int pressureHpa = 1013;     // 气压 hPa
    [System.NonSerialized] public bool weatherFetched = false; // 是否成功获取过

    [Header("AI 天气语录")]
    [Tooltip("用 DeepSeek 生成符玄风格的天气语录（空=已禁用）")]
    public string aiApiUrl = "https://api.deepseek.com";
    [System.NonSerialized] public string aiApiKey = ChatConfig.ApiKey;
    public string aiModel = "deepseek-chat";
    [Tooltip("每次天气变化时生成多少句语录")]
    public int aiLineCount = 6;

    // ===== AI 生成的天气语录缓存 =====
    [System.NonSerialized]
    public System.Collections.Generic.List<string> aiWeatherLines
        = new System.Collections.Generic.List<string>();
    [System.NonSerialized]
    public bool aiWeatherReady = false;  // AI 语录是否已生成完毕

    private float _weatherTimer = 0f;
    private bool _isFetching = false;
    private WeatherType _lastFetchedWeather = WeatherType.Unknown; // 检测天气变化
    [System.NonSerialized] public string weatherSourceLabel = "wttr.in天眼"; // 实际使用的数据源（供 ToolCallInvoker 报告）

    private void Start()
    {
        UpdateTime();
        // 首次启动立即获取一次天气
        if (weatherUpdateInterval > 0f)
        {
            _weatherTimer = weatherUpdateInterval; // 让第一帧立即触发
        }
    }

    private void Update()
    {
        // 每帧更新时间
        UpdateTime();

        // 定期获取天气
        if (weatherUpdateInterval > 0f)
        {
            _weatherTimer += Time.deltaTime;
            if (_weatherTimer >= weatherUpdateInterval && !_isFetching)
            {
                _weatherTimer = 0f;
                StartCoroutine(FetchWeather());
            }
        }
    }

    private void UpdateTime()
    {
        int rawHour;
        if (debugHourOverride >= 0 && debugHourOverride <= 23)
            rawHour = debugHourOverride;
        else
            rawHour = DateTime.Now.Hour;

        hour = rawHour;
        isNight = (hour < 6 || hour >= 18);
        isSleepyTime = (hour >= 22 || hour < 5);

        // dayPhase: 6点=0, 12点=0.5, 18点=1.0
        if (hour >= 6 && hour < 18)
        {
            dayPhase = (hour - 6) / 12f;
        }
        else if (hour < 6)
        {
            dayPhase = 0f; // 凌晨统一为0
        }
        else
        {
            dayPhase = 1f; // 夜晚统一为1
        }
    }

    private IEnumerator FetchWeather()
    {
        _isFetching = true;

        if (weatherSource == WeatherSource.QWeather && !string.IsNullOrEmpty(qWeatherKey))
        {
            weatherSourceLabel = "和风天机";
            Debug.Log($"[TimeWeather] 和风天气 Key 长度: {qWeatherKey?.Length ?? 0}");
            yield return StartCoroutine(FetchQWeather());
        }
        else
        {
            weatherSourceLabel = "wttr.in天眼";
            yield return StartCoroutine(FetchWttrIn());
        }

        _isFetching = false;
    }

    /// <summary>从 wttr.in 获取天气（IP 自动定位）</summary>
    private IEnumerator FetchWttrIn()
    {
        string url = string.IsNullOrEmpty(cityCode)
            ? "https://wttr.in/?format=%C+%t+%w+%h+%p+%P"
            : $"https://wttr.in/{cityCode}?format=%C+%t+%w+%h+%p+%P";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string raw = req.downloadHandler.text.Trim();
                Debug.Log($"[TimeWeather] wttr.in 响应: {raw}");
                ParseWttrIn(raw);
                weatherFetched = true;
                OnWeatherChanged();
            }
            else
            {
                Debug.LogWarning($"[TimeWeather] wttr.in 失败: {req.error}");
            }
        }
    }

    /// <summary>从和风天气 API 获取（需 Key）https://dev.qweather.com</summary>
    private IEnumerator FetchQWeather()
    {
        // Step 1: 通过 IP 获取城市 LocationID
        string locationId = "";
        string ipUrl = "https://geoapi.qweather.com/v2/city/lookup?location=127.0.0.1&key=" + qWeatherKey;
        using (UnityWebRequest req = UnityWebRequest.Get(ipUrl))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler.text;
                // 尝试提取第一个位置的 id
                var match = System.Text.RegularExpressions.Regex.Match(body, "\"id\"\\s*:\\s*\"(\\d+)\"");
                if (match.Success)
                    locationId = match.Groups[1].Value;
            }
            else
            {
                Debug.LogWarning($"[TimeWeather] 和风IP定位失败: {req.error}");
                _isFetching = false;
                yield break;
            }
        }

        if (string.IsNullOrEmpty(locationId))
        {
            Debug.LogWarning("[TimeWeather] 无法解析和风城市ID");
            _isFetching = false;
            yield break;
        }

        // Step 2: 获取实时天气
        string weatherUrl = $"https://devapi.qweather.com/v7/weather/now?location={locationId}&key={qWeatherKey}";
        using (UnityWebRequest req = UnityWebRequest.Get(weatherUrl))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler.text;
                Debug.Log($"[TimeWeather] 和风天气响应: {body}");
                ParseQWeather(body);
                weatherFetched = true;
                OnWeatherChanged();
            }
            else
            {
                Debug.LogWarning($"[TimeWeather] 和风天气失败: {req.error}");
            }
        }
    }

    /// <summary>天气变化后的公共处理（AI 语录生成等）</summary>
    private void OnWeatherChanged()
    {
        if (weather != _lastFetchedWeather)
        {
            _lastFetchedWeather = weather;
            aiWeatherReady = false;
            if (!string.IsNullOrEmpty(aiApiKey) && aiLineCount > 0)
            {
                StartCoroutine(GenerateWeatherLines(weather, temperatureC));
            }
        }
    }

    /// <summary>
    /// 解析 wttr.in 返回的天气字符串
    /// 格式: "Light Rain Shower, Mist +27°C ←29km/h 100% 0.4mm 988hPa"
    ///       天气描述 +温度 风向风速 湿度 降水量 气压
    /// </summary>
    private void ParseWttrIn(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        // ——— 拆分字段 ———
        // 格式固定：天气描述 +温度 风向风速 湿度% 降水量 气压hPa
        // 先按空格分割，但天气描述本身可能含空格，所以从后往前解析
        string[] parts = raw.Trim().Split(' ');
        if (parts.Length < 3) return;

        // 从尾部解析已知格式字段
        int pressureIdx = -1;
        int precipIdx = -1;
        int humidityIdx = -1;
        int windIdx = -1;
        int tempIdx = -1;

        for (int i = parts.Length - 1; i >= 0; i--)
        {
            string p = parts[i];
            if (p.EndsWith("hPa") && pressureIdx < 0)
                pressureIdx = i;
            else if (p.EndsWith("mm") && precipIdx < 0)
                precipIdx = i;
            else if (p.EndsWith("%") && humidityIdx < 0)
                humidityIdx = i;
            else if ((p.Contains("km/h") || p.Contains("km" )) && windIdx < 0)
                windIdx = i;
            else if (p.Contains("°C") && tempIdx < 0)
                tempIdx = i;
        }

        // ——— 解析温度 ———
        try
        {
            if (tempIdx >= 0)
            {
                string tStr = parts[tempIdx].Replace("°C", "").Replace("+", "");
                if (int.TryParse(tStr, out int temp))
                    temperatureC = temp;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Weather] 温度解析异常: {ex.Message}");
        }

        // ——— 解析风速和风向 ———
        try
        {
            if (windIdx >= 0)
            {
                string wStr = parts[windIdx];
                // 提取风向箭头（第一个非数字/字母的字符）
                int dirEnd = 0;
                for (int i = 0; i < wStr.Length; i++)
                {
                    if (char.IsLetter(wStr[i]) || char.IsDigit(wStr[i]))
                    { dirEnd = i; break; }
                }
                if (dirEnd > 0)
                    windDirection = wStr.Substring(0, dirEnd);
                else
                    windDirection = "";

                // 提取风速数值
                var kmMatch = System.Text.RegularExpressions.Regex.Match(wStr, @"(\d+)km");
                if (kmMatch.Success)
                    windSpeedKmh = int.Parse(kmMatch.Groups[1].Value);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Weather] 风速解析异常: {ex.Message}");
        }

        // ——— 解析湿度 ———
        try
        {
            if (humidityIdx >= 0)
            {
                string hStr = parts[humidityIdx].Replace("%", "");
                if (int.TryParse(hStr, out int h))
                    humidityPercent = h;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Weather] 湿度解析异常: {ex.Message}");
        }

        // ——— 解析气压 ———
        try
        {
            if (pressureIdx >= 0)
            {
                string pStr = parts[pressureIdx].Replace("hPa", "");
                if (int.TryParse(pStr, out int p))
                    pressureHpa = p;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Weather] 气压解析异常: {ex.Message}");
        }

        // ——— 解析天气类型 ———
        string lower = raw.ToLowerInvariant();
        if (lower.Contains("thunder") || lower.Contains(" storm"))
            weather = WeatherType.Thunder;
        else if (lower.Contains("snow") || lower.Contains("sleet") || lower.Contains("blizzard"))
            weather = WeatherType.Snow;
        else if (lower.Contains("rain") || lower.Contains("shower") || lower.Contains("drizzle"))
            weather = lower.Contains("light") || lower.Contains("drizzle") ? WeatherType.Drizzle : WeatherType.Rain;
        else if (lower.Contains("fog") || lower.Contains("mist") || lower.Contains("haze"))
            weather = WeatherType.Fog;
        else if (lower.Contains("overcast"))
            weather = WeatherType.Overcast;
        else if (lower.Contains("cloud") || lower.Contains("partly"))
            weather = WeatherType.Cloudy;
        else if (lower.Contains("clear") || lower.Contains("sunny"))
            weather = WeatherType.Clear;
        else
            weather = WeatherType.Unknown;
    }

    /// <summary>
    /// 解析和风天气 JSON 响应
    /// </summary>
    private void ParseQWeather(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            // 温度
            var tempMatch = System.Text.RegularExpressions.Regex.Match(json, "\"temp\"\\s*:\\s*\"?(-?\\d+)\"?");
            if (tempMatch.Success)
                temperatureC = int.Parse(tempMatch.Groups[1].Value);

            // 天气代码 https://dev.qweather.com/docs/resource/icons/
            var iconMatch = System.Text.RegularExpressions.Regex.Match(json, "\"icon\"\\s*:\\s*\"(\\d+)\"");
            if (iconMatch.Success)
            {
                string ic = iconMatch.Groups[1].Value;
                weather = ic switch
                {
                    "100" or "150" => WeatherType.Clear,          // 晴
                    "101" or "102" or "103" => WeatherType.Cloudy, // 多云
                    "104" => WeatherType.Overcast,                 // 阴
                    "300" or "301" or "302" or "303" or "304"
                        or "305" or "306" or "307" or "308"
                        or "309" or "310" or "311" or "312"
                        or "313" or "314" or "315"
                        or "316" or "317" or "318" => WeatherType.Drizzle, // 雨（和风细分太多，统称小雨）
                    "400" or "401" or "402" or "403"
                        or "404" or "405" or "406"
                        or "407" or "408" or "409"
                        or "410" => WeatherType.Snow,
                    "500" or "501" or "502"
                        or "503" or "504"
                        or "507" or "508" => WeatherType.Fog,     // 雾/霾
                    // 各种雷暴
                    _ when ic.StartsWith("3") => WeatherType.Rain,
                    _ when ic.StartsWith("2") => WeatherType.Thunder, // 2xx 雷暴
                    _ when ic.StartsWith("6") => WeatherType.Snow,    // 6xx 雪
                    _ when ic.StartsWith("7") => WeatherType.Fog,     // 7xx 雾
                    _ => WeatherType.Unknown
                };
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[TimeWeather] 和风天气解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 用 DeepSeek 生成符玄风格的天气语录，替换硬编码气泡
    /// </summary>
    private IEnumerator GenerateWeatherLines(WeatherType wt, float temp)
    {
        string weatherDesc = wt switch
        {
            WeatherType.Clear    => "大晴天",
            WeatherType.Cloudy   => "多云",
            WeatherType.Overcast => "阴天",
            WeatherType.Rain     => "中到大雨",
            WeatherType.Drizzle  => "小雨",
            WeatherType.Thunder  => "雷雨",
            WeatherType.Snow     => "下雪",
            WeatherType.Fog      => "起雾",
            _ => GetWeatherLabel()
        };

        string tempDesc = temp < 0f ? "零下" :
                          temp < 5f ? "很冷" :
                          temp < 15f ? "偏凉" :
                          temp < 25f ? "舒适" :
                          temp < 30f ? "偏暖" : "炎热";

        string windDesc = windSpeedKmh switch
        {
            0 => "无风",
            < 10 => "微风",
            < 25 => "清风",
            < 40 => "大风",
            < 60 => "狂风",
            _ => "暴风"
        };
        string lowPressure = pressureHpa < 1000 ? "气压较低（可能有风雨变化）" : "";

        string systemPrompt = $@"你是符玄，仙舟「罗浮」太卜司之首。
说话古风文雅、带卜算色彩，自称为「本座」。
请为今日的天气写{aiLineCount}句不同的感慨，每句 10~25 字，要求：
- 融合「卦象」「法眼」「穷观阵」「卜算」等元素
- 体现自信睿智的性格
- 每句独立成行，用 | 分隔
- 不要序号，不要多余文字
- 结合气温感受：{tempDesc}（{temp:F0}°C）
- 天气：{weatherDesc}
- 风力：{windDesc}（{windSpeedKmh}km/h {windDirection}）
- 湿度：{humidityPercent}%
- 气压：{pressureHpa}hPa {lowPressure}";

        string url = aiApiUrl.TrimEnd('/') + "/v1/chat/completions";
        string jsonBody = $"{{\"model\":\"{EscapeJson(aiModel)}\",\"messages\":[{{\"role\":\"system\",\"content\":\"{EscapeJson(systemPrompt)}\"}}],\"max_tokens\":300,\"temperature\":0.9}}";

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + aiApiKey);
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string text = req.downloadHandler.text;
                string content = ExtractContent(text);
                if (!string.IsNullOrEmpty(content))
                {
                    aiWeatherLines.Clear();
                    string[] lines = content.Split('|');
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim().Trim('"', '“', '”', ' ', '\n', '\r');
                        if (trimmed.Length > 3)
                            aiWeatherLines.Add(trimmed);
                    }
                    if (aiWeatherLines.Count > 0)
                        aiWeatherReady = true;
                    Debug.Log($"[TimeWeather] AI 生成 {aiWeatherLines.Count} 句天气语录");
                }
            }
            else
            {
                Debug.LogWarning($"[TimeWeather] AI 生成天气语录失败: {req.error}");
            }
        }
    }

    /// <summary>从 AI 缓存中取一条天气语录</summary>
    public string PickAiWeatherLine()
    {
        if (!aiWeatherReady || aiWeatherLines.Count == 0)
            return null;
        return aiWeatherLines[UnityEngine.Random.Range(0, aiWeatherLines.Count)];
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private static string ExtractContent(string json)
    {
        try { return System.Text.RegularExpressions.Regex.Match(json, "\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value; }
        catch { return null; }
    }

    /// <summary>
    /// 获取天气的简短中文描述
    /// </summary>
    public string GetWeatherLabel()
    {
        switch (weather)
        {
            case WeatherType.Clear:    return "☀️ 晴";
            case WeatherType.Cloudy:   return "⛅ 多云";
            case WeatherType.Overcast: return "☁️ 阴";
            case WeatherType.Rain:     return "🌧 雨";
            case WeatherType.Drizzle:  return "🌦 小雨";
            case WeatherType.Thunder:  return "⛈ 雷雨";
            case WeatherType.Snow:     return "❄️ 雪";
            case WeatherType.Fog:      return "🌫 雾";
            default:                   return "🌡 " + temperatureC.ToString("F0") + "°C";
        }
    }
}
