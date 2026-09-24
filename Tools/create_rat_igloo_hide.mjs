import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const root = process.cwd();
const outDir = path.join(root, 'meshy_output', 'rat_igloo_hide');
fs.mkdirSync(outDir, { recursive: true });

const meshes = [];
const nodes = [];

function addMesh(name, positions, normals, uvs, indices, material, extras = {}) {
  meshes.push({ name, positions, normals, uvs, indices, material });
  nodes.push({ name, mesh: meshes.length - 1, extras });
}

function pushQuad(positions, normals, uvs, indices, corners, normal, uvMode = 0) {
  const base = positions.length / 3;
  for (let i = 0; i < 4; i++) {
    positions.push(...corners[i]);
    normals.push(...normal);
    const uv = uvMode === 0 ? [[0, 0], [1, 0], [1, 1], [0, 1]][i] : [[0, 0], [0, 1], [1, 1], [1, 0]][i];
    uvs.push(...uv);
  }
  indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
}

function addBox(name, center, size, material, extras = {}) {
  const [cx, cy, cz] = center;
  const [sx, sy, sz] = size;
  const hx = sx / 2; const hy = sy / 2; const hz = sz / 2;
  const faces = [
    { normal: [0, 1, 0], corners: [[-hx, hy, -hz], [hx, hy, -hz], [hx, hy, hz], [-hx, hy, hz]] },
    { normal: [0, -1, 0], corners: [[-hx, -hy, -hz], [-hx, -hy, hz], [hx, -hy, hz], [hx, -hy, -hz]] },
    { normal: [0, 0, 1], corners: [[-hx, -hy, hz], [hx, -hy, hz], [hx, hy, hz], [-hx, hy, hz]] },
    { normal: [0, 0, -1], corners: [[hx, -hy, -hz], [-hx, -hy, -hz], [-hx, hy, -hz], [hx, hy, -hz]] },
    { normal: [1, 0, 0], corners: [[hx, -hy, hz], [hx, -hy, -hz], [hx, hy, -hz], [hx, hy, hz]] },
    { normal: [-1, 0, 0], corners: [[-hx, -hy, -hz], [-hx, -hy, hz], [-hx, hy, hz], [-hx, hy, -hz]] },
  ];
  const positions = []; const normals = []; const uvs = []; const indices = [];
  for (const face of faces) {
    const corners = face.corners.map(([x, y, z]) => [x + cx, y + cy, z + cz]);
    pushQuad(positions, normals, uvs, indices, corners, face.normal);
  }
  addMesh(name, positions, normals, uvs, indices, material, extras);
}

function addCylinder(name, center, radius, height, sides, material, extras = {}) {
  const [cx, cy, cz] = center;
  const positions = []; const normals = []; const uvs = []; const indices = [];
  for (let i = 0; i < sides; i++) {
    const a0 = (i / sides) * Math.PI * 2;
    const a1 = ((i + 1) / sides) * Math.PI * 2;
    const x0 = Math.cos(a0) * radius; const z0 = Math.sin(a0) * radius;
    const x1 = Math.cos(a1) * radius; const z1 = Math.sin(a1) * radius;
    pushQuad(positions, normals, uvs, indices,
      [[cx + x0, cy - height / 2, cz + z0], [cx + x1, cy - height / 2, cz + z1], [cx + x1, cy + height / 2, cz + z1], [cx + x0, cy + height / 2, cz + z0]],
      [Math.cos((a0 + a1) / 2), 0, Math.sin((a0 + a1) / 2)], 1);
    const topBase = positions.length / 3;
    positions.push(cx, cy + height / 2, cz, cx + x1, cy + height / 2, cz + z1, cx + x0, cy + height / 2, cz + z0);
    normals.push(0, 1, 0, 0, 1, 0, 0, 1, 0);
    uvs.push(0.5, 0.5, 0.5 + x1 / (radius * 2), 0.5 + z1 / (radius * 2), 0.5 + x0 / (radius * 2), 0.5 + z0 / (radius * 2));
    indices.push(topBase, topBase + 1, topBase + 2);
    const bottomBase = positions.length / 3;
    positions.push(cx, cy - height / 2, cz, cx + x0, cy - height / 2, cz + z0, cx + x1, cy - height / 2, cz + z1);
    normals.push(0, -1, 0, 0, -1, 0, 0, -1, 0);
    uvs.push(0.5, 0.5, 0.5 + x0 / (radius * 2), 0.5 + z0 / (radius * 2), 0.5 + x1 / (radius * 2), 0.5 + z1 / (radius * 2));
    indices.push(bottomBase, bottomBase + 1, bottomBase + 2);
  }
  addMesh(name, positions, normals, uvs, indices, material, extras);
}

