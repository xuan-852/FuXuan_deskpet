using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public class ProductCompositionTests
{
    private GameObject _host;

    [SetUp]
    public void SetUp()
    {
        // Inactive composition tests do not run Awake/Start, read memory, open windows,
        // capture the desktop, or call a model. They verify assembly, not live readiness.
        _host = new GameObject("product-composition-test");
        _host.SetActive(false);
    }

    [TearDown]
    public void TearDown() { UnityEngine.Object.DestroyImmediate(_host); }

    [Test]
    public void FeatureAssemblyIncludesExistingProductFeaturesAndBindsTools()
    {
        ProductComposition.AttachFeatures(_host);
        Type[] expected = {
            typeof(DragHandler), typeof(AttentionReactionAdapter), typeof(HybridRenderer),
            typeof(RightPanel), typeof(TimeWeatherController), typeof(ReminderManager),
            typeof(ServerPollService), typeof(ChatManager), typeof(ToolCallInvoker),
            typeof(MotionAgent), typeof(DualModelValidator),
            typeof(AutoChat), typeof(ProactiveMessageScheduler), typeof(VisualHeartbeat),
            typeof(ChatBubble), typeof(IdleChatGenerator)
        };
        foreach (var type in expected)
            Assert.IsNotNull(_host.GetComponent(type), type.Name);
        Assert.AreSame(_host.GetComponent<ToolCallInvoker>(), _host.GetComponent<ChatManager>().toolInvoker);
    }

    [Test]
    public void RepeatedAssemblyReusesComponentsAndKeepsExplicitToolBinding()
    {
        var chat = _host.AddComponent<ChatManager>();
        var explicitTools = new GameObject("explicit-tools");
        explicitTools.SetActive(false);
        explicitTools.transform.SetParent(_host.transform);
        chat.toolInvoker = explicitTools.AddComponent<ToolCallInvoker>();

        ProductComposition.AttachFeatures(_host);
        var original = _host.GetComponents<Component>();
        ProductComposition.AttachFeatures(_host);

        CollectionAssert.AreEqual(original, _host.GetComponents<Component>());
        Assert.AreSame(explicitTools.GetComponent<ToolCallInvoker>(), chat.toolInvoker);
    }

    [Test]
    public void ChatServiceAssemblyReusesLocalChildrenEvenBeforeAwake()
    {
        ProductComposition.AttachChatServices(_host);
        var original = _host.GetComponentsInChildren<Component>(true).ToArray();
        ProductComposition.AttachChatServices(_host);
        CollectionAssert.AreEqual(original, _host.GetComponentsInChildren<Component>(true));
        Assert.IsNull(_host.GetComponentInChildren<DesktopPet>(true));
        Assert.IsNull(_host.GetComponentInChildren<MotionAgent>(true));
        foreach (Transform child in _host.transform)
            Assert.IsTrue(child.gameObject.activeSelf, "Service must activate with its host");
    }
}
