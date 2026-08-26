using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class OpenClawBridgeTests
{
    private static string BuildErrorJson(string error, bool isScanned = false)
    {
        MethodInfo method = typeof(OpenClawBridge).GetMethod(
            "ErrorJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "OpenClawBridge.ErrorJson 不应被删除或改为实例方法");
        return (string)method.Invoke(null, new object[] { error, isScanned });
    }

    [Test]
    public void ErrorJsonEscapesStructuredBridgeErrors()
    {
        const string error = "桥接失败: \"quote\"\n中文";

        JObject obj = JObject.Parse(BuildErrorJson(error));

        Assert.IsFalse(obj["success"]?.Value<bool>() ?? true);
        Assert.AreEqual(error, obj["error"]?.ToString());
    }

    [Test]
    public void ErrorJsonPreservesScannedPdfFlag()
    {
        JObject obj = JObject.Parse(BuildErrorJson("PDF 无文本层", true));

        Assert.IsTrue(obj["is_scanned"]?.Value<bool>() ?? false);
        Assert.AreEqual("PDF 无文本层", obj["error"]?.ToString());
    }
}
