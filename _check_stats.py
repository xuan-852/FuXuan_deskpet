import json, os, datetime
from collections import Counter

def safe_decode(s):
    if isinstance(s, str):
        try:
            return s.encode('latin1').decode('utf-8')
        except:
            return s
    return s

# === 动作记忆 ===
path = r'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\motion_memory.json'
with open(path, 'r', encoding='utf-8') as f:
    raw = json.load(f)
if isinstance(raw, dict) and 'entries' in raw:
    mem = raw['entries']
elif isinstance(raw, list):
    mem = raw
else:
    mem = [raw]
print(f'=== 动作记忆 ({len(mem)} 条) ===')
mem_by_score = sorted(mem, key=lambda x: x.get('bestScore', x.get('score', 0)), reverse=True)
for m in mem_by_score[:8]:
    s = m.get('bestScore', m.get('score', '?'))
    act = safe_decode(m.get('actionName', m.get('action', '?')))
    cnt = m.get('attempts', m.get('count', 1))
    print(f"  [{s}/5] {act}  (尝试:{cnt})")
if mem_by_score:
    best = mem_by_score[0]
    bscore = best.get('bestScore', best.get('score', '?'))
    bact = safe_decode(best.get('actionName', best.get('action', '?')))
    print(f"  最高分: {bscore}/5 — {bact}")
    scores = [m.get('bestScore', m.get('score', 0)) for m in mem]
    avg = sum(scores) / len(scores)
    print(f"  平均分: {avg:.2f}/5")

# === 验证日志 ===
path2 = r'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\validation_log.json'
with open(path2, 'r', encoding='utf-8') as f:
    val = json.load(f)
total = len(val)
dict_vals = [v for v in val if isinstance(v, dict)]
passed = sum(1 for v in dict_vals if v.get('passed'))
failed = total - passed
print(f'\n=== GLM 验证 ({total} 条) ===')
print(f"  通过: {passed}, 失败: {failed}, 通过率: {passed/total*100:.1f}%")
if dict_vals and failed > 0:
    fails = [v for v in dict_vals if not v.get('passed')]
    if fails:
        print(f"  最近5条失败:")
        for v in fails[-5:]:
            reason = v.get('reason','') or v.get('error','')
            print(f"    {safe_decode(reason[:80])}")

# === 活动日志 ===
path3 = r'C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\activity_log.json'
with open(path3, 'r', encoding='utf-8') as f:
    act = json.load(f)
print(f'\n=== 活动日志 ({len(act)} 条) ===')
if len(act) > 0:
    acts = Counter(a.get('description','?') for a in act if isinstance(a, dict))
    top = acts.most_common(5)
    print(f"  最频繁动作:")
    for a, c in top:
        print(f"    [{c}次] {safe_decode(a[:60])}")

# === 进程信息 ===
try:
    import psutil
    for p in psutil.process_iter(['pid','name','create_time','memory_info']):
        if p.info['name'] and 'DesktopPet' in p.info['name']:
            uptime = datetime.datetime.now() - datetime.datetime.fromtimestamp(p.info['create_time'])
            mb = p.info['memory_info'].rss / 1024 / 1024
            print(f'\n=== 进程 ===')
            print(f"  PID: {p.info['pid']}, 运行: {str(uptime).split('.')[0]}, 内存: {mb:.0f}MB")
except Exception as e:
    print(f'\n(psutil: {e})')
