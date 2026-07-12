#!/usr/bin/env python3
"""
符玄 Live2D 动作参数映射器
============================
将开源 Live2D 动作数据（.mtn / .motion3.json）映射到符玄模型参数空间，
输出标准关键帧 JSON，用于注入 MotionMemory 作为高质量种子数据。

工作流程:
  1. 读取 fuxuan_map.json 获取目标模型参数定义（语义名、范围、部位）
  2. 解析源动作文件（.mtn 或 .motion3.json）提取参数曲线
  3. 通过语义映射表将源参数 ID → 语义名 → 目标参数 ID
  4. 按范围比例缩放参数值
  5. 输出标准 keyframe JSON 及 MotionMemoryEntry 格式

用法:
  python ParamMapper.py --input <动作文件或目录> --output <输出文件>
  python ParamMapper.py --batch ./samples/ --output ./seed_data.json
"""

import json
import os
import sys
import glob
import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple, Any
from copy import deepcopy

# ═══════════════════════════════════════════════════════════════════════
#  1. 目标模型参数定义（加载 fuxuan_map.json）
# ═══════════════════════════════════════════════════════════════════════

class TargetParamDef:
    """目标模型（符玄）的参数定义"""
    def __init__(self, semantic: str, param_id: str, description: str,
                 part: str, domain: str, axis: str,
                 min_val: float, max_val: float,
                 side: str = "", related: list = None,
                 rel_type: str = "", prerequisite: str = ""):
        self.semantic = semantic        # 语义名（如 "head_angle_x"）
        self.param_id = param_id        # 实际 ParamID（如 "ParamAngleX"）
        self.description = description  # 中文描述
        self.part = part                # 身体部位
        self.domain = domain            # 值域类型: angle/normalized/toggle/position/scale
        self.axis = axis                # 轴向: X/Y/Z
        self.min = min_val              # 最小值
        self.max = max_val              # 最大值
        self.side = side                # left/right
        self.related = related or []    # 关联参数
        self.rel_type = rel_type        # 关系类型
        self.prerequisite = prerequisite  # 前置条件

    @property
    def range_size(self) -> float:
        return self.max - self.min

    def normalize(self, value: float) -> float:
        """将 [min, max] 内的值归一化到 [0, 1]"""
        if self.range_size == 0: return 0.5
        return (value - self.min) / self.range_size

    def denormalize(self, norm: float) -> float:
        """将 [0, 1] 值映射回 [min, max]"""
        return self.min + norm * self.range_size

    def clamp(self, value: float) -> float:
        return max(self.min, min(self.max, value))

    def __repr__(self):
        return f"{self.semantic}[{self.min},{self.max}]"


class TargetModel:
    """符玄模型 — 从 fuxuan_map.json 加载"""
    def __init__(self, json_path: str):
        # 尝试 utf-8-sig 以处理 BOM
        for enc in ['utf-8-sig', 'utf-8', 'utf-16']:
            try:
                with open(json_path, 'r', encoding=enc) as f:
                    data = json.load(f)
                break
            except (json.JSONDecodeError, UnicodeError):
                continue
        else:
            raise ValueError(f"无法解析 {json_path}: 所有编码尝试均失败")

        self.format_version = data.get('formatVersion', '')
        self.model_name = data.get('modelName', '')
        self.description = data.get('description', '')

        self.params: Dict[str, TargetParamDef] = {}    # semantic → TargetParamDef
        self.param_id_map: Dict[str, str] = {}         # ParamID → semantic

        for entry in data.get('entries', []):
            s = entry['s']
            p = entry['p']
            # 清理 ParamID 前缀
            clean_p = p.replace('Param', '') if p.startswith('Param') else p

            param = TargetParamDef(
                semantic=s,
                param_id=p,
                description=entry.get('d', ''),
                part=entry.get('part', ''),
                domain=entry.get('domain', ''),
                axis=entry.get('axis', ''),
                min_val=entry.get('min', 0),
                max_val=entry.get('max', 1),
                side=entry.get('side', ''),
                related=entry.get('related', []),
                rel_type=entry.get('relType', ''),
                prerequisite=entry.get('prerequisite', ''),
            )
            self.params[s] = param
            self.param_id_map[clean_p] = s

        # 按部位分组
        self.part_groups: Dict[str, List[TargetParamDef]] = {}
        for p in self.params.values():
            self.part_groups.setdefault(p.part, []).append(p)

        print(f"[TargetModel] 加载 {self.model_name}: {len(self.params)} 个参数, "
              f"{len(self.part_groups)} 个部位")

    def get_by_part(self, part: str) -> List[TargetParamDef]:
        return self.part_groups.get(part, [])

    def has_semantic(self, semantic: str) -> bool:
        return semantic in self.params

    def get_range(self, semantic: str) -> Optional[Tuple[float, float]]:
        p = self.params.get(semantic)
        if p: return (p.min, p.max)
        return None


# ═══════════════════════════════════════════════════════════════════════
#  2. 语义映射表 — 将各种 Live2D 模型的原始参数 ID 映射到语义名
# ═══════════════════════════════════════════════════════════════════════

# 这是核心映射知识库：
# 将不同 Live2D 模型中的常见参数 ID 映射到符玄使用的统一语义名。
# 映射策略：匹配时尝试多种模式（全名、去掉 Param 前缀、小写等）

