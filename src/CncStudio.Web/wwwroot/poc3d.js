// Prova de conceito do 3D (Fase 1, caminho B): three.js desenha a malha que o Core gera.
// Só é carregado na tela de teste (?poc3d); o app normal não usa este arquivo.
import * as THREE from './lib/three/three.module.min.js';
import { OrbitControls } from './lib/three/OrbitControls.js';

let renderer = null, scene = null, camera = null, controls = null, mesh = null, host = null, raf = 0;
let frames = 0, lastTick = 0, fps = 0, framed = false;

// topFraction: onde a área 3D começa, como fração da altura da janela (0 a 1)
export function start(topFraction) {
  stop();
  host = document.createElement('div');
  host.id = 'poc3d-host';
  host.style.cssText = 'position:fixed;left:0;right:0;bottom:0;top:' + (topFraction * 100) + '%;z-index:50;background:#0d1117;touch-action:none';
  document.body.appendChild(host);

  renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.setSize(host.clientWidth, host.clientHeight);
  host.appendChild(renderer.domElement);

  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0d1117);
  scene.add(new THREE.HemisphereLight(0xffffff, 0x404858, 1.1));
  const sun = new THREE.DirectionalLight(0xffffff, 1.6);
  sun.position.set(0.5, -0.8, 1.2);
  scene.add(sun);

  camera = new THREE.PerspectiveCamera(35, host.clientWidth / Math.max(1, host.clientHeight), 1, 20000);
  camera.up.set(0, 0, 1);
  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;

  window.addEventListener('resize', resize);
  frames = 0; lastTick = performance.now(); fps = 0; framed = false;
  const loop = () => {
    raf = requestAnimationFrame(loop);
    controls.update();
    renderer.render(scene, camera);
    frames++;
    const now = performance.now();
    if (now - lastTick >= 1000) { fps = frames * 1000 / (now - lastTick); frames = 0; lastTick = now; }
  };
  loop();
  const gl = renderer.getContext();
  const info = gl.getExtension('WEBGL_debug_renderer_info');
  return info ? String(gl.getParameter(info.UNMASKED_RENDERER_WEBGL)) : String(gl.getParameter(gl.RENDERER));
}

function resize() {
  if (!host) return;
  renderer.setSize(host.clientWidth, host.clientHeight);
  camera.aspect = host.clientWidth / Math.max(1, host.clientHeight);
  camera.updateProjectionMatrix();
}

// Recebe a malha pronta: bytes de Float32 (posições, normais, cores) e Uint32 (índices).
// Os bytes chegam como cópia (slice), pois a memória do .NET pode mudar depois da chamada.
export function setMesh(positions, normals, colors, indices) {
  if (!scene) return;
  const toF32 = v => { const b = v.slice(); return new Float32Array(b.buffer, b.byteOffset, b.byteLength / 4); };
  const toU32 = v => { const b = v.slice(); return new Uint32Array(b.buffer, b.byteOffset, b.byteLength / 4); };
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(toF32(positions), 3));
  geometry.setAttribute('normal', new THREE.BufferAttribute(toF32(normals), 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(toF32(colors), 3));
  geometry.setIndex(new THREE.BufferAttribute(toU32(indices), 1));
  geometry.computeBoundingSphere();
  if (mesh) { mesh.geometry.dispose(); mesh.geometry = geometry; }
  else {
    mesh = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ vertexColors: true, metalness: 0.15, roughness: 0.6 }));
    scene.add(mesh);
  }
  if (!framed) {
    const s = geometry.boundingSphere;
    controls.target.copy(s.center);
    // Distância para a peça inteira caber, também com a tela em pé (aspecto < 1)
    const half = THREE.MathUtils.degToRad(camera.fov / 2);
    const dist = s.radius / Math.sin(half) / Math.min(1, camera.aspect) * 1.05;
    const dir = new THREE.Vector3(0.45, -0.62, 0.64).normalize();
    camera.position.copy(s.center).addScaledVector(dir, dist);
    camera.near = dist / 100; camera.far = dist * 10; camera.updateProjectionMatrix();
    framed = true;
  }
}

export function getFps() { return fps; }

// A barra de cima mudou de altura (rotação da tela, quebra de linha): a área 3D acompanha
export function setTop(topFraction) {
  if (!host) return;
  host.style.top = (topFraction * 100) + '%';
  resize();
}

export function stop() {
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', resize);
  if (mesh) { mesh.geometry.dispose(); mesh.material.dispose(); mesh = null; }
  if (renderer) { renderer.dispose(); renderer = null; }
  if (host) { host.remove(); host = null; }
  scene = null; camera = null; controls = null;
}