function addTorus(name, center, majorRadius, minorRadius, majorSegments, minorSegments, material, extras = {}) {
  const [cx, cy, cz] = center;
  const positions = []; const normals = []; const uvs = []; const indices = [];
  for (let i = 0; i < majorSegments; i++) {
    const a0 = (i / majorSegments) * Math.PI * 2;
    const a1 = ((i + 1) / majorSegments) * Math.PI * 2;
    for (let j = 0; j < minorSegments; j++) {
      const b0 = (j / minorSegments) * Math.PI * 2;
      const b1 = ((j + 1) / minorSegments) * Math.PI * 2;
      const point = (a, b) => {
        const r = majorRadius + minorRadius * Math.cos(b);
        return [cx + r * Math.cos(a), cy + minorRadius * Math.sin(b), cz + r * Math.sin(a)];
      };
      const normal = (a, b) => [Math.cos(b) * Math.cos(a), Math.sin(b), Math.cos(b) * Math.sin(a)];
      pushQuad(positions, normals, uvs, indices, [point(a0, b0), point(a1, b0), point(a1, b1), point(a0, b1)], normal((a0 + a1) / 2, (b0 + b1) / 2));
    }
  }
  addMesh(name, positions, normals, uvs, indices, material, extras);
}

function addIglooShell() {
  const rings = [
    [0.14, 1.96], [0.28, 2.08], [0.76, 2.07], [1.20, 2.02], [1.48, 1.90], [1.70, 1.54], [1.86, 0.72],
  ];
  const segments = 72;
  const positions = []; const normals = []; const uvs = []; const indices = [];
  const halfWidthAt = (y) => {
    const spring = 0.78;
    const radius = 0.76;
    if (y <= spring) return radius;
    if (y >= spring + radius) return 0;
    return Math.sqrt(Math.max(0, radius * radius - (y - spring) * (y - spring)));
  };
  for (let r = 0; r < rings.length - 1; r++) {
    const [y0, radius0] = rings[r];
    const [y1, radius1] = rings[r + 1];
    for (let s = 0; s < segments; s++) {
      const a0 = (s / segments) * Math.PI * 2;
      const a1 = ((s + 1) / segments) * Math.PI * 2;
      const ym = (y0 + y1) / 2;
      const rm = (radius0 + radius1) / 2;
      const xm = rm * Math.cos((a0 + a1) / 2);
      const zm = rm * Math.sin((a0 + a1) / 2);
      const opening = zm > 0.45 && Math.abs(xm) < halfWidthAt(ym);
      if (opening) continue;
      const p = [[radius0 * Math.cos(a0), y0, radius0 * Math.sin(a0)], [radius0 * Math.cos(a1), y0, radius0 * Math.sin(a1)], [radius1 * Math.cos(a1), y1, radius1 * Math.sin(a1)], [radius1 * Math.cos(a0), y1, radius1 * Math.sin(a0)]];
      const n = p.map(([x, y, z]) => {
        const length = Math.hypot(x, z) || 1;
        return [x / length, 0.24, z / length];
      });
      const base = positions.length / 3;
      for (let i = 0; i < 4; i++) { positions.push(...p[i]); normals.push(...n[i]); uvs.push((s + (i > 0 && i < 3 ? 1 : 0)) / segments, (r + (i >= 2 ? 1 : 0)) / (rings.length - 1)); }
      indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }
  }
  addMesh('Translucent Igloo Shell', positions, normals, uvs, indices, 0, { role: 'rounded translucent plastic shell', entrance: 'arched opening facing +Z' });
}

