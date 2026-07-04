using UnityEngine;

/// <summary>
/// 宠物渲染器接口 — 抽象 PNG 渲染和 Live2D 渲染的差异
/// </summary>
public interface IPetRenderer
{
    /// <summary>切换到拖拽姿势</summary>
    void ShowDragPose();

    /// <summary>切换到点击姿势</summary>
    /// <param name="screenPos">鼠标在屏幕上的位置（Unity 坐标，左下原点）</param>
    void ShowClickPose(Vector2 screenPos);

    /// <summary>切换到落地姿势</summary>
    void ShowLandPose();

    /// <summary>切换到行走姿势</summary>
    void ShowWalkPose();

    /// <summary>切换到停止姿势，锁定 duration 秒不被覆盖</summary>
    void ShowStopPose(float lockSeconds);

    /// <summary>每帧更新渲染器（位置更新等）</summary>
    void OnPetUpdate(int petX, int petY, int petWidth, int petHeight,
                     int petVx, int petVy, bool onGround, bool isDragging, bool isPaused);

    /// <summary>屏幕边缘碰撞反弹动画</summary>
    /// <param name="direction">碰撞方向：-1=左墙, 1=右墙</param>
    void ShowWallHitPose(int direction);

    /// <summary>设置鼠标眼睛跟随目标</summary>
    /// <param name="targetX">水平方向 -1~1，null=恢复默认</param>
    /// <param name="targetY">垂直方向 -1~1，null=恢复默认</param>
    void SetEyeTarget(float? targetX, float? targetY);
}
