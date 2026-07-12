#!/usr/bin/env python3
import sys

filepath = sys.argv[1]
print(f"File: {filepath}")

# Read raw bytes
with open(filepath, 'rb') as f:
    raw = f.read(200)

print(f"First 20 bytes: {list(raw[:20])}")

# Try utf-8-sig
content = raw.decode('utf-8-sig', errors='replace')
lines = content.strip().split('\n')
print(f"Lines: {len(lines)}")
print(f"Line 0 repr: {repr(lines[0][:80])}")
print(f"Starts with #: {lines[0].strip().startswith('#')}")

# Check each line
param_count = 0
for i, line in enumerate(lines[:30]):
    line_s = line.strip()
    if '=' in line_s and not line_s.startswith('$') and not line_s.startswith('#'):
        name, vals = line_s.split('=', 1)
        vals_list = [v.strip() for v in vals.split(',') if v.strip()]
        param_count += 1
        print(f"  Param {i}: {name} -> {len(vals_list)} values, first={vals_list[0]}")
print(f"Total params found: {param_count}")
