// Plataforma Casillas: tela inicial com os módulos e barra fixa inferior (Início, Code, Academy, Macro).
// Tudo em HTML; roda antes do .NET carregar. Para abrir Academy e Macro (que dependem do .NET), avisa o C#
// pelo callback registrado em onPlatform (web.js). Se o .NET ainda não carregou, guarda o pedido e entrega depois.

const DOCK_H = 'calc(56px + env(safe-area-inset-bottom, 0px))';
const LAST_KEY = 'cc.platform.last';

// ids: code, academy, macro = ativos; cam, biblioteca, ferramentas = em breve
const MODULES = [
  { id: 'academy', name: 'Casillas Academy', text: 'Formação profissional', color: '#3ddc84', icon: '<path d="M2 9l10-5 10 5-10 5-10-5zM6 11.5V16c0 1.5 3 3 6 3s6-1.5 6-3v-4.5M22 9v6" />' },
  { id: 'cam', name: 'Casillas CAM', text: 'Do desenho ao programa 2,5D', color: '#4d8dff', soon: true, icon: '<path d="M3 17l6-6 4 4 8-9M15 6h6v6" />' },
  { id: 'code', name: 'Casillas Code', text: 'Editor G-code e M-code', color: '#e36bf0', icon: '<path d="M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16" />' },
  { id: 'biblioteca', name: 'Casillas Biblioteca CNC', text: 'Banco de dados G-code', color: '#ee5a45', soon: true, icon: '<path d="M4 4h5v16H4zM11 4h5v16h-5zM18 6l3 .8-3.5 13.2L14.5 19" />' },
  { id: 'macro', name: 'Casillas Macro', text: 'Programação paramétrica e lógica', color: '#9b7bff', icon: '<path d="M4 4h16v16H4zM4 9h16M9 9v11" />' },
  { id: 'ferramentas', name: 'Casillas Ferramentas', text: 'Gestão e seleção de ferramentas', color: '#2fd0c8', soon: true, icon: '<path d="M14 6a4 4 0 0 0 5 5l-9 9a2.1 2.1 0 0 1-3-3l9-9a4 4 0 0 1-2-2z" />' },
];
const DOCK = ['inicio', 'code', 'academy', 'macro'];

const state = (window.casillasPlatform = window.casillasPlatform || { handler: null, pending: [] });
let current = 'inicio';
let homeEl = null, dockEl = null;

const make = (tag, text, style) => {
  const el = document.createElement(tag);
  if (text != null) el.textContent = text;
  if (style) el.style.cssText = style;
  return el;
};
const svg = (paths, color, size) =>
  `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="${color}" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">${paths}</svg>`;
const store = {
  get() { try { return localStorage.getItem(LAST_KEY); } catch { return null; } },
  set(v) { try { localStorage.setItem(LAST_KEY, v); } catch { /* sem armazenamento */ } },
};

function toast(text) {
  const t = make('div', text, 'position:fixed;left:50%;transform:translateX(-50%);bottom:calc(var(--dock-h) + 14px);z-index:10050;background:#21262d;color:#e6edf3;border:1px solid #4a5665;border-radius:10px;padding:10px 16px;font:600 14px system-ui,sans-serif;max-width:88vw;text-align:center');
  document.body.append(t);
  setTimeout(() => t.remove(), 2200);
}

function closeOverlays() { window.dispatchEvent(new Event('casillas:close-overlays')); }

function deliver(id) {
  if (state.handler) { try { state.handler(id); } catch { /* ignora */ } }
  else state.pending.push(id);
}

/** Abre um módulo (ou o início). */
export function openModule(id) {
  const mod = MODULES.find(m => m.id === id);
  if (mod && mod.soon) { toast(mod.name + ': em breve'); return; }
  if (id === 'inicio') {
    closeOverlays();
    current = 'inicio';
    showHome();
  } else {
    hideHome();
    closeOverlays();
    current = id;
    store.set(id);
    if (id !== 'code') deliver(id);
  }
  drawDock();
}

function hideHome() { if (homeEl) { homeEl.remove(); homeEl = null; } }

