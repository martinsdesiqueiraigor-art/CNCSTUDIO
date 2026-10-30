// Plataforma Casillas: tela inicial com os módulos e barra fixa inferior (Início, Code, Academy, Macro).
// Tudo em HTML; roda antes do .NET carregar. Para abrir Academy e Macro (que dependem do .NET), avisa o C#
// pelo callback registrado em onPlatform (web.js). Se o .NET ainda não carregou, guarda o pedido e entrega depois.

import { accountCheck, accountLogin, accountPanel, moduleKnown, moduleState } from './account.js';

const DOCK_H = 'calc(56px + env(safe-area-inset-bottom, 0px))';
const LAST_KEY = 'cc.platform.last';

// ids: code, academy, macro, cam = ativos; biblioteca = em breve
const MODULES = [
  { id: 'academy', img: 'ic-academy', name: 'Casillas Academy', text: 'Formação profissional', color: '#3ddc84', icon: '<path d="M2 9l10-5 10 5-10 5-10-5zM6 11.5V16c0 1.5 3 3 6 3s6-1.5 6-3v-4.5M22 9v6" />' },
  { id: 'cam', img: 'ic-cam', name: 'Casillas CAM', text: 'Do desenho ao programa 2,5D e 4º eixo', color: '#4d8dff', icon: '<path d="M3 17l6-6 4 4 8-9M15 6h6v6" />' },
  { id: 'assist', img: 'ic-assist', name: 'Casillas Assist', text: 'Sugestões e orçamento a partir do desenho', color: '#ffd24a', desktop: true },
  { id: 'code', img: 'ic-code', name: 'Casillas Code', text: 'Editor G-code e M-code', color: '#e36bf0', icon: '<path d="M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16" />' },
  { id: 'biblioteca', img: 'ic-biblioteca', name: 'Casillas Biblioteca CNC', text: 'Banco de dados G-code', color: '#ee5a45', soon: true, icon: '<path d="M4 4h5v16H4zM11 4h5v16h-5zM18 6l3 .8-3.5 13.2L14.5 19" />' },
  { id: 'macro', img: 'ic-macro', name: 'Casillas Macro', text: 'Programação paramétrica e lógica', color: '#9b7bff', icon: '<path d="M4 4h16v16H4zM4 9h16M9 9v11" />' },
  { id: 'otimizador', img: 'ic-otimizador', name: 'Casillas Otimizador', text: 'Encontra repetições e simplifica o programa', color: '#ffb454', icon: '<path d="M13 2L4 14h7l-1 8 9-12h-7z" />' },
  { id: 'calculadoras', img: 'ic-calculadoras', name: 'Casillas Calculadoras', text: 'Cálculos técnicos de usinagem', color: '#ff9a4a', url: 'https://martinsdesiqueiraigor-art.github.io/Casillas_app_2/', icon: '<path d="M6 3h12v18H6zM8 7h8M9 11h2M13 11h2M9 15h2M13 15h2" />' },
  { id: 'loja', img: 'ic-loja', name: 'Casillas Loja', text: 'Loja de ferramentas', color: '#2fd0c8', icon: '<path d="M14 6a4 4 0 0 0 5 5l-9 9a2.1 2.1 0 0 1-3-3l9-9a4 4 0 0 1-2-2z" />' },
];
const DOCK = ['inicio', 'code', 'academy', 'macro', 'otimizador'];

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

// Módulos que exigem teste ativo ou licença (o Code trava a simulação por dentro; a Academy abre o módulo 1)
const GATED = new Set(['macro', 'otimizador', 'cam']);
// Módulos que só travam depois que o servidor passa a listá-los (produto ainda inativo = abre livre, sem pedir conta)
const SOFT = new Set(['cam']);
const BUY_URL = 'https://wa.me/5519996816755?text=' + encodeURIComponent('Olá! Quero comprar a licença do Casillas.');

