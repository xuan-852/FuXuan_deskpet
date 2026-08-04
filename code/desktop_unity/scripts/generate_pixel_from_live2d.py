from PIL import Image, ImageOps
import os

candidates = [
    r"code/desktop_unity/Assets/Live2D/Models/Fuxuan/符玄.4096/texture_00.png",
    r"code/desktop_unity/Assets/StreamingAssets/Live2D/Fuxuan/符玄.4096/texture_00.png",
    r"code/desktop_unity/Assets/Live2D/Models/Fuxuan/符玄.4096/texture_01.png",
]

root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
found = None
for p in candidates:
    full = os.path.join(root, p)
    if os.path.exists(full):
        found = full
        break

if not found:
    print("No source Live2D texture found. Checked candidates:")
    for p in candidates:
        print(" - ", os.path.join(root, p))
    raise SystemExit(1)

print("Using source:", found)
img = Image.open(found).convert("RGBA")

# try to find non-transparent bbox
alpha = img.split()[-1]
bbox = alpha.getbbox()
if not bbox:
    # fallback: use luminosity threshold
    gray = img.convert("L")
    mask = gray.point(lambda p: 255 if p < 240 else 0)
    bbox = mask.getbbox()

if not bbox:
    # give up, use center crop
    w,h = img.size
    s = min(w,h)
    left = (w-s)//2
    top = (h-s)//2
    bbox = (left, top, left+s, top+s)

crop = img.crop(bbox)
# Make square by padding
cw, ch = crop.size
side = max(cw, ch)
sq = Image.new("RGBA", (side, side), (0,0,0,0))
px = (side-cw)//2
py = (side-ch)//2
sq.paste(crop, (px,py))

# Resize to target pixel size
TARGET = 32
small = sq.resize((TARGET, TARGET), resample=Image.NEAREST)

# Optional: posterize using quantize to reduce colors
# Keep transparency: separate alpha
alpha = small.split()[-1]
rgb = small.convert('RGB').quantize(colors=24, method=Image.FASTOCTREE)
rgb = rgb.convert('RGBA')
rgb.putalpha(alpha)

# Save to Resources
out_dir = os.path.join(root, 'Assets', 'Resources')
if not os.path.exists(out_dir):
    os.makedirs(out_dir)
out_path = os.path.join(out_dir, 'PixelFuXuan.png')
rgb.save(out_path, optimize=True)
print('Saved pixel avatar to', out_path)

# Also save a preview big-upscaled for quick inspection
preview = rgb.resize((TARGET*8, TARGET*8), resample=Image.NEAREST)
preview_path = os.path.join(out_dir, 'PixelFuXuan_preview.png')
preview.save(preview_path)
print('Saved preview to', preview_path)
