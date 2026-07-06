using System;
using System.Collections.Generic;

/// <summary>
/// Live2D 参数已知命名模式 — 双方共享的唯一真实来源
///
/// 所有 KNOWN_PATTERNS 和 STANDARD_SEMANTICS 集中于此，
/// RuntimeModelAnalyzer 和 Live2DModelAnalyzer 均引用此文件。
/// 新模型接入时只需修改此处。
/// </summary>
public static class KnownParameterPatterns
{
    /// <summary>
    /// 已知的命名模式 → 语义名映射。
    /// Key 是去掉 "Param" 前缀后的参数 ID（不区分大小写），
    /// Value 是标准语义名。
    /// </summary>
    public static readonly Dictionary<string, string> KNOWN_PATTERNS = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // ─── 身体 ───
        { "bodyanglex",     "body_angle_x" },
        { "bodyangley",     "body_angle_y" },
        { "bodyanglez",     "body_angle_z" },
        { "body_x",         "body_angle_x" },
        { "body_y",         "body_angle_y" },
        { "body_z",         "body_angle_z" },

        // ─── 头 ───
        { "anglex",         "head_angle_x" },
        { "angley",         "head_angle_y" },
        { "anglez",         "head_angle_z" },
        { "headx",          "head_angle_x" },
        { "heady",          "head_angle_y" },
        { "headz",          "head_angle_z" },

        // ─── 呼吸 ───
        { "breath",         "breath" },

        // ─── 眼睛 ───
        { "eye_l_open",     "eye_l_open" },
        { "eye_r_open",     "eye_r_open" },
        { "eyelopen",       "eye_l_open" },
        { "eyeropen",       "eye_r_open" },
        { "eyeballx",       "eye_ball_x" },
        { "eyebally",       "eye_ball_y" },
        { "eye_ball_x",     "eye_ball_x" },
        { "eye_ball_y",     "eye_ball_y" },
        { "eyelsmile",      "eye_l_smile" },
        { "eyersmile",      "eye_r_smile" },
        { "eyelform",       "eye_l_smile" },
        { "eyerform",       "eye_r_smile" },

        // ─── 眉毛 ───
        { "browry",         "brow_r_y" },
        { "browly",         "brow_l_y" },
        { "browryform",     "brow_r_y" },
        { "browlyform",     "brow_l_y" },
        { "browl",          "brow_l_angle" },
        { "browr",          "brow_r_angle" },
        { "browlx",         "brow_l_angle" },
        { "browrx",         "brow_r_angle" },

        // ─── 嘴 ───
        { "mouthform",      "mouth_form" },
        { "mouthopeny",     "mouth_open_y" },
        { "mouthopen",      "mouth_open_y" },

        // ─── 肩膀 ───
        { "shoulder",       "shoulder" },

        // ─── 右手臂 ───
        { "arm_r_up",       "arm_right_upper" },
        { "arm_r_mid",      "arm_right_mid" },
        { "arm_r_low",      "arm_right_lower" },
        { "arm_r_rot",      "arm_right_rotation" },
        { "arm_r_base",     "arm_right_base_rotation" },
        { "arm_r_sw",       "arm_right_switch" },
        { "arm_r_reach",    "arm_right_reach" },
        { "arm_r_wrist",    "arm_right_wrist_z" },

        // ─── 左手臂 ───
        { "arm_l_up",       "arm_left_upper" },
        { "arm_l_mid",      "arm_left_mid" },
        { "arm_l_low",      "arm_left_lower" },
        { "arm_l_ext",      "arm_left_extra" },
        { "arm_l_ext2",     "arm_left_extra2" },

        // ─── 手掌图层切换 ───
        { "handlayer95",    "hand_layer_95" },
        { "handlayer117",   "hand_layer_117" },
        { "handlayer98",    "hand_layer_98" },
        { "handlayer100",   "hand_layer_100" },
        { "handlayer116",   "hand_layer_116" },
        { "handlayer120",   "hand_layer_120" },
        { "handlayer108",   "hand_layer_108" },
        { "handlayer119",   "hand_layer_119" },

        // ─── 手指 ───
        { "normalfinger1",  "finger_normal_1" },
        { "normalfinger2",  "finger_normal_2" },
        { "normalfinger3",  "finger_normal_3" },
        { "normalfinger4",  "finger_normal_4" },
        { "normalfinger5",  "finger_normal_5" },
        { "finger_z",       "finger_z_rotate" },
        { "fthumb",         "finger_thumb" },
        { "findex",         "finger_index" },
        { "fmiddle",        "finger_middle" },
        { "fring",          "finger_ring" },
        { "fpinky",         "finger_pinky" },
        { "swordswitch",    "sword_finger_switch" },

