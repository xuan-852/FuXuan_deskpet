using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 审批弹窗状态回归：回执失败必须恢复同一审批，成功且后台已清除时才关闭。
/// </summary>
public class RightPanelApprovalTests
{
    private GameObject _panelObject;
    private RightPanel _panel;
    private FieldInfo _pendingApprovalBackingField;
    private FieldInfo _dialogOpenField;
    private MethodInfo _applyResultMethod;

    [SetUp]
    public void SetUp()
    {
        _panelObject = new GameObject("RightPanelApprovalTest");
        _panel = _panelObject.AddComponent<RightPanel>();
        _pendingApprovalBackingField = typeof(OpenClawBridge).GetField(
            "<PendingApproval>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        _dialogOpenField = typeof(RightPanel).GetField(
            "_approvalDialogOpen", BindingFlags.NonPublic | BindingFlags.Instance);
        _applyResultMethod = typeof(RightPanel).GetMethod(
            "ApplyApprovalResolutionResult", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(_pendingApprovalBackingField);
        Assert.IsNotNull(_dialogOpenField);
        Assert.IsNotNull(_applyResultMethod);
        _pendingApprovalBackingField.SetValue(null, null);
        _dialogOpenField.SetValue(_panel, false);
    }

    [TearDown]
    public void TearDown()
    {
        _pendingApprovalBackingField?.SetValue(null, null);
        if (_panelObject != null) Object.DestroyImmediate(_panelObject);
    }

    [Test]
    public void 回执失败保留同一审批并恢复弹窗()
    {
        OpenClawBridge.InjectTestApproval("echo approval");
        var approval = OpenClawBridge.PendingApproval;

        _applyResultMethod.Invoke(_panel, new object[] { approval.approvalId, false });

        Assert.IsTrue(_panel.IsApprovalDialogOpen);
        Assert.AreSame(approval, OpenClawBridge.PendingApproval);
    }

    [Test]
    public void 回执成功且审批已清除时关闭弹窗()
    {
        OpenClawBridge.InjectTestApproval("echo approval");
        var approvalId = OpenClawBridge.PendingApproval.approvalId;
        _dialogOpenField.SetValue(_panel, true);
        _pendingApprovalBackingField.SetValue(null, null);

        _applyResultMethod.Invoke(_panel, new object[] { approvalId, true });

        Assert.IsFalse(_panel.IsApprovalDialogOpen);
    }
}