/** Aviso "Comprar licença" quando o teste acabou. */
function buyDialog(mod) {
  const ov = make('div', null, 'position:fixed;inset:0;z-index:10040;background:rgba(0,0,0,.65);display:flex;align-items:center;justify-content:center;padding:16px;font:15px/1.4 system-ui,sans-serif');
  const box = make('div', null, `width:100%;max-width:380px;background:#161b22;color:#e6edf3;border:2px solid ${mod.color};border-radius:16px;padding:20px;display:flex;flex-direction:column;gap:12px;text-align:center;box-shadow:0 0 24px ${mod.color}55`);
  box.append(make('div', '🔒', 'font-size:34px'));
  box.append(make('strong', mod.name, `font-size:18px;color:${mod.color}`));
  const limit = moduleState(mod.id) === 'DEVICE_LIMIT';
  box.append(make('div', limit
    ? 'Sua conta já está em 2 aparelhos. Libere um deles em Conta (ou peça ao suporte) para usar aqui.'
    : 'Seu período de teste terminou. Este módulo precisa de licença para continuar.', 'color:#9baab9;font-size:14px'));
  const btn = (text, kind) => make('button', text, 'min-height:44px;border-radius:10px;font:600 15px system-ui;cursor:pointer;' + (kind === 'buy' ? `background:${mod.color};color:#0d1117;border:0` : 'background:transparent;color:#e6edf3;border:1px solid #30363d'));
  const buy = btn('Comprar licença', 'buy'); buy.type = 'button';
  buy.addEventListener('click', () => window.open(BUY_URL, '_blank', 'noopener'));
  const code = btn('Já tenho um código'); code.type = 'button';
  code.addEventListener('click', async () => {
    try {
      const action = await accountPanel(await accountCheck());
      if (action === 'refresh') { ov.remove(); openModule(mod.id); }
    } catch { /* sem conta */ }
  });
  const close = btn('Voltar'); close.type = 'button';
  close.addEventListener('click', () => ov.remove());
  box.append(buy, code, close);
  ov.append(box);
  document.body.append(ov);
}

/** Confere conta e licença do módulo. Resolve true se pode abrir. */
export function ensureAccess(id) { return allowed(MODULES.find(m => m.id === id)); }

async function allowed(mod) {
  if (SOFT.has(mod.id) && !moduleKnown(mod.id)) {
    // O servidor ainda não vende este módulo: abre livre e atualiza a situação em segundo plano
    accountCheck().catch(() => {});
    return true;
  }
  let st = moduleState(mod.id);
  if (st === 'VALID' || st === 'TRIAL') { accountCheck().catch(() => {}); return true; } // confere de novo em segundo plano
  try {
    let info = JSON.parse(await accountCheck());
    if (!info.signedIn) {
      if (await accountLogin('Crie uma conta para testar todos os módulos por 30 dias.') !== 'ok') return false;
      info = JSON.parse(await accountCheck());
    }
    if (!info.signedIn) return false;
  } catch { toast('Conecte-se à internet para conferir a licença'); return false; }
  st = moduleState(mod.id);
  if (st === 'VALID' || st === 'TRIAL') return true;
  if (st === null) { toast('Conecte-se à internet para conferir a licença'); return false; }
  buyDialog(mod);
  return false;
}

/** Abre um módulo (ou o início). */
export function openModule(id) {
  const mod = MODULES.find(m => m.id === id);
  if (mod && mod.url) { window.open(mod.url, '_blank', 'noopener'); return; }
  if (mod && mod.soon) { toast(mod.name + ': em breve'); return; }
  if (mod && mod.desktop) { toast(mod.name + ' está no Casillas Code para Windows'); return; }
  if (GATED.has(id)) { allowed(mod).then(ok => { if (ok) doOpen(id); }); return; }
  doOpen(id);
}

function doOpen(id) {
  if (id === 'inicio') {
    closeOverlays();
    current = 'inicio';
    showHome();
  } else {
    hideHome();
    closeOverlays();
    current = id;
    store.set(id);
    enter(id);
  }
  drawDock();
}

/** Entra no módulo: a Loja é desta página; Academy, Macro e Otimizador são entregues ao app. */
function enter(id) {
  if (id === 'loja') import('./store-ui.js').then(m => m.openStore());
  else if (id !== 'code') deliver(id);
}

function hideHome() { if (homeEl) { homeEl.remove(); homeEl = null; } }

// Situação da licença guardada pela conta (account.js), só para a etiqueta: nada é decidido aqui
function licenseChip() {
  try {
    const st = JSON.parse(localStorage.getItem('cc.license') || 'null');
    if (!st) return null;
    if (st.state === 'licensed') return { text: 'Licença ativa', kind: 'ok' };
    if (st.state === 'trial' && st.trial_ends_at) {
      const days = Math.max(0, Math.ceil((Date.parse(st.trial_ends_at) - Date.now()) / 86400000));
      return { text: days > 1 ? `Trial · ${days} dias` : (days === 1 ? 'Trial · último dia' : 'Trial termina hoje'), kind: days <= 5 ? 'warn' : 'info' };
    }
    if (st.state === 'expired') return { text: 'Trial encerrado', kind: 'warn' };
    if (st.state === 'device_limit') return { text: 'Limite de aparelhos', kind: 'warn' };
  } catch { /* sem armazenamento */ }
  return null;
}

