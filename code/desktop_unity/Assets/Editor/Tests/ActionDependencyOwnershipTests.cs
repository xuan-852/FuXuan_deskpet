using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ActionDependencyOwnershipTests
{
    [Test]
    public void StartingStandaloneMotionAgentDoesNotCreateProductServices()
    {
        var host = new GameObject("motion-dependency-owner-test");
        host.SetActive(false);
        int before = Resources.FindObjectsOfTypeAll<DualModelValidator>().Length;
        int monitorsBefore = Resources.FindObjectsOfTypeAll<GpuLoadMonitor>().Length;
        try
        {
            var agent = host.AddComponent<MotionAgent>();
            typeof(MotionAgent).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<DualModelValidator>().Length);
            Assert.AreEqual(monitorsBefore, Resources.FindObjectsOfTypeAll<GpuLoadMonitor>().Length);
            Assert.IsNull(host.GetComponent<GpuLoadMonitor>());
            Assert.AreEqual(0, host.transform.childCount);

            // The product supplies these on the same host; resolution must bind them
            // even while inactive, without borrowing a different scene instance.
            var renderer = host.AddComponent<Live2DRenderer>();
            var chat = host.AddComponent<ChatManager>();
            var validator = host.AddComponent<DualModelValidator>();
            typeof(MotionAgent).GetMethod("ResolveComponents", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            Assert.AreSame(renderer, typeof(MotionAgent).GetField("_renderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(agent));
            Assert.AreSame(chat, typeof(MotionAgent).GetField("_chatManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(agent));
            Assert.AreSame(validator, typeof(MotionAgent).GetField("_dualValidator", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(agent));
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void ValidatorInitializationDoesNotCreateOrLoadMotionMemory()
    {
        var host = new GameObject("validator-dependency-owner-test");
        host.SetActive(false);
        int before = Resources.FindObjectsOfTypeAll<MotionMemoryManager>().Length;
        try
        {
            var validator = host.AddComponent<DualModelValidator>();
            typeof(DualModelValidator).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(validator, null);
            Assert.AreEqual(before, Resources.FindObjectsOfTypeAll<MotionMemoryManager>().Length);
            Assert.AreEqual(0, host.transform.childCount);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