# 标准 Cubism 参数 ID → 语义名（Cubism 3+/4+ 标准命名）
CUBISM3_PARAM_MAP = {
    # 身体
    "ParamBodyAngleX": "body_angle_x",
    "ParamBodyAngleY": "body_angle_y",
    "ParamBodyAngleZ": "body_angle_z",

    # 头部
    "ParamAngleX": "head_angle_x",
    "ParamAngleY": "head_angle_y",
    "ParamAngleZ": "head_angle_z",

    # 呼吸
    "ParamBreath": "breath",

    # 眼睛
    "ParamEyeLOpen": "eye_l_open",
    "ParamEyeROpen": "eye_r_open",
    "ParamEyeBallX": "eye_ball_x",
    "ParamEyeBallY": "eye_ball_y",
    "ParamEyeLSmile": "eye_l_smile",
    "ParamEyeRSmile": "eye_r_smile",
    "ParamEyeForm": "eye_ball_x",     # 替代映射
    "ParamEyeForm_2": "eye_ball_y",

    # 眉毛
    "ParamBrowLY": "brow_l_y",
    "ParamBrowRY": "brow_r_y",
    "ParamBrowLAngle": "brow_l_angle",
    "ParamBrowRAngle": "brow_r_angle",
    "ParamBrowLForm": "brow_l_form",
    "ParamBrowRForm": "brow_r_form",  # 符玄没有，映射到 brow_r_y
    "ParamBrowLX": "brow_l_angle",    # 替代近似
    "ParamBrowRX": "brow_r_angle",

    # 嘴
    "ParamMouthForm": "mouth_form",
    "ParamMouthOpenY": "mouth_open_y",

    # 右臂
    "ParamArmRUpper": "arm_right_upper",
    "ParamArmR Mid": "arm_right_mid",     # 注意空格
    "ParamArmR_Mid": "arm_right_mid",
    "ParamArmRLower": "arm_right_lower",
    "ParamArmRWrist": "arm_right_wrist_z",

    # 左臂
    "ParamArmLUpper": "arm_left_upper",
    "ParamArmL Mid": "arm_left_mid",
    "ParamArmL_Mid": "arm_left_mid",
    "ParamArmLLower": "arm_left_lower",

    # 肩膀
    "ParamShoulderY": "shoulder",

    # 腿
    "ParamLegLLift": "leg_l_lift",
    "ParamLegRLift": "leg_r_lift",
    "ParamLegLSwing": "leg_l_swing",
    "ParamLegRSwing": "leg_r_swing",
    "ParamLegLBend": "leg_l_bend",
    "ParamLegRBend": "leg_r_bend",

    # 头发（通用物理）
    "ParamHairFront": "hair_bangs_1",
    "ParamHairSide": "hair_side_1",
    "ParamHairBack": "hair_back_b_1",
    "ParamHairFluffy": "hair_physics_1",
}

# Cubism 2.1 (.mtn) 常见参数 ID → 语义名
# .mtn 文件通常省略 "Param" 前缀
CUBISM2_PARAM_MAP = {
    "BODY_ANGLE_X": "body_angle_x",
    "BODY_ANGLE_Y": "body_angle_y",
    "BODY_ANGLE_Z": "body_angle_z",

    "ANGLE_X": "head_angle_x",
    "ANGLE_Y": "head_angle_y",
    "ANGLE_Z": "head_angle_z",

    "BREATH": "breath",

    "EYE_L_OPEN": "eye_l_open",
    "EYE_R_OPEN": "eye_r_open",
    "EYE_BALL_X": "eye_ball_x",
    "EYE_BALL_Y": "eye_ball_y",
    "EYE_L_SMILE": "eye_l_smile",
    "EYE_R_SMILE": "eye_r_smile",

    "BROW_L_Y": "brow_l_y",
    "BROW_R_Y": "brow_r_y",
    "BROW_L_ANGLE": "brow_l_angle",
    "BROW_R_ANGLE": "brow_r_angle",
    "BROW_L_FORM": "brow_l_form",

    "MOUTH_FORM": "mouth_form",
    "MOUTH_OPEN_Y": "mouth_open_y",

    "ARM_L_UPPER": "arm_left_upper",
    "ARM_L_LOWER": "arm_left_lower",
    "ARM_R_UPPER": "arm_right_upper",
    "ARM_R_LOWER": "arm_right_lower",

    "LEG_L_LIFT": "leg_l_lift",
    "LEG_R_LIFT": "leg_r_lift",

    "HAIR_FRONT": "hair_bangs_1",
    "HAIR_SIDE": "hair_side_1",
    "HAIR_BACK": "hair_back_b_1",

    "SHOULDER": "shoulder",
    "SHOULDER_Y": "shoulder",
}

# 合并映射表（.mtn 也兼容无 Param 前缀的 Cubism3 参数）
# 例如 "AngleX" → head_angle_x
def _build_fuzzy_map() -> Dict[str, str]:
    """构建模糊映射：同时匹配带/不带 Param 前缀的各种变体"""
    fuzzy = {}

    # 从 Cubism3 映射表生成变体
    for cubism_id, semantic in CUBISM3_PARAM_MAP.items():
        # 原样
        fuzzy[cubism_id] = semantic
        fuzzy[cubism_id.lower()] = semantic
        # 去掉 Param 前缀
        if cubism_id.startswith("Param"):
            no_prefix = cubism_id[5:]
            fuzzy[no_prefix] = semantic
            fuzzy[no_prefix.upper()] = semantic
            fuzzy[no_prefix.lower()] = semantic
        # 去掉下划线
        flat = cubism_id.replace("_", "").replace(" ", "").lower()
        fuzzy[flat] = semantic

    # 从 Cubism2 映射表补充
    for c2_id, semantic in CUBISM2_PARAM_MAP.items():
        fuzzy[c2_id] = semantic
        fuzzy[c2_id.lower()] = semantic
        fuzzy[c2_id.replace("_", "").lower()] = semantic
        # 加 Param 前缀
        fuzzy["Param" + c2_id.title().replace("_", "")] = semantic

    return fuzzy

FUZZY_PARAM_MAP = _build_fuzzy_map()


def resolve_semantic(param_name: str) -> Optional[str]:
    """将任意 Live2D 参数名解析为语义名（模糊匹配）"""
    if not param_name:
        return None

    # 精确匹配
    if param_name in FUZZY_PARAM_MAP:
        return FUZZY_PARAM_MAP[param_name]

    # 尝试各种清理
    cleaned = param_name.strip()

    # 去掉 "Param" 前缀 (Cubism 3+, e.g. "ParamAngleX")
    if cleaned.startswith("Param"):
        cleaned = cleaned[5:]
    # 去掉 "PARAM_" 前缀 (Cubism 2.1 old .mtn, e.g. "PARAM_ANGLE_X")
    elif cleaned.startswith("PARAM_"):
        cleaned = cleaned[6:]
    # 去掉 "param_" 前缀 (小写变体)
    elif cleaned.startswith("param_"):
        cleaned = cleaned[6:]

    # 去掉空格
    cleaned = cleaned.replace(" ", "")
    # 尝试多种大小写
    for variant in [cleaned, cleaned.upper(), cleaned.lower(), cleaned.title()]:
        if variant in FUZZY_PARAM_MAP:
            return FUZZY_PARAM_MAP[variant]

    return None


