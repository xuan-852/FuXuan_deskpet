import json

# motion_memory
with open('C:/Users/25295/AppData/LocalLow/DefaultCompany/desktop pet/motion_memory.json','r',encoding='utf-8') as f:
    mm = json.load(f)
ents = mm.get('entries',[])
print(f'运动记忆: {len(ents)} 条')
sorted_ents = sorted(ents, key=lambda e: e.get('bestScore',0), reverse=True)
print('\n最高分动作TOP10:')
for e in sorted_ents[:10]:
    print(f'  {e.get("bestScore",0)}/5  {e.get("actionName","")}  (尝试{e.get("attempts",0)}次, 帧{e.get("keyframeCount",0)})')

# validation_log
with open('C:/Users/25295/AppData/LocalLow/DefaultCompany/desktop pet/validation_log.json','r',encoding='utf-8') as f:
    vl = json.load(f)
vents = vl.get('entries',[])
print(f'\n验证日志: {len(vents)} 条')
passes = [e for e in vents if e.get('isConsensus')]
fails = [e for e in vents if not e.get('isConsensus')]
print(f'通过(>=3/5): {len(passes)}')
print(f'低分(<3/5): {len(fails)}')
if passes:
    print('最近通过的:')
    for e in passes[-5:]:
        print(f'  ✅ {e.get("actionDescription","")} {e.get("scoreGlm",0)}/5')
if fails:
    print('最近低分的:')
    for e in fails[-3:]:
        print(f'  ❌ {e.get("actionDescription","")} {e.get("scoreGlm",0)}/5')
