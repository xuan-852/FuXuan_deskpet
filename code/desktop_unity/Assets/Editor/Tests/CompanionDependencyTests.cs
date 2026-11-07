using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class CompanionDependencyTests
{
    [Test]
    public void AutoChatDoesNotCreateMissingDependenciesOrScheduleGreetings()
    {
        var host = new GameObject("unassembled-auto-chat");
        host.SetActive(false);
        try
        {
            var component = host.AddComponent<AutoChat>();
            LogAssert.Expect(LogType.Warning, "[AutoChat] 缺少 ChatBubble/IdleChatGenerator，请由装配入口提供，已停用");
            typeof(AutoChat).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
            Assert.IsFalse(component.enabled);
            Assert.IsNull(host.GetComponent<ChatBubble>());
            Assert.IsNull(host.GetComponent<IdleChatGenerator>());
            Assert.IsFalse(component.IsInvoking());
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void SchedulerDoesNotCreateMissingBubble()
    {
        var host = new GameObject("unassembled-proactive-chat");
        host.SetActive(false);
        try
        {
            var component = host.AddComponent<ProactiveMessageScheduler>();
            LogAssert.Expect(LogType.Warning, "[ProactiveScheduler] 缺少 ChatBubble，请由装配入口提供，已停用");
            typeof(ProactiveMessageScheduler).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
            Assert.IsFalse(component.enabled);
            Assert.IsNull(host.GetComponent<ChatBubble>());
        }
        finally { Object.DestroyImmediate(host); }
    }
}