function archPath(halfWidth, springY, archRadius, bottom, segments = 12) {
  const points = [[-halfWidth, bottom], [-halfWidth, springY]];
  for (let i = 0; i <= segments; i++) {
    const a = Math.PI - (i / segments) * Math.PI;
    points.push([Math.cos(a) * halfWidth, springY + Math.sin(a) * archRadius]);
  }
  points.push([halfWidth, bottom]);
  return points;
}

function addArchTunnel() {
  const outer = archPath(0.94, 0.76, 0.72, 0.08);
  const inner = archPath(0.72, 0.76, 0.53, 0.08);
  const zBack = 1.77;
  const zFront = 2.68;
  const positions = []; const normals = []; const uvs = []; const indices = [];
  for (let i = 0; i < outer.length - 1; i++) {
    const o0 = outer[i]; const o1 = outer[i + 1]; const in0 = inner[i]; const in1 = inner[i + 1];
    pushQuad(positions, normals, uvs, indices, [[o0[0], o0[1], zFront], [o1[0], o1[1], zFront], [in1[0], in1[1], zFront], [in0[0], in0[1], zFront]], [0, 0, 1]);
    pushQuad(positions, normals, uvs, indices, [[o1[0], o1[1], zBack], [o0[0], o0[1], zBack], [in0[0], in0[1], zBack], [in1[0], in1[1], zBack]], [0, 0, -1]);
    pushQuad(positions, normals, uvs, indices, [[o0[0], o0[1], zBack], [o1[0], o1[1], zBack], [o1[0], o1[1], zFront], [o0[0], o0[1], zFront]], [o0[0], 0.2, 0]);
    pushQuad(positions, normals, uvs, indices, [[in1[0], in1[1], zBack], [in0[0], in0[1], zBack], [in0[0], in0[1], zFront], [in1[0], in1[1], zFront]], [-in0[0], 0.2, 0]);
  }
  addMesh('Arched Entrance Tunnel', positions, normals, uvs, indices, 0, { role: 'translucent arched entrance collar' });
}

addIglooShell();
addCylinder('Interior Floor', [0, 0.10, 0], 1.86, 0.12, 64, 1, { role: 'dark translucent interior floor' });
addTorus('Lower Base Ring', [0, 0.14, 0], 2.02, 0.085, 64, 8, 1, { role: 'thick plastic base rim' });
addTorus('Body Seam Lower', [0, 0.69, 0], 2.06, 0.025, 64, 6, 1, { role: 'molded plastic seam' });
addTorus('Body Seam Upper', [0, 1.25, 0], 1.96, 0.022, 64, 6, 1, { role: 'molded plastic seam' });
addArchTunnel();
addCylinder('Top Platform', [0, 1.89, 0], 0.76, 0.09, 32, 0, { role: 'top vent platform' });