function chip(text, kind) {
  const c = { ok: ['#3ddc84', 'rgba(61,220,132,.12)'], warn: ['#ffd7a8', 'rgba(240,136,62,.18)'], info: ['#7cc4ff', 'rgba(77,141,255,.14)'] }[kind] || ['#9baab9', 'rgba(255,255,255,.06)'];
  return make('span', text, `display:inline-block;padding:3px 12px;border-radius:999px;font-size:12px;font-weight:700;color:${c[0]};background:${c[1]};border:1px solid ${c[0]}55`);
}

// O Consultor Técnico mora dentro do app Casillas (calculadora): o botão só abre esse módulo lá
const CONSULTOR_URL = 'https://martinsdesiqueiraigor-art.github.io/Casillas_app_2/#/modulo/consultor-tecnico';

function consultorBanner() {
  const b = make('div', null, 'position:relative;overflow:hidden;display:flex;align-items:center;gap:6px;min-height:128px;margin-bottom:10px;padding:12px 8px 12px 14px;border-radius:16px;border:1px solid #ff9a4d55;background:linear-gradient(120deg,#2b1a0b 0%,#161b22 70%);box-sizing:border-box');
  b.className = 'pl-banner';
  const txt = make('div', null, 'flex:1;min-width:0;display:flex;flex-direction:column;gap:5px;z-index:1');
  txt.append(
    make('div', 'CONSULTOR TÉCNICO', 'font-size:10.5px;font-weight:800;letter-spacing:.06em;color:#ff9a4d;white-space:nowrap'),
    make('strong', 'Tire suas dúvidas de usinagem', 'font-size:15px;line-height:1.2;color:#f3d9a4'),
    make('span', 'Orientação rápida sobre ferramentas e parâmetros. Confira sempre no catálogo do fabricante.', 'font-size:11.5px;line-height:1.3;color:#9baab9'),
  );
  const go = make('button', 'Abrir Consultor Técnico', 'align-self:flex-start;margin-top:3px;min-height:38px;padding:0 12px;white-space:nowrap;border:0;border-radius:10px;background:linear-gradient(180deg,#ff8a33,#ef6a0e);color:#1a0d02;font:800 12.5px system-ui,sans-serif;cursor:pointer');
  go.type = 'button';
  go.addEventListener('click', () => window.open(CONSULTOR_URL, '_blank', 'noopener'));
  txt.append(go);
  const pics = make('div', null, 'flex:none;display:flex;align-items:flex-end;margin-right:-6px');
  pics.className = 'pl-mascots';
  const cap = make('img', null, 'height:86px;width:auto;margin-right:-20px;filter:drop-shadow(0 4px 8px rgba(0,0,0,.5))');
  cap.src = './img/plataforma/mascote-capivara.webp'; cap.alt = ''; cap.loading = 'lazy';
  const qua = make('img', null, 'height:80px;width:auto;filter:drop-shadow(0 4px 8px rgba(0,0,0,.5))');
  qua.src = './img/plataforma/mascote-quati.webp'; qua.alt = ''; qua.loading = 'lazy';
  pics.append(cap, qua);
  b.append(txt, pics);
  return b;
}

let homeStyle = null;
function ensureHomeStyle() {
  if (homeStyle) return;
  homeStyle = document.createElement('style');
  // a tela inicial cabe sem rolar: em telas baixas some a descrição e a linha de áreas
  homeStyle.textContent = [
    // celular em pé: logo grande, 3 colunas de cartões compactos (a última linha fica centralizada)
    '.pl-grid{display:flex;flex-wrap:wrap;justify-content:center;align-content:center;gap:8px;flex:1;min-height:0}',
    '.pl-card{flex:0 0 calc((100% - 16px)/3);aspect-ratio:1/0.86;max-height:calc((100% - 8px)/2)}',
    '.pl-logo{width:clamp(120px,24vh,190px)!important;height:clamp(120px,24vh,190px)!important}',
    '.pl-sub{display:none!important}',
    '@media (max-width:599px){.pl-wrap{flex:none!important}.pl-right{flex:none!important}.pl-grid{flex:none!important;align-content:flex-start!important}.pl-card{max-height:none!important}}',
    '@media (max-height:620px){.pl-tag{display:none!important}}',
    // celular deitado / tela larga: logo e título à esquerda, 4 colunas × 2 linhas à direita (sem cartão sozinho)
    '@media (min-width:600px){.pl-wrap{flex-direction:row!important;align-items:center;gap:14px;max-width:980px!important}',
    '.pl-wrap{align-items:flex-start!important;flex:none!important}.pl-left{flex:0 0 36%;display:flex;flex-direction:column}',
    '.pl-right{flex:1;display:flex;flex-direction:column;min-width:0}',
    '.pl-grid{flex:none!important;align-content:flex-start!important}.pl-card{flex:0 0 calc((100% - 32px)/5);aspect-ratio:1/0.95;max-height:none!important;height:auto!important}',
    '}',
    '@media (min-width:600px) and (min-height:700px){.pl-sub{display:-webkit-box!important}}',
  ].join('');
  document.head.append(homeStyle);
}

