/*
 * Measures internal Live2D pose change from cropped model-RT PNG frames.
 * Frames are bottom-centred on a common canvas, intentionally removing
 * desktop/root translation before calculating visual change.
 * Usage: node scripts/live2d-probe/analyze_perceptibility_sequence.cjs <png-dir>
 */
'use strict';
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const dir = process.argv[2];
if (!dir) throw new Error('Usage: analyze_perceptibility_sequence.cjs <png-dir>');

function pngFrame(file) {
  const bytes = fs.readFileSync(file);
  const signature = '89504e470d0a1a0a';
  if (bytes.subarray(0, 8).toString('hex') !== signature) throw new Error('Not a PNG: ' + file);
  let offset = 8, width, height, bitDepth, colorType, compressed = [];
  while (offset < bytes.length) {
    const size = bytes.readUInt32BE(offset); const type = bytes.toString('ascii', offset + 4, offset + 8);
    const data = bytes.subarray(offset + 8, offset + 8 + size); offset += 12 + size;
    if (type === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); bitDepth = data[8]; colorType = data[9]; }
    if (type === 'IDAT') compressed.push(data);
    if (type === 'IEND') break;
  }
  if (bitDepth !== 8 || (colorType !== 2 && colorType !== 6)) throw new Error(`Unsupported PNG format in ${file}: depth=${bitDepth}, type=${colorType}`);
  const bytesPerPixel = colorType === 6 ? 4 : 3;
  const raw = zlib.inflateSync(Buffer.concat(compressed)); const stride = width * bytesPerPixel; const decoded = Buffer.alloc(stride * height);
  let source = 0;
  for (let y = 0; y < height; y++) {
    const filter = raw[source++]; const row = y * stride;
    for (let x = 0; x < stride; x++) {
      const value = raw[source++]; const left = x >= bytesPerPixel ? decoded[row + x - bytesPerPixel] : 0;
      const up = y ? decoded[row - stride + x] : 0; const upLeft = y && x >= bytesPerPixel ? decoded[row - stride + x - bytesPerPixel] : 0;
      if (filter === 0) decoded[row + x] = value;
      else if (filter === 1) decoded[row + x] = (value + left) & 255;
      else if (filter === 2) decoded[row + x] = (value + up) & 255;
      else if (filter === 3) decoded[row + x] = (value + Math.floor((left + up) / 2)) & 255;
      else if (filter === 4) { const p = left + up - upLeft, pa = Math.abs(p - left), pb = Math.abs(p - up), pc = Math.abs(p - upLeft); decoded[row + x] = (value + (pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft)) & 255; }
      else throw new Error('Unsupported PNG filter: ' + filter);
    }
  }
  const pixels = colorType === 6 ? decoded : Buffer.alloc(width * height * 4);
  if (colorType === 2) for (let sourceIndex = 0, targetIndex = 0; sourceIndex < decoded.length; sourceIndex += 3, targetIndex += 4) { pixels[targetIndex] = decoded[sourceIndex]; pixels[targetIndex + 1] = decoded[sourceIndex + 1]; pixels[targetIndex + 2] = decoded[sourceIndex + 2]; pixels[targetIndex + 3] = 255; }
  return { file: path.basename(file), width, height, pixels };
}