for (let i = 0; i < 8; i++) {
  const angle = (i / 8) * Math.PI * 2 + Math.PI / 8;
  const radius = 1.30;
  addCylinder(`Top Vent Post ${i + 1}`, [Math.cos(angle) * radius, 2.03, Math.sin(angle) * radius], 0.27, 0.38, 8, 0, { role: 'raised top vent post' });
}
for (let i = 0; i < 6; i++) {
  const angle = (i / 6) * Math.PI * 2;
  const radius = 0.43;
  addCylinder(`Vent Hole ${i + 1}`, [Math.cos(angle) * radius, 1.95, Math.sin(angle) * radius], 0.075, 0.018, 16, 2, { role: 'vent opening' });
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function makePng(width, height, pixelFn) {
  const pixels = Buffer.alloc(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const color = pixelFn(x, y);
    const i = (y * width + x) * 4;
    pixels[i] = color[0]; pixels[i + 1] = color[1]; pixels[i + 2] = color[2]; pixels[i + 3] = color[3] ?? 255;
  }
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) { raw[y * (width * 4 + 1)] = 0; pixels.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4); }
  const chunk = (type, data) => { const h = Buffer.alloc(8); h.writeUInt32BE(data.length, 0); h.write(type, 4, 4, 'ascii'); const c = Buffer.alloc(4); c.writeUInt32BE(crc32(Buffer.concat([Buffer.from(type, 'ascii'), data])), 0); return Buffer.concat([h, data, c]); };
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([signature, chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

function makePreview() {
  const width = 800; const height = 600; const pixels = Buffer.alloc(width * height * 4);
  const set = (x, y, c) => { if (x < 0 || y < 0 || x >= width || y >= height) return; const i = (y * width + x) * 4; pixels[i] = c[0]; pixels[i + 1] = c[1]; pixels[i + 2] = c[2]; pixels[i + 3] = c[3] ?? 255; };
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) set(x, y, [245, 246, 244, 255]);
  const blend = (base, over, alpha) => [Math.round(base[0] * (1 - alpha) + over[0] * alpha), Math.round(base[1] * (1 - alpha) + over[1] * alpha), Math.round(base[2] * (1 - alpha) + over[2] * alpha), 255];
  const ellipse = (cx, cy, rx, ry, color, alpha = 1) => { for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++) for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) { const dx = (x - cx) / rx; const dy = (y - cy) / ry; if (dx * dx + dy * dy <= 1) set(x, y, blend([245, 246, 244], color, alpha)); } };
  const poly = (points, color, alpha = 1) => { const minY = Math.max(0, Math.floor(Math.min(...points.map((p) => p[1])))); const maxY = Math.min(height - 1, Math.ceil(Math.max(...points.map((p) => p[1])))); for (let y = minY; y <= maxY; y++) { const xs = []; for (let i = 0; i < points.length; i++) { const a = points[i]; const b = points[(i + 1) % points.length]; if ((a[1] <= y && b[1] > y) || (b[1] <= y && a[1] > y)) xs.push(a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1])); } xs.sort((a, b) => a - b); for (let i = 0; i + 1 < xs.length; i += 2) for (let x = Math.ceil(xs[i]); x <= Math.floor(xs[i + 1]); x++) set(x, y, blend([245, 246, 244], color, alpha)); } };
  const line = (a, b, color, widthPx = 2) => { const n = Math.max(Math.abs(b[0] - a[0]), Math.abs(b[1] - a[1]), 1); for (let i = 0; i <= n; i++) { const t = i / n; const x = Math.round(a[0] + (b[0] - a[0]) * t); const y = Math.round(a[1] + (b[1] - a[1]) * t); for (let ox = -widthPx; ox <= widthPx; ox++) for (let oy = -widthPx; oy <= widthPx; oy++) set(x + ox, y + oy, color); } };
  const purple = [104, 18, 132, 255]; const purpleDark = [71, 8, 91, 255]; const outline = [61, 9, 73, 255];
  poly([[125, 235], [610, 235], [680, 400], [80, 400]], purple, 0.63);
  ellipse(365, 235, 270, 122, purple, 0.55);
  poly([[125, 235], [610, 235], [610, 275], [125, 275]], purpleDark, 0.28);
  line([80, 400], [680, 400], outline, 3);
  line([112, 315], [646, 315], [116, 26, 137, 180], 2);
  line([105, 258], [630, 258], [116, 26, 137, 160], 2);
  // Entrance cutout and tunnel.
  const arch = [[345, 410], [345, 315], [365, 285], [397, 270], [430, 285], [450, 315], [450, 410]];
  poly(arch, [245, 246, 244], 1);
  line([345, 410], [345, 315], outline, 4); line([345, 315], [365, 285], outline, 4); line([365, 285], [397, 270], outline, 4); line([397, 270], [430, 285], outline, 4); line([430, 285], [450, 315], outline, 4); line([450, 315], [450, 410], outline, 4);
  poly([[337, 411], [337, 312], [360, 272], [397, 250], [435, 272], [458, 312], [458, 411], [445, 409], [445, 316], [425, 288], [397, 276], [370, 288], [350, 316], [350, 411]], purple, 0.62);
  line([337, 411], [337, 312], outline, 3); line([337, 312], [360, 272], outline, 3); line([360, 272], [397, 250], outline, 3); line([397, 250], [435, 272], outline, 3); line([435, 272], [458, 312], outline, 3); line([458, 312], [458, 411], outline, 3);
  // Top posts and vent holes.
  const posts = [[205,180],[275,146],[365,135],[455,146],[530,180],[250,205],[475,205]];
  for (const [x, y] of posts) { poly([[x - 22, y], [x + 22, y], [x + 20, y + 40], [x - 20, y + 40]], purple, 0.75); line([x - 22, y], [x + 22, y], outline, 2); }
  for (const [x, y] of [[330,190],[365,180],[400,190],[365,220]]) ellipse(x, y, 6, 3, purpleDark, 0.9);
  return makePng(width, height, (x, y) => { const i = (y * width + x) * 4; return [pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]]; });
}