        // ─── 腿 ───
        { "leg_l_lift",     "leg_l_lift" },
        { "leg_r_lift",     "leg_r_lift" },
        { "leg_l_swing",    "leg_l_swing" },
        { "leg_r_swing",    "leg_r_swing" },
        { "leg_l_bend",     "leg_l_bend" },
        { "leg_r_bend",     "leg_r_bend" },

        // ─── 头发 ───
        { "hair_bangs_1",   "hair_bangs_1" },
        { "hair_bangs_2",   "hair_bangs_2" },
        { "hair_bangs_3",   "hair_bangs_3" },
        { "hair_physics_1", "hair_physics_1" },
        { "hair_physics_2", "hair_physics_2" },
        { "hair_physics_3", "hair_physics_3" },
        { "hair_back_b_1",  "hair_back_b_1" },
        { "hair_back_b_2",  "hair_back_b_2" },
        { "hair_side_1",    "hair_side_1" },
        { "hair_side_2",    "hair_side_2" },
        { "hair_side_3",    "hair_side_3" },
        { "hair_back_1",    "hair_back_1" },
        { "hair_back_2",    "hair_back_2" },
        { "hair_back_3",    "hair_back_3" },
        { "hair_back_4",    "hair_back_4" },
        { "hair_ornament_1","hair_ornament_1" },
        { "hair_ornament_2","hair_ornament_2" },
        { "hair_ornament_3","hair_ornament_3" },
        { "hair_head_orn","hair_head_ornament" },

        // ─── 裙子 ───
        { "skirt_drive_1",  "skirt_drive_1" },
        { "skirt_drive_2",  "skirt_drive_2" },
        { "skirt_drive_3",  "skirt_drive_3" },
        { "skirt_drive_4",  "skirt_drive_4" },
        { "skirt_drive_5",  "skirt_drive_5" },
        { "skirt_drive_6",  "skirt_drive_6" },
        { "skirt_drive_7",  "skirt_drive_7" },

        // ─── 特殊效果 ───
        { "special_money",  "special_money" },
        { "special_tear",   "special_tear" },
        { "special_blush",  "special_blush_dark" },
        { "special_angry",  "special_angry" },
        { "special_outer",  "special_outer_mask" },

        // ─── 镜头 ───
        { "camerax",        "camera_x" },
        { "cameray",        "camera_y" },
        { "char_scale",     "character_scale" },
    };

    /// <summary>标准语义名列表（用于模板生成）</summary>
    public static readonly string[] STANDARD_SEMANTICS = new string[]
    {
        "body_angle_x", "body_angle_y", "body_angle_z",
        "head_angle_x", "head_angle_y", "head_angle_z",
        "breath",
        "eye_l_open", "eye_r_open", "eye_ball_x", "eye_ball_y",
        "eye_l_smile", "eye_r_smile", "eye_heart",
        "brow_r_y", "brow_l_y", "brow_l_angle", "brow_r_angle",
        "mouth_form", "mouth_open_y",
        "arm_right_upper", "arm_right_mid", "arm_right_lower",
        "arm_right_rotation", "arm_right_base_rotation",
        "arm_right_switch", "arm_right_reach", "arm_right_wrist_z",
        "arm_left_upper", "arm_left_mid", "arm_left_lower",
        "arm_left_extra", "arm_left_extra2",
        "hand_layer_95", "hand_layer_117", "hand_layer_98",
        "hand_layer_100", "hand_layer_116", "hand_layer_120",
        "hand_layer_108", "hand_layer_119",
        "leg_l_lift", "leg_r_lift", "leg_l_swing", "leg_l_bend",
        "leg_r_swing", "leg_r_bend", "shoulder",
        "hair_bangs_1", "hair_bangs_2", "hair_bangs_3",
        "hair_physics_1", "hair_physics_2", "hair_physics_3",
        "hair_back_b_1", "hair_back_b_2",
        "hair_side_1", "hair_side_2", "hair_side_3",
        "hair_back_1", "hair_back_2", "hair_back_3", "hair_back_4",
        "hair_ornament_1", "hair_ornament_2", "hair_ornament_3", "hair_head_ornament",
        "skirt_drive_1", "skirt_drive_2", "skirt_drive_3",
        "skirt_drive_4", "skirt_drive_5", "skirt_drive_6", "skirt_drive_7",
        "special_money", "special_tear", "special_blush_dark",
        "special_angry", "special_outer_mask",
        "sword_finger_switch",
        "finger_normal_1", "finger_normal_2", "finger_normal_3",
        "finger_normal_4", "finger_normal_5",
        "finger_z_rotate", "finger_thumb", "finger_index",
        "finger_middle", "finger_ring", "finger_pinky",
        "camera_x", "camera_y", "character_scale"
    };
}
