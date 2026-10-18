// Motor 3D da simulação (Fase 1): three.js desenha a cena que o Core descreve (SimulationScene).
// O C# manda malhas prontas (posições, normais, cores, índices), linhas e a câmera; aqui só se desenha
// e se trata o gesto de girar, aproximar e deslocar. Carregado só quando a vista 3D é aberta.
import * as THREE from './lib/three/three.module.min.js';

const views = new Map();   // id da vista → estado
let nextId = 1;

// Cores de fundo por tema
const BACKGROUND = { dark: 0x0d1117, light: 0xf3f5f8 };

/** Cria uma vista 3D dentro de um div novo (o Avalonia posiciona o div). Devolve o div. */
export function create() {
  const id = nextId++;
  const host = document.createElement('div');
  host.style.cssText = 'width:100%;height:100%;position:relative;overflow:hidden;touch-action:none;background:#0d1117';
  host.dataset.scene3d = String(id);

  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.domElement.style.cssText = 'display:block;width:100%;height:100%';
  host.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(BACKGROUND.dark);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x6a7482, 1.05));
  const sun = new THREE.DirectionalLight(0xffffff, 0.95);
  scene.add(sun);
  scene.add(sun.target);

  const camera = new THREE.PerspectiveCamera(35, 1, 0.1, 10000);
  camera.up.set(0, 0, 1);

  const v = {
    id, host, renderer, scene, camera, sun,
    objects: new Map(), lines: new Map(),
    orbit: { tx: 0, ty: 0, tz: 0, distance: 200, az: 35, el: 30 },
    dirty: true, raf: 0, frames: 0, fps: 0, fpsClock: performance.now(),
  };
  views.set(id, v);

  // Tamanho: acompanha o div (o Avalonia muda ao redimensionar a janela ou girar o celular)
  v.resize = new ResizeObserver(() => resize(v));
  v.resize.observe(host);
  attachGestures(v);
  requestRender(v);
  return host;
}

function viewOf(host) {
  return views.get(Number(host?.dataset?.scene3d));
}

function resize(v) {
  const w = Math.max(1, v.host.clientWidth), h = Math.max(1, v.host.clientHeight);
  v.renderer.setSize(w, h, false);
  v.camera.aspect = w / h;
  v.camera.updateProjectionMatrix();
  requestRender(v);
}

function requestRender(v) {
  v.dirty = true;
  if (!v.raf) v.raf = requestAnimationFrame(() => draw(v));
}

// Desenha só quando algo mudou (economiza bateria no celular)
function draw(v) {
  v.raf = 0;
  if (!v.dirty || !views.has(v.id)) return;
  v.dirty = false;
  applyOrbit(v);
  v.renderer.render(v.scene, v.camera);
  v.frames++;
  const now = performance.now();
  if (now - v.fpsClock >= 1000) { v.fps = v.frames * 1000 / (now - v.fpsClock); v.frames = 0; v.fpsClock = now; }
}

// Mesma convenção do Core (SceneCamera): Z para cima, azimute a partir de −Y girando para +X
function applyOrbit(v) {
  const o = v.orbit;
  const az = o.az * Math.PI / 180, el = Math.max(-89.9, Math.min(89.9, o.el)) * Math.PI / 180;
  const dx = Math.sin(az) * Math.cos(el), dy = -Math.cos(az) * Math.cos(el), dz = Math.sin(el);
  v.camera.position.set(o.tx + dx * o.distance, o.ty + dy * o.distance, o.tz + dz * o.distance);
  v.camera.near = o.distance / 100;
  v.camera.far = o.distance * 20;
  v.camera.updateProjectionMatrix();
  v.camera.lookAt(o.tx, o.ty, o.tz);
  // Luz principal vem de cima, por trás do ombro de quem olha
  v.sun.position.set(o.tx + (dx * 0.6 - dy * 0.5) * o.distance, o.ty + (dy * 0.6 + dx * 0.5) * o.distance, o.tz + o.distance);
  v.sun.target.position.set(o.tx, o.ty, o.tz);
}