const binaryChunks = []; const bufferViews = []; const accessors = [];
function appendAligned(bytes) { const offset = binaryChunks.reduce((sum, item) => sum + item.length, 0); const padded = Buffer.concat([bytes, Buffer.alloc((4 - (bytes.length % 4)) % 4)]); binaryChunks.push(padded); return { offset, length: bytes.length }; }
function minMax(values, stride) { const min = Array(stride).fill(Infinity); const max = Array(stride).fill(-Infinity); for (let i = 0; i < values.length; i += stride) for (let j = 0; j < stride; j++) { min[j] = Math.min(min[j], values[i + j]); max[j] = Math.max(max[j], values[i + j]); } return { min, max }; }
function addAccessor(values, componentType, type, target) { const typed = componentType === 5126 ? new Float32Array(values) : new Uint32Array(values); const bytes = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength); const loc = appendAligned(bytes); const viewIndex = bufferViews.push({ buffer: 0, byteOffset: loc.offset, byteLength: loc.length, target }) - 1; const count = values.length / ({ SCALAR: 1, VEC2: 2, VEC3: 3 }[type]); const accessor = { bufferView: viewIndex, componentType, count, type, ...minMax(values, ({ SCALAR: 1, VEC2: 2, VEC3: 3 }[type]) ) }; return accessors.push(accessor) - 1; }

for (const mesh of meshes) { mesh.positionAccessor = addAccessor(mesh.positions, 5126, 'VEC3', 34962); mesh.normalAccessor = addAccessor(mesh.normals, 5126, 'VEC3', 34962); mesh.uvAccessor = addAccessor(mesh.uvs, 5126, 'VEC2', 34962); mesh.indexAccessor = addAccessor(mesh.indices, 5125, 'SCALAR', 34963); }

const gltf = {
  asset: { version: '2.0', generator: 'Codex rat igloo hide generator' }, scene: 0,
  scenes: [{ name: 'Rat Igloo Hide', nodes: nodes.map((_, i) => i) }], nodes,
  meshes: meshes.map((mesh) => ({ name: mesh.name, primitives: [{ attributes: { POSITION: mesh.positionAccessor, NORMAL: mesh.normalAccessor, TEXCOORD_0: mesh.uvAccessor }, indices: mesh.indexAccessor, material: mesh.material }] })),
  materials: [
    { name: 'Translucent Purple Plastic', alphaMode: 'BLEND', doubleSided: true, pbrMetallicRoughness: { baseColorFactor: [0.34, 0.035, 0.52, 0.58], metallicFactor: 0, roughnessFactor: 0.22 } },
    { name: 'Darker Purple Plastic', alphaMode: 'BLEND', doubleSided: true, pbrMetallicRoughness: { baseColorFactor: [0.18, 0.012, 0.27, 0.62], metallicFactor: 0, roughnessFactor: 0.30 } },
    { name: 'Vent Interior', alphaMode: 'BLEND', doubleSided: true, pbrMetallicRoughness: { baseColorFactor: [0.055, 0.004, 0.08, 0.72], metallicFactor: 0, roughnessFactor: 0.42 } },
  ],
  bufferViews, accessors, buffers: [{ byteLength: binaryChunks.reduce((sum, item) => sum + item.length, 0) }],
  extras: { dimensions: { width: 4.16, depth: 4.16, height: 2.22 }, material: 'translucent purple plastic', entrance: { width: 1.88, height: 1.48, tunnelDepth: 0.91 }, topVentPosts: 8, ventHoles: 6 },
};
const json = Buffer.from(JSON.stringify(gltf)); const jsonPadded = Buffer.concat([json, Buffer.alloc((4 - (json.length % 4)) % 4, 0x20)]); const bin = Buffer.concat(binaryChunks); const binPadded = Buffer.concat([bin, Buffer.alloc((4 - (bin.length % 4)) % 4)]);
const header = Buffer.alloc(12); header.writeUInt32LE(0x46546c67, 0); header.writeUInt32LE(2, 4); header.writeUInt32LE(12 + 8 + jsonPadded.length + 8 + binPadded.length, 8);
const jsonHeader = Buffer.alloc(8); jsonHeader.writeUInt32LE(jsonPadded.length, 0); jsonHeader.writeUInt32LE(0x4e4f534a, 4); const binHeader = Buffer.alloc(8); binHeader.writeUInt32LE(binPadded.length, 0); binHeader.writeUInt32LE(0x004e4942, 4);
const glbPath = path.join(outDir, 'rat_igloo_hide.glb'); fs.writeFileSync(glbPath, Buffer.concat([header, jsonHeader, jsonPadded, binHeader, binPadded]));