# ═══════════════════════════════════════════════════════════════════════
#  3. 动作文件解析器
# ═══════════════════════════════════════════════════════════════════════

class MotionKeyframe:
    """单个关键帧"""
    def __init__(self, time: float, values: Dict[str, float]):
        self.time = time
        self.values = values  # semantic → value

    def to_dict(self) -> dict:
        return {"time": self.time, "values": dict(self.values)}


class ParsedMotion:
    """解析后的动作数据"""
    def __init__(self, source_file: str, description: str = ""):
        self.source_file = source_file
        self.description = description or Path(source_file).stem
        self.keyframes: List[MotionKeyframe] = []
        self.total_duration: float = 0.0
        self.raw_params: Dict[str, List[Tuple[float, float]]] = {}  # semantic → [(time, value)]

    def add_keyframe(self, time: float, values: Dict[str, float]):
        self.keyframes.append(MotionKeyframe(time, values))
        if time > self.total_duration:
            self.total_duration = time

    def add_curve_point(self, semantic: str, time: float, value: float):
        """添加曲线控制点（用于后续采样）"""
        if semantic not in self.raw_params:
            self.raw_params[semantic] = []
        self.raw_params[semantic].append((time, value))

    def sample_at(self, time: float) -> Dict[str, float]:
        """在指定时间点采样所有参数值（线性插值）"""
        result = {}
        for semantic, points in self.raw_params.items():
            if len(points) == 0:
                continue
            if len(points) == 1:
                result[semantic] = points[0][1]
                continue

            # 排序
            sorted_pts = sorted(points, key=lambda x: x[0])

            # 边界
            if time <= sorted_pts[0][0]:
                result[semantic] = sorted_pts[0][1]
                continue
            if time >= sorted_pts[-1][0]:
                result[semantic] = sorted_pts[-1][1]
                continue

            # 线性插值
            for i in range(len(sorted_pts) - 1):
                t0, v0 = sorted_pts[i]
                t1, v1 = sorted_pts[i + 1]
                if t0 <= time <= t1:
                    if t1 == t0:
                        result[semantic] = v0
                    else:
                        t = (time - t0) / (t1 - t0)
                        result[semantic] = v0 + t * (v1 - v0)
                    break

        return result

    def to_keyframe_sequence(self, num_frames: int = 6) -> List[MotionKeyframe]:
        """将曲线点转换为均匀采样的关键帧序列"""
        if self.keyframes:
            return self.keyframes

        if not self.raw_params:
            return []

        # 确定时间范围
        all_times = set()
        for pts in self.raw_params.values():
            for t, _ in pts:
                all_times.add(t)

        if not all_times:
            return []

        t_min = 0.0
        t_max = max(all_times)
        if t_max <= 0:
            t_max = 3.0

        frames = []
        for i in range(num_frames):
            t = t_min + (t_max - t_min) * i / (num_frames - 1) if num_frames > 1 else 0
            values = self.sample_at(t)
            frames.append(MotionKeyframe(t, values))

        self.keyframes = frames
        self.total_duration = t_max
        return frames

    def to_dict(self) -> dict:
        return {
            "totalDuration": self.total_duration,
            "description": self.description,
            "keyframes": [kf.to_dict() for kf in self.keyframes]
        }


def parse_mtn_v1_text(filepath: str) -> Optional[ParsedMotion]:
    """
    解析 Cubism 2.0/2.1 旧版文本格式 .mtn 文件

    格式:
    # Live2D Animator Motion Data
    $fps=30
    PARAM_NAME=val1,val2,val3,...

    每帧一个值，帧数由逗号数量决定。
    """
    try:
        with open(filepath, 'r', encoding='utf-8-sig') as f:
            content = f.read()
    except Exception as e:
        print(f"  ⚠️  读取失败: {e}")
        return None

    # 使用 splitlines 兼容 \n, \r\n, \r 三种换行符
    lines = content.strip().splitlines()

    # 检测是否为旧版文本格式
    if not lines or not lines[0].strip().startswith('#'):
        return None

    fps = 30
    param_data = {}  # param_id → [values...]
    description = Path(filepath).stem

    for line in lines:
        line = line.strip()
        if not line:
            continue
        if line.startswith('#'):
            continue
        if line.startswith('$'):
            # $fps=30
            parts = line[1:].split('=')
            if len(parts) == 2 and parts[0].strip().lower() == 'fps':
                try:
                    fps = int(parts[1].strip())
                except ValueError:
                    pass
            continue

        # PARAM_NAME=val1,val2,...
        if '=' in line:
            name, vals_str = line.split('=', 1)
            name = name.strip()
            vals = []
            for v_str in vals_str.split(','):
                v_str = v_str.strip()
                if v_str:
                    try:
                        vals.append(float(v_str))
                    except ValueError:
                        pass
            if vals:
                param_data[name] = vals

    if not param_data:
        return None

    # 确定最大帧数
    max_frames = max(len(v) for v in param_data.values())
    if max_frames == 0:
        return None

    motion = ParsedMotion(filepath, description)
    motion.total_duration = max_frames / fps

    mapped_count = 0
    for param_id, values in param_data.items():
        semantic = resolve_semantic(param_id)
        if not semantic:
            continue

        for i, val in enumerate(values):
            t = i / fps
            motion.add_curve_point(semantic, t, val)
            mapped_count += 1

    if mapped_count == 0:
        return None

    # 采样为关键帧（保留原始分辨率，最多 10 帧）
    num_frames = min(max_frames, 10)
    motion.to_keyframe_sequence(num_frames=num_frames)
    print(f"  ✅ 解析成功(旧版文本): {len(param_data)} 个参数, "
          f"{num_frames} 帧, {motion.total_duration:.1f}s, fps={fps}")
    return motion


