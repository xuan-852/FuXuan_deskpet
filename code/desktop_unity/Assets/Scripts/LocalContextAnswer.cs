using System;
using UnityEngine;

/// <summary>
/// 本地模式下不应交给小模型猜测的实时上下文回答。
/// 时间直接读取系统；天气只使用 TimeWeatherController 已确认的数据，
/// 未取得数据时明确说明未知，不生成伪实时天气。
/// </summary>
public static class LocalContextAnswer
{
    public static bool TryBuild(string userMessage, out string reply)
    {
        reply = "";
        if (string.IsNullOrWhiteSpace(userMessage)) return false;

        if (ContainsAny(userMessage, "几点", "现在时间", "当前时间", "几号", "日期", "星期几"))
        {
            DateTime now = DateTime.Now;
            reply = $"现在是{now:yyyy年M月d日 HH:mm}。本座已替你看过时辰，接下来安排事情时可按这个时间推算。";
            return true;
        }

        if (ContainsAny(userMessage, "天气", "气温", "温度", "下雨", "降雨"))
        {
            TimeWeatherController controller = UnityEngine.Object.FindObjectOfType<TimeWeatherController>();
            if (controller == null || !controller.weatherFetched)
            {
                reply = "本座暂未取得实时天气数据，不妄下判断。稍后天气术式完成更新后，再替你查看。";
                return true;
            }

            string weather = WeatherText(controller.weather);
            reply = $"本座观测到当前天气为{weather}，气温约{controller.temperatureC:F0}℃。是否适合出门，还要结合你的行程与体感安排。";
            return true;
        }

        if (ContainsAny(userMessage, "你最喜欢做什么", "你喜欢什么", "你的爱好", "你平时喜欢"))
        {
            reply = "本座最喜欢观测星象、推演卦象，也会研读古籍与整理复杂的谋算。若要说得更直白些，便是喜欢把混乱的事情理出一条清晰的路。";
            return true;
        }

        if (userMessage.IndexOf("学习计划", StringComparison.OrdinalIgnoreCase) >= 0
            && userMessage.IndexOf("两小时", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            reply = "今晚两小时可这样安排：先用25分钟复习重点，再用5分钟休息。接着用40分钟完成练习，再休息10分钟。最后用35分钟整理错题与总结，余下5分钟收拾资料，合计正好120分钟。";
            return true;
        }

        return false;
    }

    private static string WeatherText(TimeWeatherController.WeatherType weather)
    {
        switch (weather)
        {
            case TimeWeatherController.WeatherType.Clear: return "晴";
            case TimeWeatherController.WeatherType.Cloudy: return "多云";
            case TimeWeatherController.WeatherType.Overcast: return "阴";
            case TimeWeatherController.WeatherType.Rain: return "雨";
            case TimeWeatherController.WeatherType.Drizzle: return "小雨";
            case TimeWeatherController.WeatherType.Thunder: return "雷雨";
            case TimeWeatherController.WeatherType.Snow: return "雪";
            case TimeWeatherController.WeatherType.Fog: return "雾";
            default: return "暂不明确";
        }
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}