function normalize(frame, canvasWidth, canvasHeight) {
  if (frame.width > canvasWidth || frame.height > canvasHeight) throw new Error('Frame exceeds normalized canvas: ' + frame.file);
  const out = Buffer.alloc(canvasWidth * canvasHeight * 4);
  let minX = frame.width, maxX = -1;
  for (let y = 0; y < frame.height; y++) for (let x = 0; x < frame.width; x++) {
    const i = (y * frame.width + x) * 4;
    if (frame.pixels[i + 3] > 20 && frame.pixels[i] + frame.pixels[i + 1] + frame.pixels[i + 2] > 12) { minX = Math.min(minX, x); maxX = Math.max(maxX, x); }
  }
  if (maxX < minX) throw new Error('No visible model pixels: ' + frame.file);
  // Preserve vertical position (walk bounce), but translate horizontally so the
  // model's own centre is invariant. This excludes DesktopPet root travel only.
  const modelCenterX = (minX + maxX) / 2;
  const left = Math.round(canvasWidth / 2 - modelCenterX), top = canvasHeight - frame.height;
  for (let y = 0; y < frame.height; y++) for (let x = 0; x < frame.width; x++) {
    const targetX = left + x; if (targetX < 0 || targetX >= canvasWidth) continue;
    frame.pixels.copy(out, ((top + y) * canvasWidth + targetX) * 4, (y * frame.width + x) * 4, (y * frame.width + x + 1) * 4);
  }
  return { ...frame, canvasWidth, canvasHeight, modelCenterX, pixels: out };
}

function compare(a, b) {
  let rgb = 0, union = 0, alphaDelta = 0, silhouette = 0;
  for (let i = 0; i < a.pixels.length; i += 4) {
    const aa = a.pixels[i + 3], ba = b.pixels[i + 3];
    // Unity's PNG encoder may emit opaque RGB even for a transparent RT.
    // A low RGB threshold therefore defines the model mask for both formats.
    const aVisible = aa > 20 && a.pixels[i] + a.pixels[i + 1] + a.pixels[i + 2] > 12;
    const bVisible = ba > 20 && b.pixels[i] + b.pixels[i + 1] + b.pixels[i + 2] > 12;
    if (aVisible || bVisible) { union++; rgb += Math.abs(a.pixels[i] - b.pixels[i]) + Math.abs(a.pixels[i + 1] - b.pixels[i + 1]) + Math.abs(a.pixels[i + 2] - b.pixels[i + 2]); alphaDelta += Math.abs(aa - ba); if (aVisible !== bVisible) silhouette++; }
  }
  return { rgbMeanDelta: +(rgb / Math.max(1, union * 3)).toFixed(4), alphaMeanDelta: +(alphaDelta / Math.max(1, union)).toFixed(4), silhouetteChangeRatio: +(silhouette / Math.max(1, union)).toFixed(6), unionPixels: union };
}

const files = fs.readdirSync(dir).filter(name => name.toLowerCase().endsWith('.png')).sort();
if (files.length < 2) throw new Error('Need at least two PNG frames.');
const rawFrames = files.map(name => pngFrame(path.join(dir, name)));
const canvasWidth = Math.max(...rawFrames.map(frame => frame.width));
const canvasHeight = Math.max(...rawFrames.map(frame => frame.height));
const frames = rawFrames.map(frame => normalize(frame, canvasWidth, canvasHeight));
const adjacent = frames.slice(1).map((frame, index) => ({ from: frames[index].file, to: frame.file, ...compare(frames[index], frame) }));
const againstFirst = frames.slice(1).map(frame => ({ from: frames[0].file, to: frame.file, ...compare(frames[0], frame) }));
function stat(rows, key, kind) { return +(kind === 'mean' ? rows.reduce((sum, row) => sum + row[key], 0) / rows.length : Math.max(...rows.map(row => row[key]))).toFixed(4); }
const report = { method: 'maximum model-RT canvas, horizontal silhouette-centre alignment; root/window translation excluded while walk bounce is retained', canvas: { width: canvasWidth, height: canvasHeight }, frames: frames.map(({ file, width, height, modelCenterX }) => ({ file, width, height, modelCenterX })), adjacent, againstFirst, summary: { adjacentRgbMean: stat(adjacent, 'rgbMeanDelta', 'mean'), adjacentRgbPeak: stat(adjacent, 'rgbMeanDelta', 'max'), phaseRgbPeak: stat(againstFirst, 'rgbMeanDelta', 'max'), phaseSilhouettePeak: stat(againstFirst, 'silhouetteChangeRatio', 'max') } };
const output = path.join(dir, 'perceptibility-report.json'); fs.writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ output, summary: report.summary }, null, 2));