def parse_mtn_json(filepath: str) -> Optional[ParsedMotion]:
    """
    解析 Cubism 2.1 JSON 格式 .mtn 动作文件

    .mtn JSON 格式包含:
    {
      "Version": 3,
      "Curve": [
        {"Target": "Parameter", "ID": "ParamAngleX", "FadeInTime": 0.5,
         "FadeOutTime": 0.5, "Segments": [0, 0, 0.5, 15, 1.0, 0]},
        ...
      ],
      "UserData": [],
      "FaceParts": [],
      "Expression": []
    }

    Segments 格式: [t0, v0, t1, v1, t2, v2, ...]（时间-值对，线性分段）
    """
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        data = json.loads(content)
    except (json.JSONDecodeError, UnicodeDecodeError):
        try:
            with open(filepath, 'r', encoding='shift-jis') as f:
                content = f.read()
            data = json.loads(content)
        except Exception as e:
            print(f"  ⚠️  无法解析 JSON: {e}")
            return None
    except Exception as e:
        print(f"  ⚠️  读取失败: {e}")
        return None

    if not isinstance(data, dict):
        return None

    curves = data.get("Curve", [])
    if not curves:
        curves = data.get("curves", data.get("parameter", []))

    description = Path(filepath).stem
    motion = ParsedMotion(filepath, description)

    mapped_count = 0
    for curve in curves:
        if not isinstance(curve, dict):
            continue

        target = curve.get("Target", "")
        param_id = curve.get("ID", curve.get("Id", curve.get("id", "")))
        segments = curve.get("Segments", curve.get("segments", []))

        if not param_id or not segments:
            continue

        if target and target.lower() != "parameter":
            continue

        semantic = resolve_semantic(param_id)
        if not semantic:
            continue

        for i in range(0, len(segments) - 1, 2):
            t = segments[i]
            v = segments[i + 1]
            motion.add_curve_point(semantic, t, v)
            mapped_count += 1

    if mapped_count == 0:
        print(f"  ⚠️  没有可映射的参数")
        return None

    # 提取描述
    user_data = data.get("UserData", [])
    if user_data and isinstance(user_data, list) and len(user_data) > 0:
        if isinstance(user_data[0], dict) and "Data" in user_data[0]:
            motion.description = user_data[0].get("Data", "").strip() or description

    # 采样为关键帧序列
    motion.to_keyframe_sequence(num_frames=6)
    print(f"  ✅ 解析成功: {len(motion.raw_params)} 个参数 → "
          f"{len(motion.keyframes)} 帧, {motion.total_duration:.1f}s")

    return motion


def parse_motion3_json(filepath: str) -> Optional[ParsedMotion]:
    """
    解析 Cubism 3/4/5 .motion3.json 动作文件

    格式:
    {
      "Version": 3,
      "Meta": {"Duration": 3.0, "Fps": 30, "Loop": false,
               "AreBeziersRestricted": true, "CurveCount": N,
               "TotalSegmentCount": M, "TotalPointCount": K,
               "UserDataCount": 0, "TotalUserDataSize": L},
      "Curves": [
        {"Target": "Parameter", "Id": "ParamAngleX",
         "FadeInTime": 0.0, "FadeOutTime": 0.0,
         "Segments": [0, 0, 0.5, 15.0, 1.0, 0.0]},
        ...
      ],
      "UserData": [],
      "FaceParts": [],
      "Expression": []
    }

    Segments 格式更复杂: [t0, v0, type, ...]
      type=0: 线性, 后续 [t, v]
      type=1: 贝塞尔, 后续 [t, v, t, v]
      type=2: 分段线性 stepped (无插值), 后续 [t, v]
      type=3: 贝塞尔分段, 后续 [t, v, t, v, t, v, t, v]
    """
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            data = json.load(f)
    except Exception as e:
        print(f"  ⚠️  读取失败: {e}")
        return None

    if not isinstance(data, dict):
        return None

    meta = data.get("Meta", {})
    duration = meta.get("Duration", 3.0)
    curves = data.get("Curves", [])

    description = Path(filepath).stem
    motion = ParsedMotion(filepath, description)
    motion.total_duration = duration

    mapped_count = 0
    for curve in curves:
        if not isinstance(curve, dict):
            continue

        target = curve.get("Target", "")
        param_id = curve.get("Id", curve.get("ID", ""))
        segments = curve.get("Segments", [])

        if not param_id or not segments:
            continue

        if target and target.lower() != "parameter":
            continue

        semantic = resolve_semantic(param_id)
        if not semantic:
            continue

        # 解析 Segments (Cubism 3+ 格式)
        # 每段以 type 开头
        i = 0
        while i < len(segments):
            if i >= len(segments):
                break
            segment_type = int(segments[i]) if i < len(segments) else 0
            i += 1

            if segment_type == 0:  # 线性
                # [t, v] 对
                while i + 1 < len(segments):
                    t = segments[i]
                    v = segments[i + 1]
                    if i + 2 < len(segments) and isinstance(segments[i + 2], (int, float)):
                        # 看看下一个是不是新的段起点
                        # Cubism 线性段: [t0, v0, t1, v1, ...]
                        pass
                    motion.add_curve_point(semantic, t, v)
                    mapped_count += 1
                    i += 2
                    # 检查是否到了下一个段类型标记
                    if i < len(segments) and isinstance(segments[i], int) and segments[i] in (0, 1, 2, 3):
                        break
            elif segment_type == 1:  # 贝塞尔
                # [cx1, cy1, cx2, cy2, ex, ey]
                # 简化：只采样终点
                if i + 5 < len(segments):
                    ex = segments[i + 4]
                    ey = segments[i + 5]
                    motion.add_curve_point(semantic, ex, ey)
                    mapped_count += 1
                    i += 6
                else:
                    i += 2  # 跳过异常数据
            elif segment_type == 2:  # Stepped（阶跃）
                if i + 1 < len(segments):
                    t = segments[i]
                    v = segments[i + 1]
                    motion.add_curve_point(semantic, t, v)
                    mapped_count += 1
                    i += 2
                else:
                    i += 1
            elif segment_type == 3:  # 贝塞尔分段
                if i + 7 < len(segments):
                    ex = segments[i + 6]
                    ey = segments[i + 7]
                    motion.add_curve_point(semantic, ex, ey)
                    mapped_count += 1
                    i += 8
                else:
                    i += 2
            else:
                break

    if mapped_count == 0:
        print(f"  ⚠️  没有可映射的参数")
        return None

    motion.to_keyframe_sequence(num_frames=max(4, int(duration * 2)))
    print(f"  ✅ 解析成功: {len(motion.raw_params)} 个参数, "
          f"{len(motion.keyframes)} 帧, {duration:.1f}s")

    return motion


