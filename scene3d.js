// Motor 3D da simulação (Fase 1): three.js desenha a cena que o Core descreve (SimulationScene).
// O C# manda malhas prontas (posições, normais, cores, índices), linhas e a câmera; aqui só se desenha
// e se trata o gesto de girar, aproximar e deslocar. Carregado só quando a vista 3D é aberta.
import * as THREE from './lib/three/three.module.min.js';

const views = new Map();   // id da vista → estado
let nextId = 1;

// O fundo é um degradê (CSS no div); as cores vêm do Core (SceneBackground), um só lugar para o desktop e a web.

/** Cria uma vista 3D dentro de um div novo (o Avalonia posiciona o div). Devolve o div. */
export function create() {
  const id = nextId++;
  const host = document.createElement('div');
  host.style.cssText = 'width:100%;height:100%;position:relative;overflow:hidden;touch-action:none;background:linear-gradient(to bottom,#273142,#0a0e14)';
  host.dataset.scene3d = String(id);

  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
  renderer.setClearColor(0x000000, 0); // transparente: aparece o degradê do div
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.domElement.style.cssText = 'display:block;width:100%;height:100%';
  host.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.add(new THREE.HemisphereLight(0xffffff, 0x6a7482, 1.05));
  const sun = new THREE.DirectionalLight(0xffffff, 0.95);
  scene.add(sun);
  scene.add(sun.target);

  const camera = new THREE.PerspectiveCamera(35, 1, 0.1, 10000);
  camera.up.set(0, 0, 1);

  // Grupo que gira junto com a mesa (material, fixações e percurso); a ferramenta fica fora dele
  const part = new THREE.Group();
  part.matrixAutoUpdate = false;
  scene.add(part);

  const v = {
    id, host, renderer, scene, camera, sun, part,
    objects: new Map(), lines: new Map(),
    orbit: { tx: 0, ty: 0, tz: 0, distance: 200, az: 35, el: 30 },
    materialOpacity: 1,
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
  if (v.bar) layoutBar(v);
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
function materialFor(role, opacity) {
  const matte = isMatter(role);
  const tool = role === 'Tool';
  const material = new THREE.MeshStandardMaterial({
    vertexColors: true,
    metalness: matte ? 0.0 : tool ? 0.45 : 0.1,
    roughness: matte ? 0.88 : tool ? 0.38 : 0.7,
    polygonOffset: true, polygonOffsetFactor: 1, polygonOffsetUnits: 1,
  });
  if (matte) applyOpacity(material, opacity);
  return material;
}

const isMatter = role => role === 'Material' || role === 'Section';

// Material transparente: a peça fica translúcida (sem escrever a profundidade) e a ferramenta aparece por dentro
function applyOpacity(material, opacity) {
  material.transparent = opacity < 1;
  material.opacity = opacity;
  material.depthWrite = opacity >= 1;
  material.needsUpdate = true;
}

/** Opacidade do material e da face de corte (1 = sólido). Vem do Core (SceneSnapshot.MaterialOpacity). */
export function setMaterialOpacity(host, opacity) {
  const v = viewOf(host);
  if (!v) return;
  v.materialOpacity = opacity;
  for (const entry of v.objects.values()) {
    if (isMatter(entry.role)) {
      applyOpacity(entry.mesh.material, opacity);
      entry.mesh.renderOrder = opacity < 1 ? 5 : 0; // translúcido depois dos opacos
    }
  }
  requestRender(v);
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
    const mesh = new THREE.Mesh(geometry, materialFor(role, v.materialOpacity));
    if (isMatter(role) && v.materialOpacity < 1) mesh.renderOrder = 5;
    parentFor(v, role).add(mesh);
    entry = { mesh, role, edges: null };
    v.objects.set(id, entry);
  }
  requestRender(v);
}

const PART_ROLES = new Set(['Material', 'Section', 'Fixture', 'FixtureJaw', 'FeedPath', 'RapidPath', 'RapidInMaterial', 'CurrentPath']);
const parentFor = (v, role) => (PART_ROLES.has(role) ? v.part : v.scene);

/** Posição da mesa: matriz 4x4 (16 floats, colunas) que gira a peça e o percurso; a ferramenta não gira. */
export function setPartMatrix(host, bytes) {
  const v = viewOf(host);
  if (!v) return;
  v.part.matrix.fromArray(toF32(bytes));
  v.part.matrixWorldNeedsUpdate = true;
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
  entry.mesh.parent?.remove(entry.mesh);
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
    parentFor(v, role).add(line);
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

/** Fundo em degradê vertical: cor de cima e de baixo (0xRRGGBB), calculadas pelo Core. */
export function setBackground(host, top, bottom) {
  const v = viewOf(host);
  if (!v) return;
  const css = c => '#' + (c >>> 0).toString(16).padStart(6, '0');
  v.host.style.background = `linear-gradient(to bottom, ${css(top)}, ${css(bottom)})`;
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

// ── Barra da simulação flutuante ──
// HTML dentro do div do 3D (o 3D fica por cima de tudo do app, então a barra tem de morar nele).
// Fica numa das quatro bordas (baixo, direita, cima, esquerda): arraste a alça ou toque nela para trocar.
// Os comandos voltam ao C# por um callback (início, voltar, play, passo, fim, ir a um passo, velocidade).
const BAR_EDGES = ['bottom', 'right', 'top', 'left'];
const BAR_KEY = 'cc.simbar.edge';
const BAR_ICONS = {
  start: 'M0,0 H2 V12 H0 Z M7,0 L2.5,6 L7,12 Z M12,0 L7.5,6 L12,12 Z',
  back: 'M0,0 H2.5 V12 H0 Z M11,0 L3.5,6 L11,12 Z',
  play: 'M1,0 L12,6 L1,12 Z',
  pause: 'M1,0 H4.5 V12 H1 Z M7.5,0 H11 V12 H7.5 Z',
  step: 'M0,0 L7.5,6 L0,12 Z M8.5,0 H11 V12 H8.5 Z',
  end: 'M0,0 L4.5,6 L0,12 Z M5,0 L9.5,6 L5,12 Z M10,0 H12 V12 H10 Z',
};

function barEdge() {
  try { const e = localStorage.getItem(BAR_KEY); if (BAR_EDGES.includes(e)) return e; } catch { /* sem armazenamento */ }
  return 'bottom';
}

function ensureSimBar(v) {
  if (v.bar) return v.bar;
  const b = { edge: barEdge(), step: 0, max: 1, playing: false, handler: null };
  const el = document.createElement('div');
  el.style.cssText = 'position:absolute;z-index:5;display:none;box-sizing:border-box;align-items:center;gap:6px;padding:6px;border-radius:14px;'
    + 'background:rgba(13,17,23,.86);border:1px solid var(--cc-accent,#f0883e);box-shadow:0 2px 12px rgba(0,0,0,.45);color:#e6edf3;'
    + 'font:600 13px system-ui,sans-serif;touch-action:none;user-select:none;-webkit-user-select:none;-webkit-tap-highlight-color:transparent';
  // O div do 3D fica dentro do Avalonia: os eventos da barra não podem subir
  for (const t of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'click', 'wheel', 'dblclick', 'contextmenu'])
    el.addEventListener(t, e => e.stopPropagation());

  const send = (cmd, value = 0) => { if (b.handler) b.handler(cmd, value); };
  const mk = (tag, css) => { const n = document.createElement(tag); n.style.cssText = css; return n; };
  const svg = d => `<svg viewBox="0 0 12 12" width="16" height="16" fill="currentColor" aria-hidden="true"><path d="${d}"/></svg>`;
  const button = (icon, label, cmd, size = 38) => {
    const n = mk('button', `width:${size}px;height:${size}px;display:flex;align-items:center;justify-content:center;padding:0;border-radius:10px;`
      + 'border:1px solid #303d4b;background:#161b22;color:#e6edf3;cursor:pointer;flex:none;font:inherit');
    n.type = 'button'; n.title = label; n.setAttribute('aria-label', label);
    n.addEventListener('click', () => send(cmd));
    return n;
  };

  // Alça: arrastar move a barra para a borda mais perto; um toque troca de borda
  const grip = mk('div', 'width:26px;height:38px;display:flex;align-items:center;justify-content:center;color:#9baab9;cursor:grab;flex:none;touch-action:none;font-size:16px;line-height:1');
  grip.textContent = '⠿'; grip.title = 'Mover a barra: arraste ou toque';
  grip.setAttribute('aria-label', 'Mover a barra de simulação');
  let drag = null;
  grip.addEventListener('pointerdown', e => {
    grip.setPointerCapture(e.pointerId);
    const r = el.getBoundingClientRect();
    drag = { x: e.clientX, y: e.clientY, ox: e.clientX - r.left, oy: e.clientY - r.top, moved: false };
  });
  grip.addEventListener('pointermove', e => {
    if (!drag) return;
    if (!drag.moved && Math.hypot(e.clientX - drag.x, e.clientY - drag.y) < 8) return;
    drag.moved = true;
    const h = v.host.getBoundingClientRect();
    el.style.transform = 'none'; el.style.right = 'auto'; el.style.bottom = 'auto';
    el.style.left = Math.max(0, Math.min(h.width - el.offsetWidth, e.clientX - h.left - drag.ox)) + 'px';
    el.style.top = Math.max(0, Math.min(h.height - el.offsetHeight, e.clientY - h.top - drag.oy)) + 'px';
  });
  const release = e => {
    if (!drag) return;
    const d = drag; drag = null;
    if (!d.moved) { setBarEdge(v, BAR_EDGES[(BAR_EDGES.indexOf(b.edge) + 1) % BAR_EDGES.length]); return; }
    const h = v.host.getBoundingClientRect();
    const x = e.clientX - h.left, y = e.clientY - h.top;
    const dist = { left: x, right: h.width - x, top: y, bottom: h.height - y };
    setBarEdge(v, BAR_EDGES.reduce((best, k) => dist[k] < dist[best] ? k : best, 'bottom'));
  };
  grip.addEventListener('pointerup', release);
  grip.addEventListener('pointercancel', () => { drag = null; layoutBar(v); });

  const buttons = mk('div', 'display:flex;gap:4px;flex:none');
  b.start = button(BAR_ICONS.start, 'Voltar ao início', 'start');
  b.back = button(BAR_ICONS.back, 'Passo anterior', 'back');
  b.play = button(BAR_ICONS.play, 'Executar / pausar', 'play', 44);
  b.play.style.borderColor = 'var(--cc-accent,#f0883e)'; b.play.style.color = 'var(--cc-accent,#f0883e)';
  b.next = button(BAR_ICONS.step, 'Próximo passo', 'step');
  b.end = button(BAR_ICONS.end, 'Ir ao fim', 'end');
  b.all = [b.start, b.back, b.play, b.next, b.end];
  for (const n of b.all) buttons.append(n);
  const setIcon = (n, d) => { n.innerHTML = svg(d); };
  b.setIcon = setIcon;
  setIcon(b.start, BAR_ICONS.start); setIcon(b.back, BAR_ICONS.back); setIcon(b.play, BAR_ICONS.play);
  setIcon(b.next, BAR_ICONS.step); setIcon(b.end, BAR_ICONS.end);

  // Barra de progresso própria (o controle do navegador não gira bem para a vertical)
  const slider = mk('div', 'position:relative;flex:1 1 170px;min-width:0;touch-action:none;cursor:pointer');
  const track = mk('div', 'position:absolute;border-radius:3px;background:#303d4b');
  const fill = mk('div', 'position:absolute;border-radius:3px;background:var(--cc-accent,#f0883e)');
  const thumb = mk('div', 'position:absolute;width:20px;height:20px;border-radius:50%;background:var(--cc-accent,#f0883e);border:2px solid #0d1117;box-sizing:border-box');
  slider.append(track, fill, thumb);
  b.slider = { slider, track, fill, thumb };
  let lastSeek = -1;
  const seek = e => {
    const r = slider.getBoundingClientRect();
    const vertical = b.edge === 'left' || b.edge === 'right';
    const f = vertical ? (e.clientY - r.top) / Math.max(1, r.height) : (e.clientX - r.left) / Math.max(1, r.width);
    const step = Math.round(Math.max(0, Math.min(1, f)) * b.max);
    if (step !== lastSeek) { lastSeek = step; send('seek', step); }
  };
  let seeking = false;
  slider.addEventListener('pointerdown', e => { seeking = true; lastSeek = -1; slider.setPointerCapture(e.pointerId); seek(e); });
  slider.addEventListener('pointermove', e => { if (seeking) seek(e); });
  const endSeek = () => { seeking = false; };
  slider.addEventListener('pointerup', endSeek);
  slider.addEventListener('pointercancel', endSeek);

  const speed = mk('div', 'display:flex;align-items:center;gap:4px;flex:none');
  b.slower = button('', 'Mais devagar', 'slower', 34); b.slower.textContent = '−';
  b.faster = button('', 'Mais rápido', 'faster', 34); b.faster.textContent = '+';
  b.speedText = mk('span', 'min-width:38px;text-align:center;font-size:12px;color:#9baab9;cursor:pointer');
  b.speedText.title = 'Velocidade: toque para trocar';
  b.speedText.addEventListener('click', () => { if (b.compact) send('cycle'); });
  speed.append(b.slower, b.speedText, b.faster);

  b.el = el; b.grip = grip; b.buttons = buttons; b.speed = speed;
  el.append(grip, buttons, speed, slider);   // ordem: na linha única o progresso fica por último e estica
  v.host.appendChild(el);
  v.bar = b;
  layoutBar(v);
  return b;
}

function setBarEdge(v, edge) {
  v.bar.edge = edge;
  try { localStorage.setItem(BAR_KEY, edge); } catch { /* sem armazenamento */ }
  layoutBar(v);
}

function layoutBar(v) {
  const b = v.bar;
  if (!b) return;
  const el = b.el, vertical = b.edge === 'left' || b.edge === 'right';
  // Celular deitado: a barra vertical não cabe com os botões grandes; fica mais compacta
  b.compact = vertical && v.host.clientHeight < 520;
  const size = b.compact ? 30 : 38;
  for (const n of b.all) { const big = n === b.play ? size + 6 : size; n.style.width = big + 'px'; n.style.height = big + 'px'; }
  b.grip.style.height = (b.compact ? 22 : 38) + 'px';
  b.slower.style.display = b.faster.style.display = b.compact ? 'none' : 'flex';
  b.speedText.style.padding = b.compact ? '4px 0' : '0';
  el.style.left = el.style.right = el.style.top = el.style.bottom = 'auto';
  el.style.transform = 'none';
  el.style.display = b.visible ? 'flex' : 'none';
  const s = b.slider;
  if (vertical) {
    el.style.flexDirection = 'column'; el.style.flexWrap = 'nowrap';
    el.style.top = '50%'; el.style.transform = 'translateY(-50%)';
    el.style[b.edge] = '8px';
    el.style.height = b.compact ? 'calc(100% - 16px)' : 'min(470px, calc(100% - 16px))'; el.style.width = 'auto';
    el.style.padding = b.compact ? '4px' : '6px'; el.style.gap = b.compact ? '4px' : '6px';
    b.buttons.style.flexDirection = 'column';
    b.speed.style.flexDirection = 'column';
    s.slider.style.flex = '1 1 60px'; s.slider.style.minHeight = b.compact ? '56px' : '90px'; s.slider.style.width = '30px'; s.slider.style.height = 'auto';
    Object.assign(s.track.style, { left: '13px', width: '4px', top: '0', bottom: '0', height: 'auto' });
  } else {
    el.style.flexDirection = 'row'; el.style.flexWrap = 'wrap'; el.style.justifyContent = 'center';
    el.style.left = '50%'; el.style.transform = 'translateX(-50%)';
    el.style[b.edge] = '8px';
    el.style.width = 'min(640px, calc(100% - 16px))'; el.style.height = 'auto';
    el.style.padding = '6px'; el.style.gap = '6px';
    b.buttons.style.flexDirection = 'row';
    b.speed.style.flexDirection = 'row';
    s.slider.style.flex = '1 1 170px'; s.slider.style.minHeight = '30px'; s.slider.style.width = 'auto'; s.slider.style.height = '30px';
    Object.assign(s.track.style, { top: '13px', height: '4px', left: '0', right: '0', width: 'auto', bottom: 'auto' });
  }
  paintBar(v);
}

function paintBar(v) {
  const b = v.bar;
  if (!b) return;
  const f = b.max > 0 ? Math.max(0, Math.min(1, b.step / b.max)) : 0;
  const vertical = b.edge === 'left' || b.edge === 'right';
  const { fill, thumb } = b.slider;
  if (vertical) {
    Object.assign(fill.style, { left: '13px', width: '4px', top: '0', height: (f * 100) + '%', right: 'auto', bottom: 'auto' });
    Object.assign(thumb.style, { left: '5px', top: `calc(${f * 100}% - 10px)` });
  } else {
    Object.assign(fill.style, { top: '13px', height: '4px', left: '0', width: (f * 100) + '%', right: 'auto', bottom: 'auto' });
    Object.assign(thumb.style, { top: '5px', left: `calc(${f * 100}% - 10px)` });
  }
  b.setIcon(b.play, b.playing ? BAR_ICONS.pause : BAR_ICONS.play);
}

/** Recebe os comandos da barra (início, voltar, play, passo, fim, seek, slower, faster). */
export function onSimBar(host, handler) {
  const v = viewOf(host);
  if (v) ensureSimBar(v).handler = handler;
}

/** Atualiza o estado mostrado: reproduzindo, passo atual, total de passos e texto da velocidade. */
export function setSimBar(host, playing, step, max, speedText) {
  const v = viewOf(host);
  if (!v) return;
  const b = ensureSimBar(v);
  b.playing = playing; b.step = step; b.max = Math.max(1, max);
  b.speedText.textContent = speedText;
  paintBar(v);
}

/** Mostra ou esconde a barra (some quando o 2D está ao lado e a barra do rodapé vale para os dois). */
export function setSimBarVisible(host, visible) {
  const v = viewOf(host);
  if (!v) return;
  const b = ensureSimBar(v);
  b.visible = visible;
  layoutBar(v);
}