function showHome() {
  hideHome();
  const el = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h);z-index:9500;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px/1.5 system-ui,-apple-system,Roboto,sans-serif;padding:calc(18px + env(safe-area-inset-top, 0px)) 14px 24px;box-sizing:border-box');
  const wrap = make('div', null, 'max-width:720px;margin:0 auto');

  const head = make('div', null, 'text-align:center;margin:6px 0 18px');
  const logo = make('img', null, 'width:76px;height:76px;border-radius:18px');
  logo.src = './icone.png'; logo.alt = 'Casillas';
  head.append(logo, make('h1', 'Plataforma Casillas', 'font-size:24px;margin:10px 0 2px;color:#f3d9a4'), make('div', 'Escolha o módulo', 'color:#9baab9;font-size:14px'));
  wrap.append(head);
  const mq = window.matchMedia('(min-width: 600px)');
  el.style.setProperty('--cols', mq.matches ? '3' : '2');

  const last = MODULES.find(m => m.id === store.get() && !m.soon);
  if (last) {
    const c = make('button', null, `width:100%;display:flex;align-items:center;gap:12px;text-align:left;min-height:64px;border:1px solid ${last.color};border-radius:12px;background:#131a22;color:#e6edf3;padding:10px 14px;margin-bottom:16px;cursor:pointer;font:inherit`);
    c.type = 'button';
    c.innerHTML = svg(last.icon, last.color, 28);
    const t = make('div', null, 'flex:1');
    t.append(make('div', 'Continuar de onde parou', 'font-size:12px;color:#9baab9'), make('strong', last.name));
    c.append(t, make('span', '›', 'font-size:24px;color:' + last.color));
    c.addEventListener('click', () => openModule(last.id));
    wrap.append(c);
  }

  const grid = make('div', null, 'display:grid;grid-template-columns:repeat(var(--cols,2),1fr);gap:12px');
  MODULES.forEach(m => {
    const c = make('button', null, `position:relative;display:flex;flex-direction:column;gap:6px;text-align:left;min-height:150px;border:2px solid ${m.color};border-radius:16px;background:linear-gradient(160deg,${m.color}26,#0f151c 60%);box-shadow:0 0 14px ${m.color}55,inset 0 0 16px ${m.color}14;color:#e6edf3;padding:14px;cursor:pointer;font:inherit;${m.soon ? 'opacity:.7' : ''}`);
    c.type = 'button';
    const ic = make('div', null, `width:48px;height:48px;border-radius:12px;display:flex;align-items:center;justify-content:center;background:${m.color}26`);
    ic.innerHTML = svg(m.icon, m.color, 26);
    c.append(ic, make('strong', m.name, `font-size:15px;line-height:1.25;color:${m.color}`), make('span', m.text, 'color:#9baab9;font-size:12.5px;line-height:1.35'));
    if (m.soon) c.append(make('span', 'Em breve', 'position:absolute;top:10px;right:10px;font-size:11px;font-weight:700;color:#f3c969;background:#3b2f12;border:1px solid #7a6122;border-radius:999px;padding:1px 8px'));
    c.addEventListener('click', () => openModule(m.id));
    grid.append(c);
  });
  wrap.append(grid);
  wrap.append(make('div', 'Formação profissional • Gestão de usinagem • Banco de dados G-code • Análise e otimização • Editores avançados • Gestão de ativos', 'text-align:center;color:#c9a96a;font-size:11.5px;font-weight:700;letter-spacing:.3px;margin:18px 8px 0;line-height:1.6'));
  el.append(wrap);
  document.body.append(el);
  homeEl = el;
}

function drawDock() {
  if (!dockEl) {
    dockEl = make('nav', null, 'position:fixed;left:0;right:0;bottom:0;height:var(--dock-h);z-index:9000;background:#0d1117;border-top:1px solid #303d4b;display:flex;padding:0 6px env(safe-area-inset-bottom,0px);box-sizing:border-box');
    document.body.append(dockEl);
  }
  dockEl.replaceChildren();
  DOCK.forEach(id => {
    const m = MODULES.find(x => x.id === id);
    const active = id === current;
    const color = active ? (m ? m.color : '#ffb454') : '#9baab9';
    const b = make('button', null, `flex:1;border:none;background:none;color:${color};font:700 11.5px system-ui,sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;cursor:pointer;border-top:3px solid ${active ? color : 'transparent'}`);
    b.type = 'button';
    const icon = make('span');
    icon.innerHTML = m ? svg(m.icon, color, 22) : svg('<path d="M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10" />', color, 22);
    b.append(icon, make('span', id === 'inicio' ? 'Início' : m.name.replace('Casillas ', '')));
    b.addEventListener('click', () => { if (id !== current || id === 'inicio') openModule(id); });
    dockEl.append(b);
  });
}

// O Academy/Macro fechou sozinho (a pessoa tocou em Fechar): o Code passa a ser o atual
window.addEventListener('casillas:module-closed', e => {
  if (current === e.detail) { current = 'code'; drawDock(); }
});

function init() {
  document.documentElement.style.setProperty('--dock-h', DOCK_H);
  const style = document.createElement('style');
  style.textContent = '#out{height:calc(100vh - var(--dock-h))!important;height:calc(100dvh - var(--dock-h))!important}';
  document.head.append(style);

  // Link direto: #academy, #macro ou #code abre o módulo; sem marca, mostra a tela inicial
  const wanted = (location.hash || '').replace('#', '').toLowerCase();
  const direct = MODULES.find(m => m.id === wanted && !m.soon);
  if (direct) {
    current = direct.id;
    store.set(direct.id);
    if (direct.id !== 'code') deliver(direct.id);
  } else {
    showHome();
  }
  drawDock();
}

if (document.body) init(); else document.addEventListener('DOMContentLoaded', init);
