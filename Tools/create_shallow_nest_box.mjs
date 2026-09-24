import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const root = process.cwd();
const outDir = path.join(root, 'meshy_output', 'shallow_wooden_nest_box');
fs.mkdirSync(outDir, { recursive: true });

const TAU = Math.PI * 2;

function clamp(value, min = 0, max = 1) {
  return Math.max(min, Math.min(max, value));
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

function colorLerp(a, b, t) {
  return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t), 255];
}

function hash2(x, y, seed = 0) {
  let n = (x * 374761393 + y * 668265263 + seed * 1442695041) | 0;
  n = Math.imul(n ^ (n >>> 13), 1274126177);
  return ((n ^ (n >>> 16)) >>> 0) / 4294967295;
}

function smooth(t) {
  return t * t * (3 - 2 * t);
}

function valueNoise(x, y, seed) {
  const x0 = Math.floor(x);
  const y0 = Math.floor(y);
  const tx = smooth(x - x0);
  const ty = smooth(y - y0);
  const a = hash2(x0, y0, seed);
  const b = hash2(x0 + 1, y0, seed);
  const c = hash2(x0, y0 + 1, seed);
  const d = hash2(x0 + 1, y0 + 1, seed);
  return lerp(lerp(a, b, tx), lerp(c, d, tx), ty);
}

function seededRandom(seed) {
  let state = seed >>> 0;
  return () => {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    return state / 4294967296;
  };
}