function showHome() {
  hideHome();
  ensureHomeStyle();
  const el = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h);z-index:9500;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px/1.4 system-ui,-apple-system,Roboto,sans-serif;padding:calc(8px + env(safe-area-inset-top, 0px)) 10px 8px;box-sizing:border-box;display:flex;flex-direction:column');
  const wrap = make('div', null, 'max-width:760px;width:100%;margin:0 auto;flex:1;display:flex;flex-direction:column;min-height:0');
  wrap.className = 'pl-wrap';

  const head = make('div', null, 'display:flex;align-items:center;gap:10px;flex:none;margin-bottom:8px');
  head.className = 'pl-head';
  const logo = make('img', null, 'width:64px;height:64px;object-fit:contain;flex:none;-webkit-mask-image:radial-gradient(circle at center,#000 62%,transparent 76%);mask-image:radial-gradient(circle at center,#000 62%,transparent 76%)');
  logo.className = 'pl-logo2'; logo.src = './img/plataforma/topo.webp'; logo.alt = 'Casillas';
  const titles = make('div', null, 'flex:1;min-width:0');
  titles.append(make('h1', 'Casillas', 'font-size:22px;line-height:1.1;margin:0;color:#f3d9a4'), make('div', 'Plataforma de Usinagem CNC', 'font-size:12px;color:#9baab9'));
  head.append(logo, titles);
  const gear = make('button', null, 'flex:none;width:40px;height:40px;border-radius:12px;border:1px solid #30363d;background:#161b22;display:flex;align-items:center;justify-content:center;cursor:pointer');
  gear.type = 'button'; gear.setAttribute('aria-label', 'Conta e licença');
  gear.innerHTML = svg('<circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />', '#c9d1d9', 20);
  gear.addEventListener('click', async () => {
    try { if (await accountPanel(await accountCheck()) === 'refresh') showHome(); } catch { toast('Conecte-se à internet para abrir a conta'); }
  });
  head.append(gear);
  // Etiquetas de situação: licença/teste (lidas da última situação confirmada) e uso sem internet
  const chips = make('div', null, 'display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px');
  chips.className = 'pl-chips';
  const lic = licenseChip();
  if (lic) chips.append(chip(lic.text, lic.kind));
  if (navigator.serviceWorker && navigator.serviceWorker.controller) chips.append(chip('Offline disponível', 'info'));
  const banner = consultorBanner();
  const left = make('div', null, ''); left.className = 'pl-left'; left.append(head, chips, banner);
  const right = make('div', null, 'display:flex;flex-direction:column;flex:1;min-height:0'); right.className = 'pl-right';
  wrap.append(left, right);

  const lastId = store.get();
  const grid = make('div', null, ''); grid.className = 'pl-grid';
  MODULES.forEach(m => {
    const c = make('button', null, `position:relative;box-sizing:border-box;display:flex;flex-direction:column;align-items:center;gap:2px;text-align:center;min-height:0;min-width:0;border:2px solid ${m.color};border-radius:14px;background:linear-gradient(160deg,${m.color}26,#0f151c 62%);box-shadow:0 0 10px ${m.color}55,inset 0 0 12px ${m.color}14;color:#e6edf3;padding:6px 6px 8px;cursor:pointer;font:inherit;overflow:hidden;${m.soon ? 'opacity:.72' : ''}`);
    c.type = 'button'; c.className = 'pl-card';
    const ic = make('div', null, 'flex:1;min-height:0;width:100%;display:flex;align-items:center;justify-content:center');
    if (m.img) {
      const im = make('img', null, 'max-height:100%;max-width:100%;height:auto;width:auto;aspect-ratio:1;object-fit:contain' + (!m.img.endsWith('.svg') && !m.img.startsWith('ic-') ? ';-webkit-mask-image:radial-gradient(circle at center,#000 58%,transparent 76%);mask-image:radial-gradient(circle at center,#000 58%,transparent 76%)' : ''));
      im.src = './img/plataforma/' + (m.img.includes('.') ? m.img : m.img + '.webp'); im.alt = ''; im.loading = 'lazy';
      ic.append(im);
    } else {
      ic.innerHTML = svg(m.icon, m.color, 40);
    }
    const sub = make('span', m.text, 'color:#9baab9;font-size:11px;line-height:1.25;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden');
    sub.className = 'pl-sub';
    c.append(ic, make('strong', m.name, `font-size:12px;line-height:1.15;color:${m.color};flex:none`), sub);
    if (m.desktop) c.append(make('span', 'Windows', 'position:absolute;top:5px;right:5px;font-size:9.5px;font-weight:700;color:#ffe9a8;background:rgba(70,56,14,.92);border:1px solid #7a6122;border-radius:999px;padding:0 6px'));
    else if (m.soon) c.append(make('span', 'Em breve', 'position:absolute;top:6px;right:6px;font-size:10px;font-weight:700;color:#f3c969;background:rgba(59,47,18,.9);border:1px solid #7a6122;border-radius:999px;padding:0 7px'));
    else if (GATED.has(m.id) && moduleState(m.id) === 'EXPIRED') c.append(make('span', '🔒 Licença', 'position:absolute;top:6px;right:6px;font-size:10px;font-weight:700;color:#ffd7a8;background:rgba(80,40,10,.92);border:1px solid #a05a1d;border-radius:999px;padding:0 7px'));
    else if (m.id === lastId) c.append(make('span', 'Último', 'position:absolute;top:6px;left:6px;font-size:10px;font-weight:700;color:#0d1117;background:' + m.color + ';border-radius:999px;padding:0 7px'));
    c.addEventListener('click', () => openModule(m.id));
    grid.append(c);
  });
  right.append(grid);
  const tag = make('div', 'Formação profissional • Gestão de usinagem • Banco de dados G-code • Análise e otimização • Editores avançados • Gestão de ativos', 'text-align:center;color:#c9a96a;font-size:10.5px;font-weight:700;letter-spacing:.2px;margin:8px 6px 0;line-height:1.4;flex:none');
  tag.className = 'pl-tag';
  right.append(tag);
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
  // Atalho do tema (padrão ↔ CMYK), disponível em qualquer módulo
  const t = make('button', null, 'flex:none;width:58px;border:none;background:none;color:#9baab9;font:700 11.5px system-ui,sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;cursor:pointer;padding:0');
  t.type = 'button';
  t.setAttribute('aria-label', 'Trocar tema (padrão ou CMYK)');
  const ti = make('span');
  ti.innerHTML = svg('<circle cx="12" cy="12" r="9" /><path d="M12 3v18" /><path d="M12 3a9 9 0 0 1 0 18z" fill="#9baab9" />', '#9baab9', 22);
  t.append(ti, make('span', 'Tema'));
  t.addEventListener('click', () => window.dispatchEvent(new CustomEvent('casillas:cmyk-toggle')));
  dockEl.append(t);
}