def parse_action_file(filepath: str) -> Optional[ParsedMotion]:
    """自动检测格式并解析动作文件"""
    ext = Path(filepath).suffix.lower()
    if ext == '.mtn':
        # 先尝试新版 JSON 格式
        result = parse_mtn_json(filepath)
        if result is not None:
            return result
        # 再尝试旧版文本格式
        result = parse_mtn_v1_text(filepath)
        if result is not None:
            return result
        print(f"  ⚠️  无法解析 .mtn 文件（JSON 和文本格式均失败）")
        return None
    elif ext == '.json' and 'motion3' in Path(filepath).name.lower():
        return parse_motion3_json(filepath)
    elif ext == '.json':
        # 尝试自动检测
        try:
            with open(filepath, 'r', encoding='utf-8') as f:
                preview = f.read(512)
            if '"Curve"' in preview or '"Segments"' in preview:
                return parse_mtn_json(filepath)
            elif '"Meta"' in preview and '"Duration"' in preview:
                return parse_motion3_json(filepath)
        except:
            pass
        return parse_motion3_json(filepath)
    return None


# ═══════════════════════════════════════════════════════════════════════
#  4. 参数值缩放器 — 将源值映射到目标范围
# ═══════════════════════════════════════════════════════════════════════

class ParamScaler:
    """
    参数值缩放器

    将源模型参数值映射到目标模型（符玄）的参数范围。
    支持 3 种映射模式:
      - "proportional": 按比例缩放（默认）
      - "direct": 直接使用（当范围相同时）
      - "clamp": 仅截断
    """

    # 常见 Live2D 参数的典型范围（源模型）
    # 大多数 Cubism 标准模型使用以下约定
    SOURCE_RANGES: Dict[str, Tuple[float, float]] = {
        # 角度参数: 大多数模型使用 [-30, 30]
        "body_angle_x": (-30, 30),
        "body_angle_y": (-30, 30),
        "body_angle_z": (-30, 30),
        "head_angle_x": (-30, 30),
        "head_angle_y": (-30, 30),
        "head_angle_z": (-30, 30),

        # 眼睛: [0, 1]
        "eye_l_open": (0, 1),
        "eye_r_open": (0, 1),
        "eye_ball_x": (-1, 1),
        "eye_ball_y": (-1, 1),
        "eye_l_smile": (0, 1),
        "eye_r_smile": (0, 1),

        # 眉毛: [-1, 1]
        "brow_l_y": (-1, 1),
        "brow_r_y": (-1, 1),
        "brow_l_angle": (-1, 1),
        "brow_r_angle": (-1, 1),
        "brow_l_form": (-1, 1),

        # 嘴: [0, 1]
        "mouth_form": (0, 1),
        "mouth_open_y": (0, 1),

        # 手臂角度: [-30, 30]
        "arm_right_upper": (-30, 30),
        "arm_right_mid": (-30, 30),
        "arm_right_lower": (-30, 30),
        "arm_left_upper": (-30, 30),
        "arm_left_mid": (-30, 30),
        "arm_left_lower": (-30, 30),

        # 腿: [-20, 20]
        "leg_l_lift": (-20, 20),
        "leg_r_lift": (-20, 20),
        "leg_l_swing": (-20, 20),
        "leg_r_swing": (-20, 20),

        # 肩膀: [0, 1]
        "shoulder": (0, 1),
    }

    def __init__(self, target: TargetModel):
        self.target = target

    def get_source_range(self, semantic: str) -> Tuple[float, float]:
        """获取源模型参数范围（默认）"""
        default = self.SOURCE_RANGES.get(semantic, (0.0, 1.0))
        return default

    def scale_value(self, semantic: str, source_value: float) -> float:
        """将源值缩放到目标范围"""
        target_def = self.target.params.get(semantic)
        if not target_def:
            return source_value

        source_min, source_max = self.get_source_range(semantic)
        target_min, target_max = target_def.min, target_def.max

        source_range = source_max - source_min
        if source_range == 0:
            return (target_min + target_max) / 2

        # 归一化到 [0, 1]
        norm = (source_value - source_min) / source_range
        # 映射到目标范围
        result = target_min + norm * (target_max - target_min)
        return target_def.clamp(result)

    def scale_keyframe(self, kf: MotionKeyframe) -> MotionKeyframe:
        """缩放关键帧中的所有参数值"""
        scaled = {}
        for semantic, value in kf.values.items():
            scaled[semantic] = self.scale_value(semantic, value)
        return MotionKeyframe(kf.time, scaled)

    def scale_motion(self, motion: ParsedMotion) -> ParsedMotion:
        """缩放整个动作"""
        result = ParsedMotion(motion.source_file, motion.description)
        result.total_duration = motion.total_duration
        for kf in motion.keyframes:
            result.keyframes.append(self.scale_keyframe(kf))
        return result


# ═══════════════════════════════════════════════════════════════════════
#  5. 动作合成器 — 针对符玄模型的手工动作模板
# ═══════════════════════════════════════════════════════════════════════

