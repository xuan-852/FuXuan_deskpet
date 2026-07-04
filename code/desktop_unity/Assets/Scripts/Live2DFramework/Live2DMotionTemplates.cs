using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Live2D 动作模板 — 纯语义化参数，与具体模型解耦
///
/// 所有方法仅使用语义化参数名（通过 Live2DParameterMapper 桥接），
/// 不出现任何 "ParamX" 字面量。
///
/// 换模型时只需：
///   1. 准备新模型的 param_map.json
///   2. mapper.LoadMappingFromJson(...)
///   3. 所有动作自动适配
///
/// 设计原则：
/// - 所有动作函数形如 UpdateXxx(float phase, out MotionOutput) 风格
/// - 动作常数放在对应方法顶部，集中调参
/// - 不依赖 Live2DRenderer、DesktopPet 等外部状态（由调用方传入）
/// </summary>
public static class Live2DMotionTemplates
{
    // ================================================================
    //  输出结构 — 每帧由动作函数填充
    // ================================================================
    public class MotionOutput
    {
        public Dictionary<string, float> values = new Dictionary<string, float>(32);

        public void Set(string semanticName, float value)
        {
            values[semanticName] = value;
        }

        /// <summary>批量设值</summary>
        public void SetBulk(params (string name, float value)[] bulk)
        {
            foreach (var (n, v) in bulk) values[n] = v;
        }

        /// <summary>合并另一个输出（后调用的覆盖前调用的同名键）</summary>
        public void MergeOver(MotionOutput other)
        {
            foreach (var kv in other.values)
                values[kv.Key] = kv.Value;
        }

        /// <summary>清空</summary>
        public void Clear() { values.Clear(); }

        /// <summary>应用到参数映射器</summary>
        public void ApplyTo(Live2DParameterMapper mapper)
        {
            foreach (var kv in values)
                mapper.Set(kv.Key, kv.Value);
        }
    }

    // ================================================================
    //  空闲基态 — 呼吸 + 身体微晃 + 头部微动 + 眼球自然微动
    // ================================================================
    public static class Idle
    {
        // -- 调参区 --
        const float BREATH_AMP = 0.4f;
        const float BODY_SWAY_X = 10f;
        const float BODY_SWAY_Y = 0.5f;
        const float BODY_SWAY_Z = 0.4f;
        const float HEAD_X = 0.6f;
        const float HEAD_Y = 0.4f;
        const float EYE_X = 3f;
        const float EYE_Y = 2f;

        const float BLINK_INTERVAL_MIN = 2f;
        const float BLINK_INTERVAL_MAX = 5f;
        const float BLINK_SPEED = 20f;
        const float BLINK_DURATION = 0.15f;

        /// <summary>更新空闲基态参数</summary>
        public static void Update(float breathPhase, float noiseTimeX, float noiseTimeY,
            ref BlinkState blink, MotionOutput output)
        {
            // 呼吸
            float breath = (Mathf.PerlinNoise(breathPhase, 0f) - 0.5f) * BREATH_AMP;
            output.Set("breath", breath);

            // 身体微晃
            output.Set("body_angle_x", (Mathf.PerlinNoise(noiseTimeX, 1f) - 0.5f) * BODY_SWAY_X);
            output.Set("body_angle_y", (Mathf.PerlinNoise(noiseTimeX, 2f) - 0.5f) * BODY_SWAY_Y);
            output.Set("body_angle_z", (Mathf.PerlinNoise(noiseTimeX, 3f) - 0.5f) * BODY_SWAY_Z);

            // 头部微动
            output.Set("head_angle_x", (Mathf.PerlinNoise(noiseTimeX, 4f) - 0.5f) * HEAD_X);
            output.Set("head_angle_y", (Mathf.PerlinNoise(noiseTimeX, 5f) - 0.5f) * HEAD_Y);

            // 眼球
            float eyeX = (Mathf.PerlinNoise(noiseTimeY, 6f) - 0.5f) * EYE_X;
            float eyeY = (Mathf.PerlinNoise(noiseTimeY, 7f) - 0.5f) * EYE_Y;
            output.Set("eye_ball_x", eyeX);
            output.Set("eye_ball_y", eyeY);

            // 眨眼
            UpdateBlink(ref blink, output);
        }

        /// <summary>眨眼状态机</summary>
        public struct BlinkState
        {
            public float timer;
            public float interval;
            public bool isBlinking;
            public float phase;
        }

        public static void InitBlink(ref BlinkState bs)
        {
            bs.timer = 0f;
            bs.interval = Random.Range(BLINK_INTERVAL_MIN, BLINK_INTERVAL_MAX);
            bs.isBlinking = false;
            bs.phase = 0f;
        }

