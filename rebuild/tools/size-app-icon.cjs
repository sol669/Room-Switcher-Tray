// Resize the existing transparent artwork, preserving its colors and geometry.
// Usage: NODE_PATH=<sharp parent> node size-app-icon.cjs Assets/AppIcon
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
async function main() {
  const folder = process.argv[2];
  const input = path.join(folder, 'RoomSwitcher.png');
  const { data, info } = await sharp(input).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  let left = info.width, top = info.height, right = -1, bottom = -1;
  for (let y = 0; y < info.height; y++) for (let x = 0; x < info.width; x++) {
    if (data[(y * info.width + x) * 4 + 3] <= 8) continue;
    left = Math.min(left, x); top = Math.min(top, y); right = Math.max(right, x); bottom = Math.max(bottom, y);
  }
  if (right < left) throw new Error('Empty icon');
  const normalized = await sharp(data, { raw: info }).extract({ left, top, width: right-left+1, height: bottom-top+1 })
    .resize(976, 976, { fit: 'contain', background: '#00000000', kernel: 'lanczos3' })
    .extend({ top: 24, bottom: 24, left: 24, right: 24, background: '#00000000' }).png().toBuffer();
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = await Promise.all(sizes.map(async size => {
    const pixels = await sharp(normalized).resize(size, size).ensureAlpha().raw().toBuffer();
    // Suppress near-transparent Lanczos ringing in the empty canvas, especially at 20px.
    for (let i=0; i<pixels.length; i+=4) if (pixels[i+3] <= 8) pixels.fill(0, i, i+4);
    return sharp(pixels, {raw:{width:size,height:size,channels:4}}).png().toBuffer();
  }));
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2); directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  frames.forEach((frame, i) => {
    const entry = 6 + i*16;
    directory[entry] = directory[entry+1] = sizes[i] === 256 ? 0 : sizes[i];
    directory.writeUInt16LE(1, entry+4); directory.writeUInt16LE(32, entry+6);
    directory.writeUInt32LE(frame.length, entry+8); directory.writeUInt32LE(offset, entry+12); offset += frame.length;
  });
  fs.writeFileSync(input, normalized);
  fs.writeFileSync(path.join(folder, 'RoomSwitcher.ico'), Buffer.concat([directory, ...frames]));
  const previews = [];
  for (let row=0; row<2; row++) for (const [column, size] of [256,64,48,32,24,16].entries())
    previews.push({ input: await sharp(normalized).resize(size,size).png().toBuffer(), left:[12,285,375,449,505,549][column], top:row*280+12 });
  await sharp({create:{width:584,height:560,channels:4,background:'#fafafa'}}).composite([
    {input:await sharp({create:{width:584,height:280,channels:4,background:'#202020'}}).png().toBuffer(),left:0,top:280}, ...previews
  ]).png().toFile(path.join(folder,'preview.png'));
  console.log('Icon: 95.3% artwork bounds; 9 transparent ICO frames.');
}
main().catch(error => { console.error(error); process.exitCode=1; });