class MotionSynthesizer:
    """
    动作合成器

    当映射数据不够时，用手工模板 + 适量随机变化生成优质动作。
    这些模板基于 Live2D 动作设计的最佳实践。
    """

    # 手工设计的优质动作模板（语义名 → 值，按时间点）
    TEMPLATES: Dict[str, List[Dict[str, Any]]] = {
        "害羞捂脸": [
            {"t": 0.0, "v": {}},
            {"t": 0.5, "v": {"arm_right_upper": 25, "arm_left_upper": 25,
                              "arm_right_lower": -20, "arm_left_lower": -20,
                              "arm_right_reach": 0.6}},
            {"t": 1.0, "v": {"head_angle_y": -15, "eye_l_open": 0.3, "eye_r_open": 0.3}},
            {"t": 2.0, "v": {"head_angle_y": -12, "eye_l_open": 0.2, "eye_r_open": 0.2,
                              "mouth_form": 0.3}},
            {"t": 3.0, "v": {"head_angle_y": -5, "arm_right_upper": 15, "arm_left_upper": 15}},
            {"t": 3.5, "v": {}},
        ],
        "骄傲叉腰": [
            {"t": 0.0, "v": {}},
            {"t": 0.4, "v": {"arm_right_upper": 20, "arm_left_upper": 20,
                              "arm_right_lower": -15, "arm_left_lower": -15,
                              "body_angle_z": 5}},
            {"t": 1.0, "v": {"head_angle_y": 10, "eye_l_smile": 0.3, "eye_r_smile": 0.3,
                              "mouth_form": 0.4, "body_angle_z": 3}},
            {"t": 2.5, "v": {"head_angle_y": 8, "eye_l_smile": 0.5, "eye_r_smile": 0.5}},
            {"t": 3.5, "v": {"head_angle_y": 12, "body_angle_z": 8}},
            {"t": 4.0, "v": {}},
        ],
        "歪头思考": [
            {"t": 0.0, "v": {}},
            {"t": 0.5, "v": {"head_angle_z": 20}},
            {"t": 1.0, "v": {"head_angle_z": 25, "eye_ball_y": -0.6, "eye_ball_x": 0.2}},
            {"t": 2.0, "v": {"head_angle_z": 22, "mouth_form": 0.2}},
            {"t": 3.0, "v": {"head_angle_z": 25, "eye_ball_y": -0.5}},
            {"t": 4.0, "v": {"head_angle_z": 10}},
            {"t": 4.5, "v": {}},
        ],
        "惊讶捂嘴": [
            {"t": 0.0, "v": {}},
            {"t": 0.3, "v": {"head_angle_y": 10, "eye_l_open": 0.9, "eye_r_open": 0.9}},
            {"t": 0.6, "v": {"head_angle_y": 12, "mouth_open_y": 0.6,
                              "arm_right_upper": 25, "arm_left_upper": 20,
                              "arm_right_reach": 0.5, "arm_left_lower": -10}},
            {"t": 1.2, "v": {"head_angle_y": 8, "mouth_open_y": 0.3}},
            {"t": 2.0, "v": {"head_angle_y": 3, "eye_l_open": 0.6, "eye_r_open": 0.6}},
            {"t": 3.0, "v": {}},
        ],
        "行礼鞠躬": [
            {"t": 0.0, "v": {}},
            {"t": 0.5, "v": {"body_angle_y": -20}},
            {"t": 1.0, "v": {"body_angle_y": -25, "head_angle_y": -20,
                              "arm_right_lower": -15, "arm_left_lower": -15}},
            {"t": 1.5, "v": {"body_angle_y": -25, "head_angle_y": -20}},
            {"t": 2.5, "v": {"body_angle_y": -10, "head_angle_y": -8}},
            {"t": 3.5, "v": {}},
        ],
        "合十祈祷": [
            {"t": 0.0, "v": {}},
            {"t": 0.5, "v": {"arm_right_upper": 25, "arm_left_upper": 25,
                              "arm_right_lower": -20, "arm_left_lower": -20}},
            {"t": 1.0, "v": {"head_angle_y": -10, "eye_l_open": 0.2, "eye_r_open": 0.2}},
            {"t": 2.5, "v": {"head_angle_y": -8, "mouth_form": 0.1}},
            {"t": 4.0, "v": {"head_angle_y": -5, "arm_right_upper": 20, "arm_left_upper": 20}},
            {"t": 5.0, "v": {}},
        ],
        "俏皮眨眼": [
            {"t": 0.0, "v": {}},
            {"t": 0.3, "v": {"head_angle_z": 8, "head_angle_y": 5}},
            {"t": 0.6, "v": {"eye_r_open": 0.1, "eye_l_open": 0.9,
                              "mouth_form": 0.3}},
            {"t": 1.0, "v": {"eye_r_open": 0.0, "head_angle_z": 5}},
            {"t": 1.3, "v": {"eye_r_open": 0.9, "head_angle_z": 3}},
            {"t": 2.0, "v": {"eye_r_open": 0.9, "eye_l_open": 0.9}},
            {"t": 2.5, "v": {}},
        ],
        "伸懒腰": [
            {"t": 0.0, "v": {}},
            {"t": 0.8, "v": {"arm_right_upper": -25, "arm_left_upper": -25,
                              "head_angle_y": 15, "body_angle_y": 5}},
            {"t": 1.5, "v": {"arm_right_upper": -28, "arm_left_upper": -28,
                              "head_angle_y": 20, "mouth_open_y": 0.6,
                              "eye_l_open": 0.2, "eye_r_open": 0.2}},
            {"t": 2.5, "v": {"arm_right_upper": -20, "arm_left_upper": -20,
                              "head_angle_y": 10}},
            {"t": 3.5, "v": {"arm_right_upper": -5, "arm_left_upper": -5}},
            {"t": 4.5, "v": {}},
        ],
        "摇头": [
            {"t": 0.0, "v": {}},
            {"t": 0.2, "v": {"head_angle_x": -20, "head_angle_y": -5}},
            {"t": 0.5, "v": {"head_angle_x": 20}},
            {"t": 0.8, "v": {"head_angle_x": -18}},
            {"t": 1.1, "v": {"head_angle_x": 18}},
            {"t": 1.4, "v": {"head_angle_x": -10}},
            {"t": 1.8, "v": {"head_angle_x": 5}},
            {"t": 2.2, "v": {}},
        ],
        "低头玩衣角": [
            {"t": 0.0, "v": {}},
            {"t": 0.5, "v": {"head_angle_y": -20, "arm_right_upper": 10,
                              "arm_right_lower": 20, "arm_right_wrist_z": 15,
                              "eye_l_open": 0.3, "eye_r_open": 0.3}},
            {"t": 1.5, "v": {"head_angle_y": -22, "arm_right_wrist_z": 10,
                              "head_angle_z": 5, "mouth_form": 0.2}},
            {"t": 2.5, "v": {"head_angle_y": -18, "arm_right_wrist_z": 20}},
            {"t": 3.5, "v": {"head_angle_y": -15, "arm_right_wrist_z": 15}},
            {"t": 4.5, "v": {"head_angle_y": -8}},
            {"t": 5.5, "v": {}},
        ],
        "开心挥手": [
            {"t": 0.0, "v": {}},
            {"t": 0.3, "v": {"arm_right_upper": 25, "arm_right_lower": 15,
                              "head_angle_y": 8, "eye_l_smile": 0.7, "eye_r_smile": 0.7}},
            {"t": 0.8, "v": {"arm_right_upper": 20, "arm_right_lower": 25,
                              "mouth_form": 0.5}},
            {"t": 1.3, "v": {"arm_right_upper": 25, "arm_right_lower": 15}},
            {"t": 1.8, "v": {"arm_right_upper": 20, "arm_right_lower": 25}},
            {"t": 2.5, "v": {"arm_right_upper": 15, "arm_right_lower": 10}},
            {"t": 3.0, "v": {}},
        ],
    }

    def __init__(self, target: TargetModel, scaler: ParamScaler):
        self.target = target
        self.scaler = scaler

    def synthesize(self, name: str, variation: float = 0.0) -> Optional[ParsedMotion]:
        """合成指定动作（带可选随机变化）"""
        template = self.TEMPLATES.get(name)
        if not template:
            return None

        motion = ParsedMotion(f"template://{name}", name)
        motion.total_duration = max(kf["t"] for kf in template)

        for kf in template:
            t = kf["t"]
            values = dict(kf["v"])

            # 可选随机变化
            if variation > 0:
                for sem in values:
                    target_def = self.target.params.get(sem)
                    if target_def:
                        range_size = target_def.range_size
                        jitter = (random.random() - 0.5) * variation * range_size * 0.2
                        values[sem] = target_def.clamp(values[sem] + jitter)

            # 缩放
            scaled = {}
            for sem, val in values.items():
                scaled[sem] = self.scaler.scale_value(sem, val)

            motion.keyframes.append(MotionKeyframe(t, scaled))

        return motion

    def synthesize_all(self, variation: float = 0.1) -> List[ParsedMotion]:
        """合成所有模板动作"""
        results = []
        for name in self.TEMPLATES:
            motion = self.synthesize(name, variation)
            if motion:
                results.append(motion)
                print(f"  ✅ 合成: {name} ({len(motion.keyframes)} 帧, {motion.total_duration:.1f}s)")
        return results


