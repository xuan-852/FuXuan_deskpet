using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Serialization;

public class MotionAgentStartupTests
{
    private GameObject _host;
    private MotionAgent _agent;

    [SetUp]
    public void SetUp()
    {
        _host = new GameObject("motion-agent-startup-test");
        _host.SetActive(false);
        _agent = _host.AddComponent<MotionAgent>();
    }

    [TearDown]
    public void TearDown() { UnityEngine.Object.DestroyImmediate(_host); }

    [Test]
    public void AutonomySettingDoesNotHideUnityComponentEnabled()
    {
        Assert.IsNull(typeof(MotionAgent).GetField("enabled", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        _agent.autonomousActionsEnabled = false;
        Assert.IsTrue(((Behaviour)_agent).enabled);
    }

    [Test]
    public void OldSerializedEnabledFieldMigratesWithoutChangingJsonConfigKey()
    {
        var field = typeof(MotionAgent).GetField("autonomousActionsEnabled");
        var migration = (FormerlySerializedAsAttribute)Attribute.GetCustomAttribute(field, typeof(FormerlySerializedAsAttribute));
        Assert.AreEqual("enabled", migration.oldName);
        Assert.IsNotNull(typeof(PetConfig.ConfigData).GetField("motionAgentEnabled"));
    }

    [Test]
    public void AgentNoLongerOwnsAStartupHealthOrWarmupRoutine()
    {
        Assert.IsNull(typeof(MotionAgent).GetMethod("DelayedHealthCheck", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Test]
    public void InactiveAgentNeverStartsAutonomousRoutines()
    {
        typeof(MotionAgent).GetMethod("EnsureAutonomyStarted", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_agent, null);
        Assert.IsFalse((bool)typeof(MotionAgent).GetField("_autonomyStarted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_agent));
    }

    [Test]
    public void ActiveAgentWithUserSettingOffNeverStartsAutonomousRoutines()
    {
        _agent.autonomousActionsEnabled = false;
        _host.SetActive(true);
        typeof(MotionAgent).GetMethod("EnsureAutonomyStarted", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_agent, null);
        Assert.IsFalse((bool)typeof(MotionAgent).GetField("_autonomyStarted", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_agent));
        Assert.IsTrue(_agent.enabled);
    }

    [Test]
    public void AgentDoesNotOverwriteSharedModelSettingsWhileInactive()
    {
        string model = LocalLLMClient.ModelName;
        string url = LocalLLMClient.BaseUrl;
        _agent.localModel = "must-not-be-applied";
        _agent.localApiUrl = "http://must-not-be-applied";
        typeof(MotionAgent).GetMethod("EnsureAutonomyStarted", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_agent, null);
        Assert.AreEqual(model, LocalLLMClient.ModelName);
        Assert.AreEqual(url, LocalLLMClient.BaseUrl);
    }

    [Test]
    public void UnityComponentDisableAlsoClosesAutonomyGate()
    {
        _host.SetActive(true);
        Assert.IsTrue(_agent.CanRunAutonomy);
        _agent.enabled = false;
        Assert.IsFalse(_agent.CanRunAutonomy);
        Assert.IsTrue(_agent.autonomousActionsEnabled);
    }

    [Test]
    public void LoadPauseDoesNotOverwriteUserAutonomySetting()
    {
        _agent.autonomousActionsEnabled = false;
        _agent.SetAutonomyPaused(true);
        _agent.SetAutonomyPaused(false);
        Assert.IsFalse(_agent.autonomousActionsEnabled);
        Assert.IsFalse(_agent.CanRunAutonomy);
    }
}