// ── Gestos: um dedo / botão esquerdo gira; dois dedos aproximam e deslocam; roda aproxima;
//    botão direito ou do meio desloca ──
function attachGestures(v) {
  const el = v.renderer.domElement;
  const pointers = new Map();
  let last = null;

  const pan = (dx, dy) => {
    const o = v.orbit;
    const scale = 2 * o.distance * Math.tan(v.camera.fov * Math.PI / 360) / Math.max(1, v.host.clientHeight);
    const az = o.az * Math.PI / 180, elv = o.el * Math.PI / 180;
    // Eixos da tela no mundo (direita e cima)
    const right = [Math.cos(az), Math.sin(az), 0];
    const up = [-Math.sin(az) * Math.sin(elv), Math.cos(az) * Math.sin(elv), Math.cos(elv)];
    o.tx += (-dx * right[0] + dy * up[0]) * scale;
    o.ty += (-dx * right[1] + dy * up[1]) * scale;
    o.tz += (-dx * right[2] + dy * up[2]) * scale;
  };

  // Os eventos param aqui: o Avalonia (que envolve este div) capturaria o ponteiro para ele
  el.addEventListener('pointerdown', e => {
    e.stopPropagation();
    el.setPointerCapture(e.pointerId);
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, button: e.button });
    last = snapshot();
  });
  el.addEventListener('pointermove', e => {
    e.stopPropagation();
    if (!pointers.has(e.pointerId)) return;
    pointers.set(e.pointerId, { ...pointers.get(e.pointerId), x: e.clientX, y: e.clientY });
    const now = snapshot();
    if (!last || now.count !== last.count) { last = now; return; }
    const o = v.orbit;
    if (now.count === 1) {
      const dx = now.x - last.x, dy = now.y - last.y;
      if (now.button === 0 && e.pointerType !== 'mouse' || e.buttons === 1) {
        o.az = ((o.az - dx * 0.4) % 360 + 360) % 360;
        o.el = Math.max(-89.9, Math.min(89.9, o.el + dy * 0.4));
      } else {
        pan(dx, dy);
      }
    } else if (now.count >= 2) {
      if (last.spread > 0 && now.spread > 0) o.distance = clampDistance(o.distance * last.spread / now.spread);
      pan(now.x - last.x, now.y - last.y);
    }
    last = now;
    requestRender(v);
  });
  const end = e => { e.stopPropagation(); pointers.delete(e.pointerId); last = snapshot(); };
  el.addEventListener('pointerup', end);
  el.addEventListener('pointercancel', end);
  el.addEventListener('contextmenu', e => e.preventDefault());
  el.addEventListener('wheel', e => {
    e.preventDefault();
    e.stopPropagation();
    v.orbit.distance = clampDistance(v.orbit.distance * Math.pow(1.0015, e.deltaY));
    requestRender(v);
  }, { passive: false });

  function snapshot() {
    const list = [...pointers.values()];
    if (list.length === 0) return null;
    const x = list.reduce((s, p) => s + p.x, 0) / list.length;
    const y = list.reduce((s, p) => s + p.y, 0) / list.length;
    const spread = list.length >= 2 ? Math.hypot(list[0].x - list[1].x, list[0].y - list[1].y) : 0;
    return { count: list.length, x, y, spread, button: list[0].button };
  }
}

function clampDistance(d) { return Math.max(0.01, Math.min(1e7, d)); }

// ── Conteúdo ──

const toF32 = v => { const b = v.slice(); return new Float32Array(b.buffer, b.byteOffset, b.byteLength / 4); };
const toU32 = v => { const b = v.slice(); return new Uint32Array(b.buffer, b.byteOffset, b.byteLength / 4); };

// Material por papel: peça (material) fosca e uniforme, ferramenta com um pouco de brilho metálico,
// fixações foscas. O aspecto limpo vem da luz suave e das arestas marcadas, não do brilho.
function materialFor(role) {
  const matte = role === 'Material' || role === 'Section';
  const tool = role === 'Tool';
  return new THREE.MeshStandardMaterial({
    vertexColors: true,
    metalness: matte ? 0.0 : tool ? 0.45 : 0.1,
    roughness: matte ? 0.88 : tool ? 0.38 : 0.7,
    polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1,
  });
}

/** Cria ou troca a malha "id". Bytes de Float32 (posições, normais, cores 0–1) e Uint32 (índices). */
export function setMesh(host, id, role, positions, normals, colors, indices) {
  const v = viewOf(host);
  if (!v) return;
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(toF32(positions), 3));
  geometry.setAttribute('normal', new THREE.BufferAttribute(toF32(normals), 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(toF32(colors), 3));
  geometry.setIndex(new THREE.BufferAttribute(toU32(indices), 1));
  geometry.computeBoundingSphere();

  let entry = v.objects.get(id);
  if (entry) {
    entry.mesh.geometry.dispose();
    entry.mesh.geometry = geometry;
  } else {
    const mesh = new THREE.Mesh(geometry, materialFor(role));
    v.scene.add(mesh);
    entry = { mesh, role, edges: null };
    v.objects.set(id, entry);
  }
  requestRender(v);
}