const objLines = ['# Rat igloo hide', 'mtllib rat_igloo_hide.mtl']; let vOff = 0; let tOff = 0; let nOff = 0;
for (const mesh of meshes) { objLines.push(`o ${mesh.name.replaceAll(' ', '_')}`, `usemtl ${mesh.material === 0 ? 'TranslucentPurplePlastic' : mesh.material === 1 ? 'DarkerPurplePlastic' : 'VentInterior'}`); for (let i = 0; i < mesh.positions.length; i += 3) objLines.push(`v ${mesh.positions[i]} ${mesh.positions[i + 1]} ${mesh.positions[i + 2]}`); for (let i = 0; i < mesh.uvs.length; i += 2) objLines.push(`vt ${mesh.uvs[i]} ${mesh.uvs[i + 1]}`); for (let i = 0; i < mesh.normals.length; i += 3) objLines.push(`vn ${mesh.normals[i]} ${mesh.normals[i + 1]} ${mesh.normals[i + 2]}`); for (let i = 0; i < mesh.indices.length; i += 3) objLines.push(`f ${[0, 1, 2].map((j) => { const k = mesh.indices[i + j]; return `${k + 1 + vOff}/${k + 1 + tOff}/${k + 1 + nOff}`; }).join(' ')}`); vOff += mesh.positions.length / 3; tOff += mesh.uvs.length / 2; nOff += mesh.normals.length / 3; }
fs.writeFileSync(path.join(outDir, 'rat_igloo_hide.obj'), `${objLines.join('\n')}\n`);
fs.writeFileSync(path.join(outDir, 'rat_igloo_hide.mtl'), ['newmtl TranslucentPurplePlastic', 'Ka 0.34 0.035 0.52', 'Kd 0.34 0.035 0.52', 'Ks 0.45 0.45 0.45', 'Ns 40', 'd 0.58', 'Tr 0.42', 'illum 4', '', 'newmtl DarkerPurplePlastic', 'Ka 0.18 0.012 0.27', 'Kd 0.18 0.012 0.27', 'Ks 0.30 0.30 0.30', 'Ns 32', 'd 0.62', 'Tr 0.38', 'illum 4', '', 'newmtl VentInterior', 'Ka 0.055 0.004 0.08', 'Kd 0.055 0.004 0.08', 'Ks 0.05 0.05 0.05', 'Ns 8', 'd 0.72', 'Tr 0.28', 'illum 4', ''].join('\n'));
fs.writeFileSync(path.join(outDir, 'rat_igloo_hide_preview.png'), makePreview());
const manifest = { model: 'rat_igloo_hide.glb', alternate: 'rat_igloo_hide.obj', shape: 'translucent purple plastic rat hide / igloo', features: ['arched entrance tunnel', 'rounded shell', 'molded seam rings', 'raised top vent posts', 'top vent holes'], material: 'GLB alphaMode BLEND; OBJ MTL includes transparency values', dimensions: gltf.extras.dimensions };
fs.writeFileSync(path.join(outDir, 'asset_manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
console.log(JSON.stringify({ outDir, glbPath, preview: path.join(outDir, 'rat_igloo_hide_preview.png'), meshes: meshes.length }, null, 2));
