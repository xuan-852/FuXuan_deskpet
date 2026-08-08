using NUnit.Framework;

/// <summary>
/// ChatManager.CleanDisplayText 清洗逻辑验证测试
/// 覆盖：markdown 语法（**粗体**、*斜体*、`代码`、# 标题、列表、引用）、
///       内嵌表情/动作标记残留、纯文本不变
/// </summary>
public class CleanDisplayTextTests
{
    [Test]
    public void Strip_BoldMarkdown()
    {
        Assert.AreEqual("符玄大人驾到，尔等退下！", ChatManager.CleanDisplayText("**符玄大人**驾到，尔等退下！"));
    }

    [Test]
    public void Strip_ItalicMarkdown()
    {
        Assert.AreEqual("本座今日心情不错，便饶你一命。", ChatManager.CleanDisplayText("本座今日*心情不错*，便饶你一命。"));
    }

    [Test]
    public void Strip_InlineCode()
    {
        Assert.AreEqual("去 C:\\Windows 看看，cmd 也行。", ChatManager.CleanDisplayText("去 `C:\\Windows` 看看，`cmd` 也行。"));
    }

    [Test]
    public void Strip_Heading()
    {
        Assert.AreEqual("标题\n正文内容", ChatManager.CleanDisplayText("# 标题\n正文内容"));
    }

    [Test]
    public void Strip_UnorderedList()
    {
        Assert.AreEqual("第一点\n第二点\n第三点", ChatManager.CleanDisplayText("- 第一点\n- 第二点\n- 第三点"));
    }

    [Test]
    public void Strip_OrderedList()
    {
        Assert.AreEqual("第一步\n第二步\n第三步", ChatManager.CleanDisplayText("1. 第一步\n2. 第二步\n3. 第三步"));
    }

    [Test]
    public void Strip_Blockquote()
    {
        Assert.AreEqual("引用内容\n普通内容", ChatManager.CleanDisplayText("> 引用内容\n普通内容"));
    }

    [Test]
    public void Strip_EmojiMarker()
    {
        Assert.AreEqual("今日运势极佳！", ChatManager.CleanDisplayText("【表情:开心】今日运势极佳！"));
    }

    [Test]
    public void Strip_Kaomoji()
    {
        // 颜文字兜底清理：模型违规输出颜文字 → 文本剥除（表情动作由 StripKaomoji 触发）
        Assert.AreEqual("本座泪目了。", ChatManager.CleanDisplayText("(T_T) 本座泪目了。"));
        Assert.AreEqual("心情大好", ChatManager.CleanDisplayText("(*^▽^*) 心情大好"));
        Assert.AreEqual("竟敢如此", ChatManager.CleanDisplayText("(╬▔皿▔) 竟敢如此"));
    }

    [Test]
    public void Strip_ActionMarker()
    {
        Assert.AreEqual("本座乏了。", ChatManager.CleanDisplayText("【动作:伸懒腰】本座乏了。"));
    }

    [Test]
    public void Strip_NaturalActionMarker()
    {
        Assert.AreEqual("本座乏了。", ChatManager.CleanDisplayText("（伸了个懒腰）本座乏了。"));
    }

    [Test]
    public void Strip_CombinedMarkers()
    {
        Assert.AreEqual("加粗与代码与删除与下划线", ChatManager.CleanDisplayText("**加粗**与`代码`与~~删除~~与__下划线__"));
    }

    [Test]
    public void Strip_CodeBlock()
    {
        Assert.AreEqual("代码块后的内容", ChatManager.CleanDisplayText("```python\nprint('hello')\n```\n代码块后的内容"));
    }

    [Test]
    public void Keep_PlainText()
    {
        Assert.AreEqual("普通文本没有任何标记", ChatManager.CleanDisplayText("普通文本没有任何标记"));
    }

    [Test]
    public void Strip_MultipleBold()
    {
        Assert.AreEqual("A和B以及C混合", ChatManager.CleanDisplayText("**A**和**B**以及`C`混合"));
    }

    [Test]
    public void Keep_NullOrEmpty()
    {
        Assert.AreEqual(null, ChatManager.CleanDisplayText(null));
        Assert.AreEqual("", ChatManager.CleanDisplayText(""));
    }
}
