import json, os, datetime

result = json.load(open('temp_scan_results.json', 'r', encoding='utf-8'))

out_dir = 'D:/Unity/projects/Desktop_per_pro/code/desktop_unity/Assets/Resources/Live2D/ParamMaps'
os.makedirs(out_dir, exist_ok=True)

now = datetime.datetime.now().strftime('%Y-%m-%dT%H:%M:%S')

calibrations = []
for r in result['visionScanResults']:
    desc = r.get('description', '')
    
    bodyPart = ''
    visualChange = ''
    lines = desc.split('\n')
    for line in lines:
        if line.startswith('部位: '):
            bodyPart = line[4:].strip()
        elif line.startswith('变化: '):
            visualChange = line[4:].strip()
    
    visualDesc = desc.replace('\n', '；')
    
    entry = {
        'paramId': r['paramId'],
        'semantic': r.get('suggestedSemantic', ''),
        'min': r['min'],
        'max': r['max'],
        'defaultValue': 0.0,
        'bodyPart': bodyPart,
        'visualChange': visualChange,
        'visualDescription': visualDesc,
        'confidence': r.get('confidence', ''),
        'calibratedAt': now
    }
    calibrations.append(entry)

out = {
    'formatVersion': '1.0',
    'modelName': result['modelName'],
    'generatedAt': result['generatedAt'],
    'calibrations': calibrations
}

out_path = os.path.join(out_dir, 'vision_calibration.json')
with open(out_path, 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=2)

print(f'✓ 已写入 {len(calibrations)} 条标定数据到 {out_path}')
