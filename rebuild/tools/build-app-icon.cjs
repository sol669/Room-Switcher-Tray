// Deterministic preparation of the user-approved pink image; no redrawing.
// Usage: NODE_PATH=<sharp module parent> node build-app-icon.cjs input.png output-directory
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');

async function main() {
  const [input, output] = process.argv.slice(2);
  if (!input || !output) throw new Error('Expected source PNG and output directory.');
  const { data: rgb, info } = await sharp(input).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height } = info;
  const count = width * height;
  const chroma = new Uint8Array(count);
  for (let i = 0; i < count; i++) {
    const offset = i * 3;
    chroma[i] = Math.max(rgb[offset], rgb[offset + 1], rgb[offset + 2]) -
      Math.min(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
  }

  // Flood only low-chroma pixels connected to the outside. White enclosed in
  // the letter R stays opaque, unlike a global white/gray color-key operation.
  const outside = new Uint8Array(count);
  const queue = new Int32Array(count);
  let head = 0, tail = 0;
  const add = i => {
    if (!outside[i] && chroma[i] <= 18) { outside[i] = 1; queue[tail++] = i; }
  };
  for (let x = 0; x < width; x++) { add(x); add((height - 1) * width + x); }
  for (let y = 0; y < height; y++) { add(y * width); add(y * width + width - 1); }
  while (head < tail) {
    const i = queue[head++], x = i % width;
    if (x > 0) add(i - 1);
    if (x < width - 1) add(i + 1);
    if (i >= width) add(i - width);
    if (i < count - width) add(i + width);
  }

  const rgba = Buffer.alloc(count * 4);
  let kept = 0, opaqueWhite = 0;
  for (let i = 0; i < count; i++) {
    if (outside[i]) continue;
    const x = i % width, y = Math.floor(i / width), sourceOffset = i * 3, destinationOffset = i * 4;
    let alpha = 1, strongest = chroma[i], nearbyBackground = false, matte = 248;
    // Unmatte only the outer two-pixel fringe. All interior pixels, including
    // the white R and the shadow cast on the monitor, retain their exact RGB.
    for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) {
      const nx = x + dx, ny = y + dy;
      if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
      const n = ny * width + nx;
      if (outside[n]) { nearbyBackground = true; matte = Math.max(matte, rgb[n * 3]); }
      else strongest = Math.max(strongest, chroma[n]);
    }
    if (nearbyBackground && strongest > 0) alpha = Math.min(1, chroma[i] / strongest);
    for (let channel = 0; channel < 3; channel++) {
      rgba[destinationOffset + channel] = Math.max(0, Math.min(255,
        Math.round((rgb[sourceOffset + channel] - matte * (1 - alpha)) / Math.max(alpha, 0.001))));
    }
    rgba[destinationOffset + 3] = Math.round(alpha * 255);
    kept++;
    if (chroma[i] < 5 && rgb[sourceOffset] > 240 && alpha === 1) opaqueWhite++;
  }
  if (kept < count * 0.2 || kept > count * 0.8 || opaqueWhite < 1000) {
    throw new Error(`Unexpected extraction: ${kept} foreground pixels, ${opaqueWhite} white-letter pixels.`);
  }

  fs.mkdirSync(output, { recursive: true });
  const source = () => sharp(rgba, { raw: { width, height, channels: 4 } });
  await source().png().toFile(path.join(output, 'RoomSwitcher.png'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = await Promise.all(sizes.map(size => source().resize(size, size, { kernel: 'lanczos3' }).png().toBuffer()));
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  for (let i = 0; i < sizes.length; i++) {
    const entry = 6 + 16 * i;
    directory[entry] = directory[entry + 1] = sizes[i] === 256 ? 0 : sizes[i];
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frames[i].length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frames[i].length;
  }
  fs.writeFileSync(path.join(output, 'RoomSwitcher.ico'), Buffer.concat([directory, ...frames]));
  // Review-only contact sheet on light/dark backgrounds, including small sizes.
  const previews = [];
  for (let row = 0; row < 2; row++) {
    for (const [column, size] of [256, 64, 48, 32, 24, 16].entries()) {
      const left = [12, 285, 375, 449, 505, 549][column];
      previews.push({ input: await source().resize(size, size).png().toBuffer(), left, top: row * 280 + 12 });
    }
  }
  await sharp({ create: { width: 584, height: 560, channels: 4, background: '#fafafa' } })
    .composite([
      { input: await sharp({ create: { width: 584, height: 280, channels: 4, background: '#202020' } }).png().toBuffer(), left: 0, top: 280 },
      ...previews
    ]).png().toFile(path.join(output, 'preview.png'));
  console.log(`Prepared ${sizes.length} ICO sizes; transparent background; ${opaqueWhite} opaque white-letter pixels preserved.`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