// O Academy/Macro fechou sozinho (a pessoa tocou em Fechar): o Code passa a ser o atual
window.addEventListener('casillas:module-closed', e => {
  if (current === e.detail) { current = 'code'; drawDock(); }
});

function init() {
  document.documentElement.style.setProperty('--dock-h', DOCK_H);
  const style = document.createElement('style');
  // #out relativo e cortado: o canvas do Avalonia é absoluto e, sem isso, ocupa a tela toda e a barra inferior cobre o rodapé do app
  style.textContent = '#out{position:relative!important;overflow:hidden!important;height:calc(100vh - var(--dock-h))!important;height:calc(100dvh - var(--dock-h))!important}';
  document.head.append(style);

  // Link direto: #academy, #macro ou #code abre o módulo; sem marca, mostra a tela inicial
  const wanted = (location.hash || '').replace('#', '').toLowerCase();
  const direct = MODULES.find(m => m.id === wanted && !m.soon);
  if (direct && !GATED.has(direct.id)) {
    current = direct.id;
    store.set(direct.id);
    enter(direct.id);
  } else {
    showHome();
    if (direct) openModule(direct.id); // módulo com trava: confere a conta antes de abrir
  }
  drawDock();
  // atualiza o aviso de teste e os cadeados com a situação do servidor
  accountCheck().then(() => { if (current === 'inicio' && homeEl) showHome(); }).catch(() => {});
}

if (document.body) init(); else document.addEventListener('DOMContentLoaded', init);