function makePng(width, height, pixelFn) {
  const pixels = Buffer.alloc(width * height * 4);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const rgba = pixelFn(x, y, width, height);
      const i = (y * width + x) * 4;
      pixels[i] = clamp(Math.round(rgba[0]), 0, 255);
      pixels[i + 1] = clamp(Math.round(rgba[1]), 0, 255);
      pixels[i + 2] = clamp(Math.round(rgba[2]), 0, 255);
      pixels[i + 3] = clamp(Math.round(rgba[3] ?? 255), 0, 255);
    }
  }
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0;
    pixels.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  }
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const chunk = (type, data) => {
    const header = Buffer.alloc(8);
    header.writeUInt32BE(data.length, 0);
    header.write(type, 4, 4, 'ascii');
    const crc = crc32(Buffer.concat([Buffer.from(type, 'ascii'), data]));
    const footer = Buffer.alloc(4);
    footer.writeUInt32BE(crc >>> 0, 0);
    return Buffer.concat([header, data, footer]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([signature, chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function makeNestTexture(size = 512) {
  const pixels = [];
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const broadNoise = valueNoise(x * 0.035 + 4.7, y * 0.035 + 8.1, 17);
      const fineNoise = valueNoise(x * 0.12 + 12.3, y * 0.12 + 2.4, 31);
      const t = broadNoise * 0.78 + fineNoise * 0.22;
      pixels.push(colorLerp([201, 176, 133, 255], [250, 227, 184, 255], t));
    }
  }
  const random = seededRandom(731947);
  const drawFiber = (startX, startY, length, angle, color, opacity, width) => {
    const steps = Math.max(2, Math.ceil(length * 1.35));
    const endX = startX + Math.cos(angle) * length;
    const endY = startY + Math.sin(angle) * length;
    for (let step = 0; step <= steps; step++) {
      const t = step / steps;
      const wobble = Math.sin(t * Math.PI * 2.7 + startX * 0.07) * 0.9;
      const x = lerp(startX, endX, t) + Math.cos(angle + Math.PI * 0.5) * wobble;
      const y = lerp(startY, endY, t) + Math.sin(angle + Math.PI * 0.5) * wobble;
      const centerX = Math.round(x);
      const centerY = Math.round(y);
      for (let offset = -width; offset <= width; offset++) {
        const px = Math.abs(Math.cos(angle)) > 0.5 ? centerX : centerX + offset;
        const py = Math.abs(Math.cos(angle)) > 0.5 ? centerY + offset : centerY;
        if (px < 0 || px >= size || py < 0 || py >= size) continue;
        const i = py * size + px;
        pixels[i] = colorLerp(pixels[i], color, opacity);
      }
    }
  };
  for (let fiber = 0; fiber < 360; fiber++) {
    const startX = random() * (size - 1);
    const startY = random() * (size - 1);
    const length = lerp(3, 24, random()) * (size / 160);
    const angle = lerp(-Math.PI, Math.PI, random());
    const color = colorLerp([153, 115, 74, 255], [255, 242, 209, 255], random());
    const opacity = lerp(0.24, 0.72, random());
    const width = random() > 0.82 ? 2 : 1;
    drawFiber(startX, startY, length, angle, color, opacity, width);
  }
  return makePng(size, size, (x, y) => pixels[y * size + x]);
}

function makeWoodTexture(size = 512) {
  const random = seededRandom(139917);
  const knots = Array.from({ length: 7 }, () => ({
    x: random() * size,
    y: random() * size,
    radius: lerp(8, 30, random()),
  }));
  return makePng(size, size, (x, y) => {
    const broad = valueNoise(x * 0.015, y * 0.045, 83);
    const fine = valueNoise(x * 0.08, y * 0.14, 97);
    const grainWave = 0.5 + 0.5 * Math.sin(y * 0.14 + broad * 8 + fine * 3);
    let knot = 0;
    for (const item of knots) {
      const dx = (x - item.x) / item.radius;
      const dy = (y - item.y) / (item.radius * 0.35);
      knot = Math.max(knot, Math.exp(-(dx * dx + dy * dy)));
    }
    const shade = clamp(0.38 * broad + 0.24 * fine + 0.28 * grainWave + 0.5 * knot);
    return [lerp(176, 241, shade), lerp(119, 182, shade), lerp(67, 122, shade), 255];
  });
}

const nestTexture = makeNestTexture();
const woodTexture = makeWoodTexture();
fs.writeFileSync(path.join(outDir, 'nest_bedding_texture.png'), nestTexture);
fs.writeFileSync(path.join(outDir, 'light_wood_texture.png'), woodTexture);

const meshes = [];
const nodes = [];

function addBox(name, center, size, material, metadata = {}) {
  const [cx, cy, cz] = center;
  const [sx, sy, sz] = size;
  const hx = sx / 2;
  const hy = sy / 2;
  const hz = sz / 2;
  const faces = [
    { normal: [0, 1, 0], corners: [[-hx, hy, -hz], [hx, hy, -hz], [hx, hy, hz], [-hx, hy, hz]] },
    { normal: [0, -1, 0], corners: [[-hx, -hy, -hz], [-hx, -hy, hz], [hx, -hy, hz], [hx, -hy, -hz]] },
    { normal: [0, 0, 1], corners: [[-hx, -hy, hz], [hx, -hy, hz], [hx, hy, hz], [-hx, hy, hz]] },
    { normal: [0, 0, -1], corners: [[hx, -hy, -hz], [-hx, -hy, -hz], [-hx, hy, -hz], [hx, hy, -hz]] },
    { normal: [1, 0, 0], corners: [[hx, -hy, hz], [hx, -hy, -hz], [hx, hy, -hz], [hx, hy, hz]] },
    { normal: [-1, 0, 0], corners: [[-hx, -hy, -hz], [-hx, -hy, hz], [-hx, hy, hz], [-hx, hy, -hz]] },
  ];
  const positions = [];
  const normals = [];
  const uvs = [];
  const indices = [];
  for (const face of faces) {
    const base = positions.length / 3;
    for (let i = 0; i < 4; i++) {
      positions.push(face.corners[i][0] + cx, face.corners[i][1] + cy, face.corners[i][2] + cz);
      normals.push(...face.normal);
      const uv = [[0, 0], [1, 0], [1, 1], [0, 1]][i];
      uvs.push(...uv);
    }
    indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
  }
  const mesh = { name, positions, normals, uvs, indices, material };
  meshes.push(mesh);
  nodes.push({ name, mesh: meshes.length - 1, extras: metadata });
}

const outerWidth = 6.0;
const outerDepth = 4.2;
const outerHeight = 1.5;
const wall = 0.25;
const bottomThickness = 0.25;
const wallHeight = outerHeight - bottomThickness;

addBox('Box Bottom', [0, bottomThickness / 2, 0], [outerWidth, bottomThickness, outerDepth], 0, { role: 'wooden box base' });
addBox('Front Wall', [0, bottomThickness + wallHeight / 2, outerDepth / 2 - wall / 2], [outerWidth, wallHeight, wall], 0, { role: 'wooden box wall' });
addBox('Back Wall', [0, bottomThickness + wallHeight / 2, -outerDepth / 2 + wall / 2], [outerWidth, wallHeight, wall], 0, { role: 'wooden box wall' });
addBox('Left Wall', [-outerWidth / 2 + wall / 2, bottomThickness + wallHeight / 2, 0], [wall, wallHeight, outerDepth - wall * 2], 0, { role: 'wooden box wall' });
addBox('Right Wall', [outerWidth / 2 - wall / 2, bottomThickness + wallHeight / 2, 0], [wall, wallHeight, outerDepth - wall * 2], 0, { role: 'wooden box wall' });

const innerWidth = outerWidth - wall * 2;
const innerDepth = outerDepth - wall * 2;
const beddingTop = bottomThickness + wallHeight * (2 / 3);
const beddingThickness = 0.08;
addBox('Nest Bedding Layer', [0, beddingTop - beddingThickness / 2, 0], [innerWidth - 0.12, beddingThickness, innerDepth - 0.12], 1, {
  role: 'flat nest texture layer',
  heightFractionOfInterior: 0.6667,
  topY: beddingTop,
});

// Subtle raised top rails echo the slightly proud rim in the reference photo.
const rimHeight = 0.12;
addBox('Front Top Rim', [0, outerHeight - rimHeight / 2, outerDepth / 2 + 0.025], [outerWidth + 0.08, rimHeight, wall + 0.08], 0, { role: 'top rim' });
addBox('Back Top Rim', [0, outerHeight - rimHeight / 2, -outerDepth / 2 - 0.025], [outerWidth + 0.08, rimHeight, wall + 0.08], 0, { role: 'top rim' });
addBox('Left Top Rim', [-outerWidth / 2 - 0.025, outerHeight - rimHeight / 2, 0], [wall + 0.08, rimHeight, innerDepth + 0.08], 0, { role: 'top rim' });
addBox('Right Top Rim', [outerWidth / 2 + 0.025, outerHeight - rimHeight / 2, 0], [wall + 0.08, rimHeight, innerDepth + 0.08], 0, { role: 'top rim' });

function appendAligned(chunks, bytes) {
  const padded = Buffer.concat([bytes, Buffer.alloc((4 - (bytes.length % 4)) % 4)]);
  const offset = chunks.reduce((sum, item) => sum + item.length, 0);
  chunks.push(padded);
  return { offset, length: bytes.length };
}

function minMax(values, stride) {
  const min = Array(stride).fill(Infinity);
  const max = Array(stride).fill(-Infinity);
  for (let i = 0; i < values.length; i += stride) {
    for (let j = 0; j < stride; j++) {
      min[j] = Math.min(min[j], values[i + j]);
      max[j] = Math.max(max[j], values[i + j]);
    }
  }
  return { min, max };
}

const binaryChunks = [];
const bufferViews = [];
const accessors = [];
function addAccessor(values, componentType, type, target, normalized = false) {
  const array = componentType === 5126 ? new Float32Array(values) : new Uint32Array(values);
  const bytes = Buffer.from(array.buffer, array.byteOffset, array.byteLength);
  const location = appendAligned(binaryChunks, bytes);
  const componentCount = { SCALAR: 1, VEC2: 2, VEC3: 3 }[type];
  const view = { buffer: 0, byteOffset: location.offset, byteLength: location.length, target };
  const viewIndex = bufferViews.push(view) - 1;
  const accessor = { bufferView: viewIndex, componentType, count: values.length / componentCount, type };
  if (normalized) accessor.normalized = true;
  if (type !== 'SCALAR') Object.assign(accessor, minMax(values, componentCount));
  else {
    const mm = minMax(values, 1);
    accessor.min = mm.min;
    accessor.max = mm.max;
  }
  return accessors.push(accessor) - 1;
}

for (const mesh of meshes) {
  mesh.positionAccessor = addAccessor(mesh.positions, 5126, 'VEC3', 34962);
  mesh.normalAccessor = addAccessor(mesh.normals, 5126, 'VEC3', 34962);
  mesh.uvAccessor = addAccessor(mesh.uvs, 5126, 'VEC2', 34962);
  mesh.indexAccessor = addAccessor(mesh.indices, 5125, 'SCALAR', 34963);
}

const woodImageLocation = appendAligned(binaryChunks, woodTexture);
const nestImageLocation = appendAligned(binaryChunks, nestTexture);
const woodImageView = bufferViews.push({ buffer: 0, byteOffset: woodImageLocation.offset, byteLength: woodImageLocation.length }) - 1;
const nestImageView = bufferViews.push({ buffer: 0, byteOffset: nestImageLocation.offset, byteLength: nestImageLocation.length }) - 1;

const gltf = {
  asset: { version: '2.0', generator: 'Codex shallow wooden nest box generator' },
  scene: 0,
  scenes: [{ name: 'Shallow Wooden Nest Box', nodes: nodes.map((_, i) => i) }],
  nodes,
  meshes: meshes.map((mesh) => ({
    name: mesh.name,
    primitives: [{
      attributes: { POSITION: mesh.positionAccessor, NORMAL: mesh.normalAccessor, TEXCOORD_0: mesh.uvAccessor },
      indices: mesh.indexAccessor,
      material: mesh.material,
    }],
  })),
  materials: [
    { name: 'Light Maple Wood', pbrMetallicRoughness: { baseColorTexture: { index: 0 }, metallicFactor: 0, roughnessFactor: 0.78 } },
    { name: 'Nest Bedding Texture', pbrMetallicRoughness: { baseColorTexture: { index: 1 }, metallicFactor: 0, roughnessFactor: 0.98 } },
  ],
  textures: [{ sampler: 0, source: 0 }, { sampler: 0, source: 1 }],
  samplers: [{ magFilter: 9729, minFilter: 9987, wrapS: 10497, wrapT: 10497 }],
  images: [{ name: 'light_wood_texture.png', mimeType: 'image/png', bufferView: woodImageView }, { name: 'nest_bedding_texture.png', mimeType: 'image/png', bufferView: nestImageView }],
  bufferViews,
  accessors,
  buffers: [{ byteLength: binaryChunks.reduce((sum, item) => sum + item.length, 0) }],
  extras: {
    dimensions: { width: outerWidth, depth: outerDepth, height: outerHeight },
    nestLayer: { topY: beddingTop, interiorHeightFraction: 2 / 3, textureSource: 'Unity HabitatBuilder procedural pairing bedding recipe' },
  },
};

const json = Buffer.from(JSON.stringify(gltf));
const jsonPadded = Buffer.concat([json, Buffer.alloc((4 - (json.length % 4)) % 4, 0x20)]);
const bin = Buffer.concat(binaryChunks);
const binPadded = Buffer.concat([bin, Buffer.alloc((4 - (bin.length % 4)) % 4)]);
const glbHeader = Buffer.alloc(12);
glbHeader.writeUInt32LE(0x46546c67, 0);
glbHeader.writeUInt32LE(2, 4);
glbHeader.writeUInt32LE(12 + 8 + jsonPadded.length + 8 + binPadded.length, 8);
const jsonHeader = Buffer.alloc(8);
jsonHeader.writeUInt32LE(jsonPadded.length, 0);
jsonHeader.writeUInt32LE(0x4e4f534a, 4);
const binHeader = Buffer.alloc(8);
binHeader.writeUInt32LE(binPadded.length, 0);
binHeader.writeUInt32LE(0x004e4942, 4);
const glb = Buffer.concat([glbHeader, jsonHeader, jsonPadded, binHeader, binPadded]);
const glbPath = path.join(outDir, 'shallow_wooden_nest_box.glb');
fs.writeFileSync(glbPath, glb);

function writeObj() {
  const lines = ['# Shallow wooden nest box with embedded-style companion textures', 'mtllib shallow_wooden_nest_box.mtl'];
  let vertexOffset = 0;
  let uvOffset = 0;
  let normalOffset = 0;
  for (const mesh of meshes) {
    lines.push(`o ${mesh.name.replaceAll(' ', '_')}`, `usemtl ${mesh.material === 0 ? 'LightMapleWood' : 'NestBeddingTexture'}`);
    for (let i = 0; i < mesh.positions.length; i += 3) lines.push(`v ${mesh.positions[i]} ${mesh.positions[i + 1]} ${mesh.positions[i + 2]}`);
    for (let i = 0; i < mesh.uvs.length; i += 2) lines.push(`vt ${mesh.uvs[i]} ${mesh.uvs[i + 1]}`);
    for (let i = 0; i < mesh.normals.length; i += 3) lines.push(`vn ${mesh.normals[i]} ${mesh.normals[i + 1]} ${mesh.normals[i + 2]}`);
    for (let i = 0; i < mesh.indices.length; i += 3) {
      const face = [];
      for (let j = 0; j < 3; j++) {
        const v = mesh.indices[i + j] + 1 + vertexOffset;
        const t = mesh.indices[i + j] + 1 + uvOffset;
        const n = mesh.indices[i + j] + 1 + normalOffset;
        face.push(`${v}/${t}/${n}`);
      }
      lines.push(`f ${face.join(' ')}`);
    }
    vertexOffset += mesh.positions.length / 3;
    uvOffset += mesh.uvs.length / 2;
    normalOffset += mesh.normals.length / 3;
  }
  fs.writeFileSync(path.join(outDir, 'shallow_wooden_nest_box.obj'), `${lines.join('\n')}\n`);
  fs.writeFileSync(path.join(outDir, 'shallow_wooden_nest_box.mtl'), [
    'newmtl LightMapleWood', 'Ka 1 1 1', 'Kd 1 1 1', 'Ks 0.05 0.05 0.05', 'Ns 12', 'd 1', 'map_Kd light_wood_texture.png', '',
    'newmtl NestBeddingTexture', 'Ka 1 1 1', 'Kd 1 1 1', 'Ks 0 0 0', 'Ns 4', 'd 1', 'map_Kd nest_bedding_texture.png', '',
  ].join('\n'));
}
writeObj();

function makePreview() {
  const width = 800;
  const height = 600;
  const pixels = Buffer.alloc(width * height * 4, 0);
  const setPixel = (x, y, color) => {
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    const i = (y * width + x) * 4;
    pixels[i] = color[0]; pixels[i + 1] = color[1]; pixels[i + 2] = color[2]; pixels[i + 3] = color[3] ?? 255;
  };
  const fill = (color) => { for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) setPixel(x, y, color); };
  fill([239, 241, 238, 255]);
  const project = ([x, y, z]) => [Math.round(400 + x * 72 + z * 48), Math.round(500 - y * 110 + z * 22 - x * 8)];
  const poly = (points, color) => {
    const projected = points.map(project);
    const minY = Math.max(0, Math.floor(Math.min(...projected.map((p) => p[1]))));
    const maxY = Math.min(height - 1, Math.ceil(Math.max(...projected.map((p) => p[1]))));
    for (let y = minY; y <= maxY; y++) {
      const xs = [];
      for (let i = 0; i < projected.length; i++) {
        const a = projected[i];
        const b = projected[(i + 1) % projected.length];
        if ((a[1] <= y && b[1] > y) || (b[1] <= y && a[1] > y)) xs.push(a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]));
      }
      xs.sort((a, b) => a - b);
      for (let i = 0; i + 1 < xs.length; i += 2) for (let x = Math.ceil(xs[i]); x <= Math.floor(xs[i + 1]); x++) setPixel(x, y, color);
    }
    for (let i = 0; i < projected.length; i++) line(projected[i], projected[(i + 1) % projected.length], [105, 72, 43, 255], 2);
  };
  const line = (a, b, color, thickness = 1) => {
    const steps = Math.max(Math.abs(b[0] - a[0]), Math.abs(b[1] - a[1]), 1);
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const x = Math.round(lerp(a[0], b[0], t));
      const y = Math.round(lerp(a[1], b[1], t));
      for (let ox = -thickness; ox <= thickness; ox++) for (let oy = -thickness; oy <= thickness; oy++) setPixel(x + ox, y + oy, color);
    }
  };
  const wood = [206, 157, 99, 255];
  const woodDark = [161, 112, 66, 255];
  const bedding = [228, 193, 136, 255];
  poly([[-3, 0, 2.1], [3, 0, 2.1], [3, 1.5, 2.1], [-3, 1.5, 2.1]], wood);
  poly([[3, 0, 2.1], [3, 0, -2.1], [3, 1.5, -2.1], [3, 1.5, 2.1]], woodDark);
  poly([[-3, 0, -2.1], [3, 0, -2.1], [3, 1.5, -2.1], [-3, 1.5, -2.1]], [184, 132, 79, 255]);
  poly([[-2.875, 1.075, -1.875], [2.875, 1.075, -1.875], [2.875, 1.075, 1.875], [-2.875, 1.075, 1.875]], bedding);
  const fiberRandom = seededRandom(731947);
  for (let i = 0; i < 120; i++) {
    const x = (fiberRandom() * 2 - 1) * 2.7;
    const z = (fiberRandom() * 2 - 1) * 1.7;
    const angle = fiberRandom() * TAU;
    const len = lerp(0.05, 0.35, fiberRandom());
    line(project([x, 1.085, z]), project([x + Math.cos(angle) * len, 1.085, z + Math.sin(angle) * len]), [152, 112, 71, 190], 1);
  }
  poly([[-3.04, 1.5, 2.15], [3.04, 1.5, 2.15], [3.04, 1.62, 2.15], [-3.04, 1.62, 2.15]], [220, 177, 119, 255]);
  poly([[3.04, 1.5, 2.15], [3.04, 1.5, -2.15], [3.04, 1.62, -2.15], [3.04, 1.62, 2.15]], [184, 132, 79, 255]);
  return makePng(width, height, (x, y) => {
    const i = (y * width + x) * 4;
    return [pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]];
  });
}
fs.writeFileSync(path.join(outDir, 'shallow_wooden_nest_box_preview.png'), makePreview());

const manifest = {
  model: path.basename(glbPath),
  alternate: 'shallow_wooden_nest_box.obj',
  dimensions: { width: outerWidth, depth: outerDepth, height: outerHeight },
  nestLayer: { topY: beddingTop, topFractionOfInterior: 2 / 3 },
  textures: ['light_wood_texture.png', 'nest_bedding_texture.png'],
  textureProvenance: 'Reproduced from the deterministic procedural pairing-nest bedding recipe in Assets/Scripts/Presentation/HabitatBuilder.cs; the prior chat did not leave a standalone image file in the workspace.',
};
fs.writeFileSync(path.join(outDir, 'asset_manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
console.log(JSON.stringify({ outDir, glbPath, preview: path.join(outDir, 'shallow_wooden_nest_box_preview.png'), manifest }, null, 2));