# ═══════════════════════════════════════════════════════════════════════
#  6. 输出格式化 — 生成 MotionMemoryEntry 兼容的 JSON
# ═══════════════════════════════════════════════════════════════════════

def format_as_memory_entry(motion: ParsedMotion, score: int = 4,
                           review: str = "", action_name: str = "") -> dict:
    """
    将 ParsedMotion 格式化为 MotionMemoryEntry 兼容字典

    MotionMemoryEntry 格式:
    {
      "actionName": "动作名",
      "bestParamJson": "{\"kf\":[{...}]}",  # 最佳参数JSON字符串
      "bestScore": 4,
      "bestReview": "GLM评语摘要",
      "lastParamSnapshot": "key=value, key=value",
      "timestamp": "yyyy-MM-dd HH:mm",
      "attempts": 1,
      "totalDuration": 3.0,
      "keyframeCount": 6,
      "scoreHistory": [4]
    }
    """
    name = action_name or motion.description

    # 格式化关键帧 JSON
    keyframe_data = motion.to_dict()
    best_param_json = json.dumps(keyframe_data, ensure_ascii=False, indent=2)

    # 生成 lastParamSnapshot（取中间帧的参数快照）
    mid_idx = len(motion.keyframes) // 2
    snapshot_parts = []
    if motion.keyframes and mid_idx < len(motion.keyframes):
        for sem, val in sorted(motion.keyframes[mid_idx].values.items()):
            snapshot_parts.append(f"{sem}={val:.1f}")

    return {
        "actionName": name,
        "bestParamJson": best_param_json,
        "bestScore": score,
        "bestReview": review or f"种子数据 — 手工设计的优质{name}动作",
        "lastParamSnapshot": ", ".join(snapshot_parts[:15]),
        "timestamp": "2026-07-12 00:00",
        "attempts": 1,
        "totalDuration": motion.total_duration,
        "keyframeCount": len(motion.keyframes),
        "scoreHistory": [score],
    }


def format_as_injection_json(entries: List[dict]) -> str:
    """
    生成可注入 motion_memory.json 的 JSON 字符串

    输出格式兼容 Unity JsonUtility:
    {"entries": [...]}
    """
    output = {"entries": entries}
    return json.dumps(output, ensure_ascii=False, indent=2)


# ═══════════════════════════════════════════════════════════════════════
#  7. 主流程
# ═══════════════════════════════════════════════════════════════════════

def find_action_files(directory: str) -> List[str]:
    """递归查找目录下所有动作文件"""
    files = []
    for ext in ['*.mtn', '*.motion3.json']:
        # 使用 glob 递归搜索
        if ext == '*.motion3.json':
            # 这个 glob 模式匹配文件名中含 motion3 的 json
            found = glob.glob(os.path.join(directory, '**', '*.json'), recursive=True)
            for f in found:
                if 'motion3' in Path(f).name.lower():
                    files.append(f)
        else:
            found = glob.glob(os.path.join(directory, '**', ext), recursive=True)
            files.extend(found)
    return files