/** Arestas marcadas da malha "id" (o Core calcula: alto e pé das paredes do corte, quinas vivas das peças). */
export function setEdges(host, id, positions, color) {
  const v = viewOf(host);
  const entry = v?.objects.get(id);
  if (!entry) return;
  const array = toF32(positions);
  if (array.length === 0) {
    if (entry.edges) { entry.edges.geometry.dispose(); entry.edges.material.dispose(); entry.mesh.remove(entry.edges); entry.edges = null; }
    requestRender(v);
    return;
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(array, 3));
  if (entry.edges) {
    entry.edges.geometry.dispose();
    entry.edges.geometry = geometry;
  } else {
    // Traço fino e discreto por cima da superfície (a malha recua um pouco por polygonOffset)
    entry.edges = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color, transparent: true, opacity: 0.55 }));
    entry.mesh.add(entry.edges);
  }
  requestRender(v);
}

/** Desloca a malha "id" (a ferramenta anda) e mostra ou esconde. */
export function placeMesh(host, id, x, y, z, visible) {
  const v = viewOf(host);
  const entry = v?.objects.get(id);
  if (!entry) return;
  entry.mesh.position.set(x, y, z);
  entry.mesh.visible = visible;
  requestRender(v);
}

export function removeMesh(host, id) {
  const v = viewOf(host);
  const entry = v?.objects.get(id);
  if (!entry) return;
  v.scene.remove(entry.mesh);
  entry.mesh.geometry.dispose();
  entry.mesh.material.dispose();
  entry.edges?.geometry.dispose();
  entry.edges?.material.dispose();
  v.objects.delete(id);
  requestRender(v);
}

/** Linhas de um papel (trajetória, rápidos, rápidos no material): pares de pontos em Float32. */
export function setLines(host, role, positions, color, visible, onTop) {
  const v = viewOf(host);
  if (!v) return;
  let line = v.lines.get(role);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(toF32(positions), 3));
  if (line) {
    line.geometry.dispose();
    line.geometry = geometry;
    line.material.color.setHex(color);
  } else {
    line = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color, depthTest: !onTop, transparent: onTop }));
    line.renderOrder = onTop ? 10 : 0;
    v.scene.add(line);
    v.lines.set(role, line);
  }
  line.visible = visible;
  requestRender(v);
}

/** Câmera em órbita (mesma convenção do Core): alvo, distância, azimute, elevação e abertura. */
export function setCamera(host, tx, ty, tz, distance, az, el, fov) {
  const v = viewOf(host);
  if (!v) return;
  Object.assign(v.orbit, { tx, ty, tz, distance, az, el });
  v.camera.fov = fov;
  requestRender(v);
}

/** Câmera atual como "tx;ty;tz;distância;azimute;elevação" (para o C# guardar ao trocar de vista). */
export function getCamera(host) {
  const o = viewOf(host)?.orbit;
  return o ? [o.tx, o.ty, o.tz, o.distance, o.az, o.el].join(';') : '';
}

export function setTheme(host, light) {
  const v = viewOf(host);
  if (!v) return;
  const color = light ? BACKGROUND.light : BACKGROUND.dark;
  v.scene.background = new THREE.Color(color);
  v.host.style.background = '#' + color.toString(16).padStart(6, '0');
  requestRender(v);
}

/** Placa de vídeo em uso e quadros por segundo (diagnóstico). */
export function info(host) {
  const v = viewOf(host);
  if (!v) return '';
  const gl = v.renderer.getContext();
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  const gpu = ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
  return `${gpu} · ${v.renderer.info.render.triangles} triângulos`;
}

export function dispose(host) {
  const v = viewOf(host);
  if (!v) return;
  if (v.raf) cancelAnimationFrame(v.raf);
  v.resize.disconnect();
  for (const id of [...v.objects.keys()]) removeMesh(host, id);
  for (const line of v.lines.values()) { line.geometry.dispose(); line.material.dispose(); }
  v.renderer.dispose();
  views.delete(v.id);
}
