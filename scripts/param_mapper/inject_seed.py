#!/usr/bin/env python3
"""
种子数据注入脚本 — 将 seed_combined.json 写入 motion_memory.json
供 MotionMemoryManager 加载

用法:
  python inject_seed.py --input seed_combined.json \
                        --output <Unity persistentDataPath>/motion_memory.json
"""

import json
import os
import sys
import argparse
from datetime import datetime
from typing import List, Dict, Any


def load_entries(input_path: str) -> List[Dict[str, Any]]:
    """加载 seed_combined.json 中的条目"""
    with open(input_path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    return data.get('entries', [])


def build_motion_memory(entries: List[Dict[str, Any]], 
                         score: int = 4,
                         review: str = "离线注入的高质量种子数据") -> Dict[str, Any]:
    """
    将种子条目转换为 motion_memory.json 格式
    
    MotionMemoryManager.StorageData 格式:
    {
      "entries": [
        {
          "actionName": "动作名",
          "bestParamJson": "{...keyframe JSON...}",
          "bestScore": 4,
          "bestReview": "评语",
          "lastParamSnapshot": "{...keyframe JSON...}",
          "timestamp": "2024-01-01 12:00",
          "attempts": 1,
          "totalDuration": 3.5,
          "keyframeCount": 6,
          "scoreHistory": [4]
        }
      ]
    }
    """
    now = datetime.now().strftime("%Y-%m-%d %H:%M")
    result = {"entries": []}
    
    for entry in entries:
        # 提取 bestParamJson 中的 duration 和 keyframeCount
        best_json_str = entry.get('bestParamJson', '{}')
        try:
            best_data = json.loads(best_json_str)
            duration = best_data.get('totalDuration', entry.get('totalDuration', 3.0))
            kf_count = len(best_data.get('keyframes', []))
        except (json.JSONDecodeError, TypeError):
            duration = entry.get('totalDuration', 3.0)
            kf_count = entry.get('keyframeCount', 0)
        
        action_name = entry.get('actionName', '')
        if not action_name:
            continue
        
        motion_entry = {
            "actionName": action_name,
            "bestParamJson": best_json_str,
            "bestScore": entry.get('bestScore', score),
            "bestReview": entry.get('bestReview', review),
            "lastParamSnapshot": entry.get('lastParamSnapshot', best_json_str),
            "timestamp": entry.get('timestamp', now),
            "attempts": entry.get('attempts', 1),
            "totalDuration": duration,
            "keyframeCount": kf_count,
            "scoreHistory": entry.get('scoreHistory', [score])
        }
        result["entries"].append(motion_entry)
    
    return result


def main():
    parser = argparse.ArgumentParser(description="注入种子数据到 motion_memory.json")
    parser.add_argument('--input', '-i', required=True,
                        help='seed_combined.json 路径')
    parser.add_argument('--output', '-o', required=True,
                        help='输出 motion_memory.json 路径')
    parser.add_argument('--score', '-s', type=int, default=4,
                        help='种子评分 (1-5)，默认 4')
    parser.add_argument('--review', '-r', default='离线注入的高质量种子数据',
                        help='种子评语')
    args = parser.parse_args()
    
    # 加载
    entries = load_entries(args.input)
    print(f"✅ 加载 {len(entries)} 个种子动作")
    
    # 转换
    motion_memory = build_motion_memory(
        entries, 
        score=args.score, 
        review=args.review
    )
    
    # 统计
    valid = len(motion_memory["entries"])
    print(f"✅ 转换完成: {valid} 个可用条目")
    
    # 统计部位覆盖率
    params_covered = set()
    for entry in motion_memory["entries"]:
        try:
            bj = json.loads(entry["bestParamJson"])
            for kf in bj.get("keyframes", []):
                for sem in kf.get("values", {}):
                    params_covered.add(sem)
        except (json.JSONDecodeError, TypeError):
            pass
    
    # 输出到文件
    os.makedirs(os.path.dirname(args.output) or '.', exist_ok=True)
    with open(args.output, 'w', encoding='utf-8') as f:
        json.dump(motion_memory, f, ensure_ascii=False, indent=2)
    
    print(f"✅ 写入: {args.output}")
    print(f"📊 动作数: {valid}")
    print(f"📊 参数覆盖: {len(params_covered)} 个语义参数")
    
    # 列出所有动作名
    print("\n📋 注入清单:")
    for i, e in enumerate(motion_memory["entries"]):
        print(f"  [{i+1:2d}] {e['actionName']:20s}  score={e['bestScore']}  "
              f"frames={e['keyframeCount']}  dur={e['totalDuration']:.1f}s")


if __name__ == '__main__':
    main()