def process_directory(input_dir: str, target: TargetModel, scaler: ParamScaler) -> List[ParsedMotion]:
    """处理目录下所有动作文件"""
    files = find_action_files(input_dir)
    print(f"\n在 {input_dir} 中找到 {len(files)} 个动作文件")

    motions = []
    for fpath in sorted(files):
        rel = os.path.relpath(fpath, input_dir)
        print(f"\n  解析: {rel}")
        motion = parse_action_file(fpath)
        if motion and len(motion.raw_params) > 0:
            # 缩放参数值
            motion = scaler.scale_motion(motion)
            motions.append(motion)
        else:
            print(f"    — 跳过（无映射参数）")

    return motions


def main():
    import argparse
    import random as _random
    global random
    random = _random

    parser = argparse.ArgumentParser(
        description="符玄 Live2D 动作参数映射器 — 将开源动作数据映射到符玄参数空间")
    parser.add_argument('--input', '-i', type=str, default=None,
                        help='输入动作文件或目录（.mtn/.motion3.json）')
    parser.add_argument('--output', '-o', type=str,
                        default='seed_data.json',
                        help='输出 JSON 文件路径')
    parser.add_argument('--map', '-m', type=str,
                        default=None,
                        help='fuxuan_map.json 路径（自动查找）')
    parser.add_argument('--batch', action='store_true',
                        help='批量处理模式')
    parser.add_argument('--synthesize', action='store_true',
                        help='合成手工模板动作')
    parser.add_argument('--variation', type=float, default=0.1,
                        help='动作随机变化量 (0~1)')
    parser.add_argument('--score', type=int, default=4,
                        help='种子数据默认评分 (1-5)')
    parser.add_argument('--list-mappings', action='store_true',
                        help='列出所有已知参数映射')
    parser.add_argument('--validate', type=str, default=None,
                        help='验证指定动作文件')

    args = parser.parse_args()

    # 查找 fuxuan_map.json
    map_path = args.map
    if not map_path:
        candidates = [
            'Assets/Resources/Live2D/ParamMaps/fuxuan_map.json',
            '../code/desktop_unity/Assets/Resources/Live2D/ParamMaps/fuxuan_map.json',
            '../../code/desktop_unity/Assets/Resources/Live2D/ParamMaps/fuxuan_map.json',
        ]
        for c in candidates:
            if os.path.exists(c):
                map_path = c
                break

    if map_path and os.path.exists(map_path):
        target = TargetModel(map_path)
        scaler = ParamScaler(target)
        synth = MotionSynthesizer(target, scaler)
    else:
        print("⚠️  未找到 fuxuan_map.json，将使用默认参数范围")
        target = None
        scaler = ParamScaler.__new__(ParamScaler)
        scaler.target = None

    # 特殊命令
    if args.list_mappings:
        print("\n=== 已知参数映射表 ===")
        seen = set()
        for k, v in sorted(FUZZY_PARAM_MAP.items()):
            if v not in seen:
                seen.add(v)
                source_ranges = ParamScaler.SOURCE_RANGES.get(v, "(0,1)")
                if target:
                    tgt = target.params.get(v)
                    tgt_range = f"[{tgt.min},{tgt.max}]" if tgt else "—"
                else:
                    tgt_range = "—"
                print(f"  {v:25s}  源范围={str(source_ranges):15s}  目标范围={tgt_range}")
        return 0

    if args.validate:
        fpath = args.validate
        print(f"\n验证: {fpath}")
        motion = parse_action_file(fpath)
        if motion:
            if scaler:
                motion = scaler.scale_motion(motion)
            print(f"  ✅ 成功: {motion.description}")
            print(f"  totalDuration: {motion.total_duration}")
            print(f"  keyframes: {len(motion.keyframes)}")
            for i, kf in enumerate(motion.keyframes):
                vals = ", ".join(f"{k}={v:.1f}" for k, v in list(kf.values.items())[:6])
                print(f"    [{i}] t={kf.time:.1f}: {vals}{'...' if len(kf.values) > 6 else ''}")
            # 输出完整 JSON
            print(f"\n  --- 完整 JSON ---")
            print(json.dumps(motion.to_dict(), ensure_ascii=False, indent=2))
        else:
            print(f"  ❌ 解析失败")
        return 0

    all_motions: List[ParsedMotion] = []

    # 1) 合成手工模板
    if args.synthesize:
        print("\n=== 合成手工模板动作 ===")
        syn = synth.synthesize_all(variation=args.variation)
        all_motions.extend(syn)
        print(f"共合成 {len(syn)} 个动作")

    # 2) 处理输入文件/目录
    if args.input:
        if os.path.isfile(args.input):
            print(f"\n=== 解析文件: {args.input} ===")
            motion = parse_action_file(args.input)
            if motion and scaler:
                motion = scaler.scale_motion(motion)
            if motion:
                all_motions.append(motion)
                print(f"  ✅ 成功")
        elif os.path.isdir(args.input):
            motions = process_directory(args.input, target, scaler)
            all_motions.extend(motions)
            print(f"\n共解析 {len(motions)} 个动作")

    # 3) 输出
    if not all_motions:
        print("\n⚠️  没有生成任何动作。使用 --synthesize 合成模板或 --input 指定文件。")
        return 1

    entries = []
    for motion in all_motions:
        entry = format_as_memory_entry(motion, score=args.score,
                                        action_name=motion.description)
        entries.append(entry)

    output_json = format_as_injection_json(entries)

    with open(args.output, 'w', encoding='utf-8') as f:
        f.write(output_json)

    print(f"\n=== 完成! ===")
    print(f"输出文件: {args.output}")
    print(f"动作数量: {len(entries)}")

    # 统计
    parts_used = set()
    params_used = set()
    for motion in all_motions:
        for kf in motion.keyframes:
            for sem in kf.values:
                params_used.add(sem)
                if target and sem in target.params:
                    parts_used.add(target.params[sem].part)

    print(f"涉及参数: {len(params_used)} 个")
    print(f"涉及部位: {', '.join(sorted(parts_used))}")

    # 验证：检查哪些目标参数没有覆盖
    if target:
        uncovered = set(target.params.keys()) - params_used
        print(f"未覆盖参数: {len(uncovered)} 个")
        if uncovered:
            print(f"  例如: {', '.join(sorted(uncovered)[:10])}")

    return 0


if __name__ == '__main__':
    exit(main())
