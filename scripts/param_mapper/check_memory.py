import json, sys
sys.stdout.reconfigure(encoding='utf-8')

# Check existing
fp = r"C:\Users\25295\AppData\LocalLow\DefaultCompany\desktop pet\motion_memory.json"
with open(fp, 'r', encoding='utf-8') as f:
    data = json.load(f)

entries = data.get('entries', [])
print("现有 motion_memory.json:")
print("  条目数:", len(entries))
print()

# Show best scores
for i, e in enumerate(entries):
    print("  [%d] %-20s score=%d  attempts=%d  frames=%d" % (
        i, e.get('actionName',''), e.get('bestScore',0), e.get('attempts',0), e.get('keyframeCount',0)))
