import json

files = [
    'Assets/Resources/Live2D/ParamMaps/fuxuan_map.json',
    'Assets/Scripts/Live2DFramework/ParamMaps/fuxuan_map.json',
]

for fp in files:
    with open(fp, 'r', encoding='utf-8') as f:
        data = json.load(f)
    entries = data.get('entries', [])
    total = len(entries)
    # 's' = semantic, 'p' = paramId
    mapped = sum(1 for e in entries if e.get('s') and e['s'] != e['p'])
    unmapped = [e for e in entries if not e.get('s') or e['s'] == e['p']]
    print(f'=== {fp} ===')
    print(f'总参数: {total}')
    print(f'已映射: {mapped} ({mapped/total*100:.1f}%)')
    print(f'未映射: {len(unmapped)} ({len(unmapped)/total*100:.1f}%)')
    if unmapped:
        print('未映射参数:')
        for e in unmapped:
            print(f'  {e["p"]:20s} range=[{e.get("min",0)},{e.get("max",0)}]')
    print()
