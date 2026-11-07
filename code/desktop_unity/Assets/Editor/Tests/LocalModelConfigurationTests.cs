using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

public class LocalModelConfigurationTests
{
    private string _url, _model, _chat, _modelEnv, _chatEnv, _message;
    private bool _ready;
    private string[] _readyModels;
    private static HashSet<string> ReadyModels => (HashSet<string>)typeof(LocalLLMClient)
        .GetField("ReadyModels", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    [SetUp]
    public void SetUp()
    {
        _url = LocalLLMClient.BaseUrl;
        _model = LocalLLMClient.ModelName;
        _chat = LocalLLMClient.ChatModelName;
        _ready = LocalLLMClient.IsReady;
        _message = LocalLLMClient.LastHealthMessage;
        _readyModels = new List<string>(ReadyModels).ToArray();
        _modelEnv = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_MODEL");
        _chatEnv = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_CHAT_MODEL");
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_MODEL", null);
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_CHAT_MODEL", null);
        LocalLLMClient.SetBaseUrl("http://configuration-test.invalid:11434/v1");
        ReadyModels.Clear();
        LocalLLMClient.SetModel("action-test");
        LocalLLMClient.SetChatModel("chat-test");
    }

    [TearDown]
    public void TearDown()
    {
        LocalLLMClient.SetBaseUrl(_url);
        LocalLLMClient.SetModel(_model);
        LocalLLMClient.SetChatModel(_chat);
        ReadyModels.Clear();
        foreach (string tag in _readyModels) ReadyModels.Add(tag);
        typeof(LocalLLMClient).GetProperty("IsReady").GetSetMethod(true).Invoke(null, new object[] { _ready });
        typeof(LocalLLMClient).GetProperty("LastHealthMessage").GetSetMethod(true).Invoke(null, new object[] { _message });
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_MODEL", _modelEnv);
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_CHAT_MODEL", _chatEnv);
    }

    [Test]
    public void PersistedSettingsApplyWithoutAnEnabledMotionAgent()
    {
        PetConfig.ApplyLocalModelSettings(new PetConfig.ConfigData {
            motionAgentEnabled = false, localApiUrl = "http://configured.invalid:11434/",
            localModel = "configured-action", chatLocalModel = "configured-chat"
        });
        Assert.AreEqual("http://configured.invalid:11434/v1", LocalLLMClient.BaseUrl);
        Assert.AreEqual("configured-action", LocalLLMClient.ModelName);
        Assert.AreEqual("configured-chat", LocalLLMClient.ChatModelName);
    }

    [Test]
    public void EnvironmentOverridesBothRolesAndChatOverrideWins()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_MODEL", "override-action");
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_CHAT_MODEL", "override-chat");
        PetConfig.ApplyLocalModelSettings(new PetConfig.ConfigData());
        Assert.AreEqual("override-action", LocalLLMClient.ModelName);
        Assert.AreEqual("override-chat", LocalLLMClient.ChatModelName);
    }

    [Test]
    public void GlobalEnvironmentOverrideAlsoAppliesToChatWhenNoChatOverrideExists()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_MODEL", "shared-override");
        PetConfig.ApplyLocalModelSettings(new PetConfig.ConfigData());
        Assert.AreEqual("shared-override", LocalLLMClient.ChatModelName);
    }

    [Test]
    public void ReapplyingSameConfigurationDoesNotEraseReadiness()
    {
        var config = new PetConfig.ConfigData { localApiUrl = LocalLLMClient.BaseUrl,
            localModel = "action-test", chatLocalModel = "chat-test" };
        ReadyModels.Add("action-test");
        ReadyModels.Add("chat-test");
        typeof(LocalLLMClient).GetProperty("IsReady").GetSetMethod(true).Invoke(null, new object[] { true });
        PetConfig.ApplyLocalModelSettings(config);
        Assert.IsTrue(LocalLLMClient.IsReady);
        Assert.IsTrue(LocalLLMClient.IsModelReady("chat-test"));
    }

    [Test]
    public void ChangingEndpointInvalidatesBothRoles()
    {
        ReadyModels.Add("action-test");
        ReadyModels.Add("chat-test");
        LocalLLMClient.SetBaseUrl("http://different.invalid:11434");
        Assert.IsFalse(LocalLLMClient.IsReady);
        Assert.IsFalse(LocalLLMClient.IsModelReady("action-test"));
        Assert.IsFalse(LocalLLMClient.IsModelReady("chat-test"));
    }

    [Test]
    public void SwitchingOneRoleDoesNotInvalidateOtherModelsOnSameEndpoint()
    {
        ReadyModels.Add("action-test");
        ReadyModels.Add("chat-test");
        ReadyModels.Add("other-ready");
        LocalLLMClient.SetChatModel("other-ready");
        Assert.IsTrue(LocalLLMClient.IsModelReady("action-test"));
        LocalLLMClient.SetModel("other-ready");
        Assert.IsTrue(LocalLLMClient.IsReady);
        Assert.IsTrue(LocalLLMClient.IsModelReady("chat-test"));
    }

    [Test]
    public void EmptySettersLeaveExistingSettingsAlone()
    {
        string url = LocalLLMClient.BaseUrl;
        LocalLLMClient.SetBaseUrl(null);
        LocalLLMClient.SetModel(" ");
        LocalLLMClient.SetChatModel(null);
        Assert.AreEqual(url, LocalLLMClient.BaseUrl);
        Assert.AreEqual("action-test", LocalLLMClient.ModelName);
        Assert.AreEqual("chat-test", LocalLLMClient.ChatModelName);
    }

    [Test]
    public void MarkUnreadyRemovesOnlyTheAuxiliaryModelReadiness()
    {
        ReadyModels.Add("action-test");
        ReadyModels.Add("chat-test");
        LocalLLMClient.MarkUnready();
        Assert.IsFalse(LocalLLMClient.IsModelReady("action-test"));
        Assert.IsTrue(LocalLLMClient.IsModelReady("chat-test"));
    }
}
