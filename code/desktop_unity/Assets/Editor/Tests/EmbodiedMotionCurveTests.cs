using System;
using NUnit.Framework;

public class EmbodiedMotionCurveTests
{
    [Test] public void 线性片段按时间插值()
    {
        var curve = new EmbodiedMotionCurve("ParamA", new double[] { 0d, 0d, 0d, 1d, 20d }, 1.0);
        Assert.AreEqual(0f, curve.Evaluate(0.0), 1e-4);
        Assert.AreEqual(10f, curve.Evaluate(0.5), 1e-4);
        Assert.AreEqual(20f, curve.Evaluate(1.0), 1e-4);
    }

    [Test] public void 阶跃片段保持到端点后跳变()
    {
        var curve = new EmbodiedMotionCurve("ParamA", new double[] { 0d, 0d, 2d, 0.5d, 10d }, 1.0);
        Assert.AreEqual(0f, curve.Evaluate(0.25), 1e-4);
        Assert.AreEqual(10f, curve.Evaluate(0.5), 1e-4);
        Assert.AreEqual(10f, curve.Evaluate(1.0), 1e-4);
    }

    [Test] public void 贝塞尔片段在中点的值符合三次曲线()
    {
        // P0=(0,0) P1=(0.25,1) P2=(0.75,0) P3=(1,0)：x(0.5)=0.5，y(0.5)=0.375
        var curve = new EmbodiedMotionCurve("ParamA", new double[] { 0d, 0d, 1d, 0.25d, 1d, 0.75d, 0d, 1d, 0d }, 1.0);
        Assert.AreEqual(0f, curve.Evaluate(0.0), 1e-4);
        Assert.AreEqual(0.375f, curve.Evaluate(0.5), 1e-3);
        Assert.AreEqual(0f, curve.Evaluate(1.0), 1e-4);
    }

    [Test] public void 超出时长被钳制到端点()
    {
        var curve = new EmbodiedMotionCurve("ParamA", new double[] { 0d, 0d, 0d, 1d, 5d }, 1.0);
        Assert.AreEqual(5f, curve.Evaluate(2.0), 1e-4);
    }

    [Test] public void 非法输入被拒绝()
    {
        Assert.Throws<ArgumentException>(() => new EmbodiedMotionCurve("", new double[] { 0d, 0d }, 1.0));
        Assert.Throws<ArgumentException>(() => new EmbodiedMotionCurve("ParamA", new double[] { 0d }, 1.0));
    }
}