        private static void UpdateBlink(ref BlinkState bs, MotionOutput output)
        {
            if (bs.isBlinking)
            {
                bs.phase += Time.deltaTime;
                float v = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(bs.phase * BLINK_SPEED)));
                output.Set("eye_l_open", v);
                output.Set("eye_r_open", v);
                if (bs.phase >= BLINK_DURATION)
                {
                    bs.isBlinking = false;
                    bs.phase = 0f;
                    bs.interval = Random.Range(BLINK_INTERVAL_MIN, BLINK_INTERVAL_MAX);
                    output.Set("eye_l_open", 1f);
                    output.Set("eye_r_open", 1f);
                }
            }
            else
            {
                bs.timer += Time.deltaTime;
                if (bs.timer >= bs.interval)
                {
                    bs.timer = 0f;
                    bs.isBlinking = true;
                }
            }
        }

        /// <summary>重置眼睛到睁开</summary>
        public static void ResetEyes(MotionOutput output)
        {
            output.Set("eye_l_open", 1f);
            output.Set("eye_r_open", 1f);
            output.Set("eye_l_smile", 0f);
            output.Set("eye_r_smile", 0f);
        }
    }

    // ================================================================
    //  眨眼快捷方式（独立于 Idle 使用，用于走路/拖拽等场景）
    // ================================================================
    public static class Blink
    {
        public static void Update(ref Idle.BlinkState bs, MotionOutput output)
        {
            if (bs.isBlinking)
            {
                bs.phase += Time.deltaTime;
                float v = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(bs.phase * 20f)));
                output.Set("eye_l_open", v);
                output.Set("eye_r_open", v);
                if (bs.phase >= 0.15f)
                {
                    bs.isBlinking = false;
                    bs.phase = 0f;
                    output.Set("eye_l_open", 1f);
                    output.Set("eye_r_open", 1f);
                }
            }
            else
            {
                bs.timer += Time.deltaTime;
                if (bs.timer >= bs.interval)
                {
                    bs.timer = 0f;
                    bs.isBlinking = true;
                    bs.interval = Random.Range(2f, 5f);
                }
            }
        }
    }

    // ================================================================
    //  走路动画 — 横版过关风格（转体侧面 + 腿/臂交替 + 颠簸）
    // ================================================================
    public static class Walk
    {
        // -- 调参区 --
        const float SIDE_ANGLE = 18f;       // 身体Y轴转体
        const float SWAY_FREQ = 5f;         // 步频
        const float BOUNCE_PX = 4f;         // 上下颠簸
        const float BODY_LEAN = 5f;         // 身体前倾
        const float HEAD_TILT = 8f;         // 低头看路
        const float LEG_LIFT = 4f;          // 抬腿
        const float LEG_SWING = 6f;         // 腿前后摆
        const float LEG_BEND = 6f;          // 腿弯曲
        const float ARM_BIG = 2f;           // 手臂大范围（左臂主驱动）
        const float ARM_SMALL = 0.4f;       // 手臂小范围（右臂关节）
        const float BODY_SWING = 2f;        // 身体Z横摆
        const float SHOULDER = 1.5f;        // 耸肩
        const float BREATH_VAL = 3f;        // 呼吸加深

        /// <summary>更新走路动画（所有值已乘 weight 做淡入淡出）</summary>
        public static void Update(float walkPhase, float weight, MotionOutput output)
        {
            if (weight <= 0f) return;

            float phase = walkPhase;
            float legPhase = Mathf.Sin(phase);
            float rightPhase = -legPhase;

            // 抬腿
            output.Set("leg_l_lift", legPhase * LEG_LIFT * weight);
            output.Set("leg_r_lift", rightPhase * LEG_LIFT * weight);

            // 前后摆 + 弯曲
            output.Set("leg_l_swing", legPhase * LEG_SWING * weight);
            output.Set("leg_l_bend", Mathf.Abs(legPhase) * LEG_BEND * weight);
            output.Set("leg_r_swing", rightPhase * LEG_SWING * weight);
            output.Set("leg_r_bend", Mathf.Abs(rightPhase) * LEG_BEND * weight);

            // 右臂（与左腿同步）
            output.Set("arm_right_rotation", legPhase * ARM_BIG * weight);
            output.Set("arm_right_upper", legPhase * ARM_SMALL * 0.7f * weight);
            output.Set("arm_right_mid", legPhase * ARM_SMALL * 0.4f * weight);
            output.Set("arm_right_lower", legPhase * ARM_SMALL * 0.4f * weight);

            // 左臂（与右腿同步）
            float leftArm = rightPhase * ARM_SMALL * weight;
            output.Set("arm_left_upper", leftArm * 0.7f);
            output.Set("arm_left_mid", leftArm * 0.4f);
            output.Set("arm_left_lower", leftArm * 0.4f);

            // 肩膀
            output.Set("shoulder", Mathf.Abs(legPhase) * SHOULDER * weight);
        }

        /// <summary>更新走路体态姿势（在 Update 中调，给物理系统提前输入）</summary>
        public static void UpdateBodyPose(float walkPhase, float weight, MotionOutput output)
        {
            if (weight <= 0f) return;

            float phase = walkPhase;
            output.Set("body_angle_y", (SIDE_ANGLE + Mathf.Sin(phase) * 3f) * weight);
            output.Set("body_angle_x", BODY_LEAN * weight);
            output.Set("body_angle_z", Mathf.Sin(phase) * BODY_SWING * weight);
            output.Set("head_angle_x", SIDE_ANGLE * weight);
            output.Set("head_angle_y", HEAD_TILT * weight);
            output.Set("breath", (BREATH_VAL + Mathf.Sin(phase) * 0.5f) * weight);
        }

        /// <summary>计算颠簸偏移量（像素）</summary>
        public static float GetBounceOffset(float walkPhase)
        {
            return (1f - Mathf.Abs(Mathf.Sin(walkPhase))) * BOUNCE_PX;
        }
    }

    // ================================================================
    //  拖拽挣扎 — 手脚交替划水 + 身体扭动 + 慌张表情
    // ================================================================
    public static class DragStruggle
    {
        // -- 调参区 --
        const float ARM_FREQ = 4.5f;
        const float RIGHT_AMP = 3f;
        const float LEFT_AMP = 0.1f;
        const float JITTER1_FREQ = 2f;
        const float JITTER1_AMP = 0.2f;
        const float JITTER2_FREQ = 1f;
        const float JITTER2_AMP = 0.4f;

        const float LEG_FREQ = 5f;
        const float LEG_SWING = 12f;
        const float LEG_BEND = 6f;
        const float LEG_LIFT = 8f;

        const float TURN_ANGLE = 10f;
        const float TURN_SMOOTH = 0.1f;
        const float BODY_SWAY = 5f;
        const float BODY_FREQ = 2f;

        const float VEL_LERP = 0.01f;
        const float VEL_MAX = 3f;
        const float BODY_Z_SCALE = 3f;
        const float BODY_Z_MAX = 12f;
        const float HEAD_X_SCALE = 1.8f;
        const float HEAD_X_MAX = 22f;
        const float HEAD_Z_SCALE = 1.2f;
        const float HEAD_Z_MAX = 16f;
        const float HEAD_SHAKE = 5f;
        const float HEAD_SHAKE_FREQ = 3.5f;
        const float HEAD_TILT = -2f;
        const float HEAD_BOB = 1f;
        const float HEAD_BOB_FREQ = 2f;

        const float EYE_OPEN = 1.1f;
        const float MOUTH_AMP = 0.5f;
        const float MOUTH_PULSE = 0.3f;
        const float MOUTH_FREQ = 5f;
        const float MOUTH_PHASE = 1f;
        const float BROW = 1.2f;

        /// <summary>拖拽状态（由调用方维护）</summary>
        public struct DragState
        {
            public float smoothBodyY;
            public float smoothBodyZ;
            public float smoothHeadX;
            public float smoothHeadZ;
            public int lastPetX;
            public bool inited;
        }

        /// <summary>更新拖拽挣扎动画</summary>
        public static void Update(float time, DragState state, int petX, int petVx,
            MotionOutput output)
        {
            // 手臂交替
            float phase = time * ARM_FREQ;
            float swing = Mathf.Sin(phase);
            float jitter = Mathf.Sin(time * JITTER1_FREQ) * JITTER1_AMP
                         + Mathf.Sin(time * JITTER2_FREQ) * JITTER2_AMP;
            float rightBase = swing * RIGHT_AMP * (1f + jitter);
            float leftBase = swing * LEFT_AMP;
            float rMag = Mathf.Clamp01((rightBase + RIGHT_AMP) / (RIGHT_AMP * 2f));

            output.Set("arm_right_rotation", rightBase * 1f);      // DRAG_RPARAM94
            output.Set("arm_right_base_rotation", rightBase * 0.2f);
            output.Set("arm_right_upper", rightBase * 0.25f);
            output.Set("arm_right_mid", rightBase * 0.1f);
            output.Set("arm_right_lower", rightBase * 0.2f);
            output.Set("arm_right_switch", rMag * 0f);
            output.Set("arm_right_reach", rMag * 0.6f);

            // 手部图层
            output.Set("hand_layer_95", rMag * 0.8f);
            output.Set("hand_layer_117", rMag * 0.5f);
            output.Set("hand_layer_98", rMag * 0.6f);
            output.Set("hand_layer_100", rMag * 0.6f);
            output.Set("hand_layer_116", rMag * 0.4f);
            output.Set("hand_layer_120", rMag * 0.8f);
            output.Set("hand_layer_108", rMag * 0.8f);
            output.Set("hand_layer_119", rMag * 0.8f);

            output.Set("arm_left_upper", leftBase * 0.1f);
            output.Set("arm_left_mid", leftBase * 0.1f);
            output.Set("arm_left_lower", leftBase * 0.1f);

            // 双腿
            float legPhase = time * LEG_FREQ;
            float legSwing = Mathf.Sin(legPhase);
            float rightLeg = -legSwing;
            output.Set("leg_l_swing", legSwing * LEG_SWING);
            output.Set("leg_l_bend", Mathf.Abs(legSwing) * LEG_BEND);
            output.Set("leg_r_swing", rightLeg * LEG_SWING);
            output.Set("leg_r_bend", Mathf.Abs(rightLeg) * LEG_BEND);
            output.Set("leg_l_lift", legSwing * LEG_LIFT);
            output.Set("leg_r_lift", rightLeg * LEG_LIFT);

            // 身体转向
            float targetY = petVx > 0 ? TURN_ANGLE : -TURN_ANGLE;
            float smoothY = Mathf.Lerp(state.smoothBodyY, targetY, TURN_SMOOTH);
            output.Set("body_angle_y", smoothY);

            // 速度驱动
            float rawVel = petX - state.lastPetX;
            rawVel = Mathf.Clamp(rawVel, -VEL_MAX, VEL_MAX);
            float v = Mathf.Lerp(state.smoothBodyZ, rawVel, VEL_LERP);

            float bodyZ = Mathf.Clamp(-v * BODY_Z_SCALE, -BODY_Z_MAX, BODY_Z_MAX);
            output.Set("body_angle_z", bodyZ);

            float bodyX = Mathf.Sin(time * BODY_FREQ) * BODY_SWAY;
            output.Set("body_angle_x", bodyX);

            // 头
            float headX = Mathf.Clamp(-v * HEAD_X_SCALE, -HEAD_X_MAX, HEAD_X_MAX);
            output.Set("head_angle_x", headX + Mathf.Sin(time * HEAD_SHAKE_FREQ) * HEAD_SHAKE);
            output.Set("head_angle_z", Mathf.Clamp(v * HEAD_Z_SCALE, -HEAD_Z_MAX, HEAD_Z_MAX));
            output.Set("head_angle_y", HEAD_TILT + Mathf.Sin(time * HEAD_BOB_FREQ) * HEAD_BOB);

            // 表情
            output.Set("eye_l_open", EYE_OPEN);
            output.Set("eye_r_open", EYE_OPEN);
            output.Set("mouth_open_y", MOUTH_AMP + Mathf.Sin(time * MOUTH_FREQ + MOUTH_PHASE) * MOUTH_PULSE);
            output.Set("brow_l_angle", BROW);
            output.Set("brow_r_angle", BROW);
        }
    }

    // ================================================================
    //  点击反应 — 按点击部位分三种
    // ================================================================
    public static class ClickReaction
    {
        // -- 调参区 --
        const float CLICK_EYE_OPEN = 0.3f;
        const float CLICK_BODY_X = -5f;
        const float CLICK_HEAD_X = 8f;

        const float POKE_EYE_OPEN = 1.3f;
        const float POKE_MOUTH_OPEN = 0.8f;
        const float POKE_MOUTH_FORM = 0.5f;
        const float POKE_BROW_RAISE = 10f;
        const float POKE_HEAD_X = 4f;

        const float LEG_ANGLE_Z = -6f;
        const float LEG_SMILE = 0.6f;
        const float LEG_EYE_CLOSE = 0.3f;
        const float LEG_BODY_X = -2.5f;
        const float LEG_HEAD_X = 2.4f;

        public enum HitZone { Head, Body, Leg }

        public static void Update(HitZone zone, MotionOutput output)
        {
            switch (zone)
            {
                case HitZone.Head:
                    output.Set("eye_l_open", CLICK_EYE_OPEN);
                    output.Set("eye_r_open", CLICK_EYE_OPEN);
                    output.Set("head_angle_x", CLICK_HEAD_X);
                    output.Set("body_angle_x", CLICK_BODY_X);
                    break;

                case HitZone.Body:
                    output.Set("eye_l_open", POKE_EYE_OPEN);
                    output.Set("eye_r_open", POKE_EYE_OPEN);
                    output.Set("mouth_open_y", POKE_MOUTH_OPEN);
                    output.Set("mouth_form", POKE_MOUTH_FORM);
                    output.Set("brow_l_y", POKE_BROW_RAISE);
                    output.Set("brow_r_y", POKE_BROW_RAISE);
                    output.Set("head_angle_x", POKE_HEAD_X);
                    break;

                case HitZone.Leg:
                    output.Set("eye_l_open", LEG_EYE_CLOSE);
                    output.Set("eye_r_open", LEG_EYE_CLOSE);
                    output.Set("eye_l_smile", LEG_SMILE);
                    output.Set("eye_r_smile", LEG_SMILE);
                    output.Set("head_angle_z", LEG_ANGLE_Z);
                    output.Set("head_angle_x", LEG_HEAD_X);
                    output.Set("body_angle_x", LEG_BODY_X);
                    break;
            }
        }
    }

    // ================================================================
    //  屏幕边缘碰撞反弹
    // ================================================================
    public static class WallHit
    {
        const float DURATION = 0.5f;
        const float EYE_OPEN = 1.3f;
        const float MOUTH_OPEN = 0.5f;
        const float BODY_LEAN = 8f;

        /// <summary>更新反弹（progress: 1→0 随时间递减）</summary>
        public static void Update(float progress, int direction, MotionOutput output)
        {
            float t = Mathf.Clamp01(progress);

            float eyeOpen = Mathf.Lerp(EYE_OPEN, 1f, t);
            output.Set("eye_l_open", eyeOpen);
            output.Set("eye_r_open", eyeOpen);

            float mouthOpen = Mathf.Lerp(MOUTH_OPEN, 0f, t * t);
            output.Set("mouth_open_y", mouthOpen);

            float bodyLean = Mathf.Lerp(BODY_LEAN, 0f, t * 2f);
            output.Set("body_angle_x", (direction > 0) ? -bodyLean : bodyLean);

            output.Set("head_angle_y", Mathf.Lerp(3f, 0f, t));
        }

        public static float GetDuration() => DURATION;
    }

    // ================================================================
    //  下落
    // ================================================================
    public static class Fall
    {
        const float BODY_X = -3f;
        const float HEAD_X = -5f;

        public static void Update(MotionOutput output)
        {
            output.Set("body_angle_x", BODY_X);
            output.Set("head_angle_x", HEAD_X);
            output.Set("breath", 0f);
        }
    }

    // ================================================================
    //  鼠标眼睛跟随
    // ================================================================
    public static class EyeFollow
    {
        const float MAX_DIST = 150f;
        const float MAX_X = 10f;
        const float MAX_Y = 8f;
        const float SMOOTH_SPEED = 0.08f;
        const float RETURN_SPEED = 0.04f;
        const float RETURN_THRESHOLD = 0.1f;

        /// <summary>眼睛跟随状态</summary>
        public struct EyeState
        {
            public float smoothX;
            public float smoothY;
            public bool active;
        }

        /// <summary>更新眼睛跟随</summary>
        public static void Update(ref EyeState state, float? targetX, float? targetY, MotionOutput output)
        {
            if (targetX.HasValue || targetY.HasValue)
            {
                float rawX = (targetX ?? 0f) * MAX_X;
                float rawY = (targetY ?? 0f) * MAX_Y;
                state.smoothX = Mathf.Lerp(state.smoothX, rawX, SMOOTH_SPEED);
                state.smoothY = Mathf.Lerp(state.smoothY, rawY, SMOOTH_SPEED);
                state.active = true;
                output.Set("eye_ball_x", state.smoothX);
                output.Set("eye_ball_y", state.smoothY);
            }
            else if (state.active)
            {
                state.smoothX = Mathf.Lerp(state.smoothX, 0f, RETURN_SPEED);
                state.smoothY = Mathf.Lerp(state.smoothY, 0f, RETURN_SPEED);
                output.Set("eye_ball_x", state.smoothX);
                output.Set("eye_ball_y", state.smoothY);
                if (Mathf.Abs(state.smoothX) < RETURN_THRESHOLD && Mathf.Abs(state.smoothY) < RETURN_THRESHOLD)
                    state.active = false;
            }
        }
    }

    // ================================================================
    //  空闲特化动作 (1-11)
    // ================================================================
    public static class IdleActions
    {
        // ================================================================
        //  动作1: 歪头
        // ================================================================
        public static class Action1_Tilt
        {
            const float DURATION = 2f;
            const float TILT = 8f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Sin(elapsed / DURATION * Mathf.PI);
                output.Set("head_angle_z", t * TILT);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("head_angle_z", 0f);
            }
        }

        // ================================================================
        //  动作2: 微笑
        // ================================================================
        public static class Action2_Smile
        {
            const float DURATION = 2f;
            const float SMILE = 0.6f;
            const float MOUTH = 0.4f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Sin(elapsed / DURATION * Mathf.PI);
                output.Set("eye_l_smile", t * SMILE);
                output.Set("eye_r_smile", t * SMILE);
                output.Set("mouth_form", t * MOUTH);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("eye_l_smile", 0f);
                output.Set("eye_r_smile", 0f);
                output.Set("mouth_form", 0f);
            }
        }

        // ================================================================
        //  动作3: 挑眉
        // ================================================================
        public static class Action3_Brow
        {
            const float DURATION = 2f;
            const float BROW_Y = 6f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Sin(elapsed / DURATION * Mathf.PI);
                output.Set("brow_r_y", t * BROW_Y);
                output.Set("brow_l_y", t * BROW_Y);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("brow_r_y", 0f);
                output.Set("brow_l_y", 0f);
            }
        }

        // ================================================================
        //  动作4: 星辉环绕（仅保留时长）
        // ================================================================
        public static class Action4_StarSpin
        {
            const float DURATION = 6f;
            public static float Duration => DURATION;
            public static void Update(float elapsed, MotionOutput output) { }
            public static void Cleanup(MotionOutput output) { }
        }

        // ================================================================
        //  动作5: 伸懒腰
        // ================================================================
        public static class Action5_Stretch
        {
            const float DURATION = 4.5f;
            const float BODY_BACK = -5f;
            const float MOUTH_OPEN = 0.6f;
            const float EYE_CLOSE = 0.4f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);
                float rise = Mathf.Clamp01(t * 3f);
                float hold = Mathf.Clamp01((1f - t) * 3f);
                float phase = Mathf.Min(rise, hold);

                // 右臂全套
                output.Set("arm_right_upper", phase * 4f);
                output.Set("arm_right_mid", phase * 3f);
                output.Set("arm_right_lower", phase * 5f);
                output.Set("arm_right_rotation", phase * 10f);
                output.Set("arm_right_base_rotation", phase * 3f);
                output.Set("arm_right_switch", phase);
                output.Set("arm_right_reach", phase * 0.6f);
                output.Set("arm_right_wrist_z", phase * 0f);

                output.Set("hand_layer_95", phase * 0.8f);
                output.Set("hand_layer_117", phase * 0.5f);
                output.Set("hand_layer_98", phase * 0.6f);
                output.Set("hand_layer_100", phase * 0.6f);
                output.Set("hand_layer_116", phase * 0.4f);
                output.Set("hand_layer_120", phase * 0.8f);
                output.Set("hand_layer_108", phase * 0.8f);
                output.Set("hand_layer_119", phase * 0.8f);

                output.Set("arm_left_upper", -phase * 3f);
                output.Set("arm_left_mid", -phase * 2f);
                output.Set("arm_left_lower", -phase * 1.5f);

                output.Set("body_angle_x", phase * BODY_BACK);
                output.Set("body_angle_z", phase * 3f);
                output.Set("head_angle_x", phase * (-8f));

                output.Set("eye_l_open", 1f - phase * EYE_CLOSE);
                output.Set("eye_r_open", 1f - phase * EYE_CLOSE);
                output.Set("mouth_form", phase * MOUTH_OPEN);
                output.Set("breath", phase * 0.5f);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("arm_right_upper", 0f);
                output.Set("arm_right_mid", 0f);
                output.Set("arm_right_lower", 0f);
                output.Set("arm_right_rotation", 0f);
                output.Set("arm_right_base_rotation", 0f);
                output.Set("arm_right_switch", 0f);
                output.Set("arm_right_reach", 0f);
                output.Set("arm_right_wrist_z", 0f);
                output.Set("hand_layer_95", 0f);
                output.Set("hand_layer_117", 0f);
                output.Set("hand_layer_98", 0f);
                output.Set("hand_layer_100", 0f);
                output.Set("hand_layer_116", 0f);
                output.Set("hand_layer_120", 0f);
                output.Set("hand_layer_108", 0f);
                output.Set("hand_layer_119", 0f);
                output.Set("arm_left_upper", 0f);
                output.Set("arm_left_mid", 0f);
                output.Set("arm_left_lower", 0f);
                output.Set("body_angle_x", 0f);
                output.Set("body_angle_z", 0f);
                output.Set("head_angle_x", 0f);
                output.Set("eye_l_open", 1f);
                output.Set("eye_r_open", 1f);
                output.Set("mouth_form", 0f);
                output.Set("breath", 0f);
            }
        }

        // ================================================================
        //  动作7: 数钱钱（已删除）
        // ================================================================
        public static class Action7_Money
        {
            const float DURATION = 3.5f;
            const float TILT = 10f;
            const float SWAY = 4f;
            const float SMILE = 0.6f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);
                float eased = Mathf.Sin(t * Mathf.PI);

                output.Set("special_money", eased);
                output.Set("eye_l_smile", eased * SMILE);
                output.Set("eye_r_smile", eased * SMILE);
                output.Set("head_angle_z", Mathf.Sin(elapsed * 1.5f) * eased * TILT);

                float sway = Mathf.Sin(elapsed * 2f) * eased * SWAY;
                output.Set("body_angle_z", sway);
                output.Set("body_angle_x", sway * 0.3f);

                output.Set("eye_l_open", 1f - eased * 0.15f);
                output.Set("eye_r_open", 1f - eased * 0.15f);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("special_money", 0f);
                output.Set("eye_l_smile", 0f);
                output.Set("eye_r_smile", 0f);
                output.Set("head_angle_z", 0f);
                output.Set("body_angle_z", 0f);
                output.Set("body_angle_x", 0f);
                output.Set("eye_l_open", 1f);
                output.Set("eye_r_open", 1f);
            }
        }

        // ================================================================
        //  动作8: 委屈
        // ================================================================
        public static class Action8_Cry
        {
            const float DURATION = 3.5f;
            const float HEAD_DOWN = 6f;
            const float MOUTH_TREM = 0.3f;
            const float BROW_UP = 5f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);
                float eased = Mathf.Sin(t * Mathf.PI);

                float tear = Mathf.Sin(elapsed * 4f) * eased;
                output.Set("special_tear", Mathf.Clamp01(tear));
                output.Set("head_angle_y", eased * HEAD_DOWN);

                float mouthTrem = Mathf.Sin(elapsed * 6f) * eased * MOUTH_TREM;
                output.Set("mouth_form", Mathf.Abs(mouthTrem));
                output.Set("brow_r_y", eased * BROW_UP);
                output.Set("brow_l_y", eased * BROW_UP);
                output.Set("eye_l_open", 1f + eased * 0.15f);
                output.Set("eye_r_open", 1f + eased * 0.15f);

                float sob = Mathf.Sin(elapsed * 3.5f) * eased * 1.5f;
                output.Set("body_angle_x", sob);
                output.Set("body_angle_z", sob * 0.5f);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("special_tear", 0f);
                output.Set("head_angle_y", 0f);
                output.Set("mouth_form", 0f);
                output.Set("brow_r_y", 0f);
                output.Set("brow_l_y", 0f);
                output.Set("eye_l_open", 1f);
                output.Set("eye_r_open", 1f);
                output.Set("body_angle_x", 0f);
                output.Set("body_angle_z", 0f);
            }
        }

        // ================================================================
        //  动作9: 法阵显现
        // ================================================================
        public static class Action9_MagicCircle
        {
            const float DURATION = 8f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);

                // 身体姿态
                output.Set("body_angle_x", -8f);
                output.Set("head_angle_x", -10f);
                output.Set("body_angle_z", 0f);

                // Param92 全程保持剑指模式
                output.Set("sword_finger_switch", 1f);

                if (t < 0.20f)
                {
                    float h = EaseInCubic(t / 0.20f);
                    SetHandPose(h, output);
                    SetSwordFinger(h, output);
                    if (h > 0.01f) SetHandLayer(h, output);
                }
                else if (t < 0.45f)
                {
                    SetHandPose(1f, output);
                    SetSwordFinger(1f, output);
                    SetHandLayer(1f, output);
                }
                else if (t < 0.48f)
                {
                    SetHandPose(1f, output);
                    SetSwordFinger(1f, output);
                    SetHandLayer(1f, output);
                }
                else if (t < 0.75f)
                {
                    float h = 1f - EaseOutQuad((t - 0.48f) / 0.27f);
                    SetHandPose(h, output);
                    SetSwordFinger(h, output);
                    SetHandLayer(h, output);
                }
                else
                {
                    float phase5 = (t - 0.75f) / 0.25f;
                    float fade = 1f - EaseOutQuad(phase5);
                    output.Set("body_angle_x", fade * -8f);
                    output.Set("head_angle_x", fade * -10f);
                    SetHandLayer(fade, output);
                    output.Set("sword_finger_switch", fade);
                }
            }

            static void SetHandPose(float h, MotionOutput output)
            {
                output.Set("arm_right_rotation", h * -4.84f);
                output.Set("arm_right_base_rotation", h * -27.42f);
                output.Set("arm_right_switch", h * 1f);
                output.Set("arm_right_reach", h * -0.32f);
                output.Set("arm_right_wrist_z", h * -18.71f);
                output.Set("arm_right_upper", h * -8f);
                output.Set("arm_right_mid", h * -6f);
                output.Set("arm_right_lower", h * -10f);
                output.Set("arm_left_upper", 0f);
                output.Set("arm_left_mid", 0f);
                output.Set("arm_left_lower", 0f);
            }

            static void SetSwordFinger(float h, MotionOutput output)
            {
                output.Set("finger_normal_1", 0f);
                output.Set("finger_normal_2", 0f);
                output.Set("finger_normal_3", 0f);
                output.Set("finger_normal_4", 0f);
                output.Set("finger_normal_5", 0f);
                output.Set("finger_thumb", h * 0.2f);
                output.Set("finger_index", h * 1f);
                output.Set("finger_middle", h * 1f);
                output.Set("finger_ring", h * 0.2f);
                output.Set("finger_pinky", h * 0.2f);
                output.Set("finger_z_rotate", h * -0.5f);
            }

            static void SetHandLayer(float layer, MotionOutput output)
            {
                output.Set("hand_layer_95", layer * 1f);
                output.Set("hand_layer_117", layer * 0.8f);
                output.Set("hand_layer_98", layer * 0.8f);
                output.Set("hand_layer_100", layer * 0.8f);
                output.Set("hand_layer_116", layer * 0.6f);
                output.Set("hand_layer_120", layer * 1f);
                output.Set("hand_layer_108", layer * 1f);
                output.Set("hand_layer_119", layer * 1f);
            }

            public static void Cleanup(MotionOutput output)
            {
                var mo = new MotionOutput();
                SetHandPose(0f, mo);
                SetSwordFinger(0f, mo);
                SetHandLayer(0f, mo);
                mo.Set("sword_finger_switch", 0f);
                mo.Set("body_angle_x", 0f);
                mo.Set("head_angle_x", 0f);
                mo.Set("body_angle_z", 0f);
                output.MergeOver(mo);
            }
        }

        // ================================================================
        //  动作10: 害羞黑脸
        // ================================================================
        public static class Action10_Blush
        {
            const float DURATION = 3.5f;
            const float DARK = 1f;
            const float LOOK_AWAY = -8f;
            const float SMILE = 0.5f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);
                float eased = Mathf.Sin(t * Mathf.PI);

                float darkPulse = (Mathf.Sin(elapsed * 3f) + 1f) * 0.5f * eased;
                output.Set("special_blush_dark", darkPulse * DARK);
                output.Set("head_angle_y", eased * 4f);
                output.Set("head_angle_z", Mathf.Sin(elapsed * 1.5f) * eased * LOOK_AWAY);
                output.Set("eye_l_smile", eased * SMILE);
                output.Set("eye_r_smile", eased * SMILE);
                output.Set("eye_l_open", 1f - eased * 0.2f);
                output.Set("eye_r_open", 1f - eased * 0.2f);

                float sway = Mathf.Sin(elapsed * 2.5f) * eased * 3f;
                output.Set("body_angle_x", sway);
                output.Set("body_angle_z", sway * 0.5f);

                output.Set("mouth_form", eased * 0.3f);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("special_blush_dark", 0f);
                output.Set("head_angle_y", 0f);
                output.Set("head_angle_z", 0f);
                output.Set("eye_l_smile", 0f);
                output.Set("eye_r_smile", 0f);
                output.Set("eye_l_open", 1f);
                output.Set("eye_r_open", 1f);
                output.Set("body_angle_x", 0f);
                output.Set("body_angle_z", 0f);
                output.Set("mouth_form", 0f);
            }
        }

        // ================================================================
        //  动作11: 困惑
        // ================================================================
        public static class Action11_Confuse
        {
            const float DURATION = 3f;
            const float TILT = 15f;
            const float BROW = -3f;
            const float EYE_SQUINT = 0.15f;
            const float MOUTH = 0.2f;
            const float HEAD_SIDE = -5f;
            const float BODY_SIDE = 3f;
            public static float Duration => DURATION;

            public static void Update(float elapsed, MotionOutput output)
            {
                float t = Mathf.Clamp01(elapsed / DURATION);
                float eased = Mathf.Sin(t * Mathf.PI);

                output.Set("head_angle_z", eased * TILT);
                output.Set("head_angle_x", eased * HEAD_SIDE);
                output.Set("brow_r_y", eased * BROW);
                output.Set("brow_l_y", eased * BROW);
                output.Set("eye_l_open", 1f - eased * EYE_SQUINT);
                output.Set("eye_r_open", 1f - eased * EYE_SQUINT);
                output.Set("mouth_form", eased * MOUTH);
                output.Set("body_angle_z", eased * BODY_SIDE);
            }

            public static void Cleanup(MotionOutput output)
            {
                output.Set("head_angle_z", 0f);
                output.Set("head_angle_x", 0f);
                output.Set("brow_r_y", 0f);
                output.Set("brow_l_y", 0f);
                output.Set("eye_l_open", 1f);
                output.Set("eye_r_open", 1f);
                output.Set("mouth_form", 0f);
                output.Set("body_angle_z", 0f);
            }
        }

        // ============ 辅助 ============
        static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
        static float EaseInCubic(float x) => x * x * x;

        /// <summary>根据动作 ID 调用对应的 Cleanup（0=通用清理）</summary>
        public static void CleanupById(int actionId, MotionOutput output)
        {
            switch (actionId)
            {
                case 1: Action1_Tilt.Cleanup(output); break;
                case 2: Action2_Smile.Cleanup(output); break;
                case 3: Action3_Brow.Cleanup(output); break;
                case 4: Action4_StarSpin.Cleanup(output); break;
                case 5: Action5_Stretch.Cleanup(output); break;
                case 6: break; // 已删除
                case 7: break; // 已删除
                case 8: Action8_Cry.Cleanup(output); break;
                case 9: Action9_MagicCircle.Cleanup(output); break;
                case 10: Action10_Blush.Cleanup(output); break;
                case 11: Action11_Confuse.Cleanup(output); break;
                default:
                    // 通用清理：表情相关参数
                    output.Set("head_angle_z", 0f);
                    output.Set("eye_l_smile", 0f);
                    output.Set("eye_r_smile", 0f);
                    output.Set("mouth_form", 0f);
                    output.Set("brow_r_y", 0f);
                    output.Set("brow_l_y", 0f);
                    output.Set("eye_l_open", 1f);
                    output.Set("eye_r_open", 1f);
                    output.Set("eye_heart", 0f);
                    output.Set("special_money", 0f);
                    output.Set("special_tear", 0f);
                    output.Set("special_blush_dark", 0f);
                    output.Set("special_outer_mask", 0f);
                    break;
            }
        }
    }
}
