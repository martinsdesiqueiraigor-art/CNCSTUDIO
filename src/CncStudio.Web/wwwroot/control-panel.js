// Painel de controle da simulação: cartão estilo máquina CNC por cima do gráfico (2D e 3D).
// Mostra coordenadas absolutas, distância a percorrer, ferramenta, avanço, rotação, G ativos e o zero
// de peça. O C# manda os valores (ControlPanelState.ToJson); aqui só se desenha e se trata o arrastar
// e o expandir/recolher. O div cobre o gráfico mas deixa os gestos passarem (só o cartão pega toques).

const POS_KEY = 'cc.panel.pos';       // posição do cartão, em fração da área ("x,y")
const COLLAPSED_KEY = 'cc.panel.collapsed';

function read(key) { try { return localStorage.getItem(key); } catch { return null; } }
function write(key, value) { try { localStorage.setItem(key, value); } catch { /* sem armazenamento */ } }

const fmt = (n, digits = 3) => (Math.abs(n) < 0.0005 ? 0 : n).toFixed(digits);

/** Cria a camada transparente com o cartão dentro. O Avalonia encaixa o div sobre o gráfico. */
export function create() {
  const host = document.createElement('div');
  host.style.cssText = 'position:relative;width:100%;height:100%;overflow:hidden;pointer-events:none;z-index:20';

  const card = document.createElement('div');
  card.style.cssText = 'position:absolute;display:none;pointer-events:auto;box-sizing:border-box;min-width:150px;max-width:calc(100% - 12px);'
    + 'background:rgba(13,17,23,.88);border:1px solid var(--cc-accent,#f0883e);border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.45);color:#e6edf3;'
    + 'font:12px/1.35 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;user-select:none;-webkit-user-select:none;touch-action:none;-webkit-tap-highlight-color:transparent';
  // Os eventos do cartão não sobem (o Avalonia que envolve o div os capturaria)
  for (const t of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'click', 'wheel', 'dblclick', 'contextmenu'])
    card.addEventListener(t, e => e.stopPropagation());

  const head = document.createElement('div');
  head.style.cssText = 'display:flex;align-items:center;gap:8px;padding:5px 8px;cursor:grab;touch-action:none';
  const title = document.createElement('span');
  title.style.cssText = 'flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-weight:700;color:var(--cc-accent,#f0883e)';
  const toggle = document.createElement('button');
  toggle.type = 'button';
  toggle.style.cssText = 'flex:none;width:28px;height:28px;padding:0;border-radius:8px;border:1px solid #303d4b;background:#161b22;color:#e6edf3;cursor:pointer;display:flex;align-items:center;justify-content:center';
  head.append(title, toggle);
  const body = document.createElement('div');
  body.style.cssText = 'padding:0 10px 8px';
  card.append(head, body);
  host.appendChild(card);

  const v = { host, card, head, title, toggle, body, data: null, collapsed: null, x: 0.02, y: 0.03 };
  const saved = (read(POS_KEY) || '').split(',').map(Number);
  if (saved.length === 2 && saved.every(Number.isFinite)) { v.x = saved[0]; v.y = saved[1]; }
  host.__panel = v;

  toggle.addEventListener('click', () => { v.collapsed = !isCollapsed(v); write(COLLAPSED_KEY, v.collapsed ? '1' : '0'); render(v); });
  attachDrag(v);
  new ResizeObserver(() => place(v)).observe(host);
  return host;
}

function isCollapsed(v) {
  if (v.collapsed !== null) return v.collapsed;
  const saved = read(COLLAPSED_KEY);
  if (saved !== null) return saved === '1';
  return v.host.clientWidth < 520;   // celular em pé começa recolhido
}

function place(v) {
  const hw = v.host.clientWidth, hh = v.host.clientHeight;
  const w = v.card.offsetWidth, h = v.card.offsetHeight;
  if (!hw || !hh) return;
  const x = Math.max(0, Math.min(hw - w, v.x * hw)), y = Math.max(0, Math.min(hh - h, v.y * hh));
  v.card.style.left = x + 'px';
  v.card.style.top = y + 'px';
}

function attachDrag(v) {
  let drag = null;
  v.head.addEventListener('pointerdown', e => {
    if (e.target === v.toggle || v.toggle.contains(e.target)) return;
    v.head.setPointerCapture(e.pointerId);
    v.head.style.cursor = 'grabbing';
    const r = v.card.getBoundingClientRect();
    drag = { ox: e.clientX - r.left, oy: e.clientY - r.top, moved: false, sx: e.clientX, sy: e.clientY };
  });
  v.head.addEventListener('pointermove', e => {
    if (!drag) return;
    if (!drag.moved && Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) < 4) return;
    drag.moved = true;
    const h = v.host.getBoundingClientRect();
    v.x = (e.clientX - h.left - drag.ox) / Math.max(1, h.width);
    v.y = (e.clientY - h.top - drag.oy) / Math.max(1, h.height);
    place(v);
  });
  const end = () => {
    if (!drag) return;
    const moved = drag.moved; drag = null;
    v.head.style.cursor = 'grab';
    if (moved) {
      // guarda a posição já limitada à área
      const hw = v.host.clientWidth, hh = v.host.clientHeight;
      v.x = Math.max(0, Math.min(1, parseFloat(v.card.style.left) / Math.max(1, hw)));
      v.y = Math.max(0, Math.min(1, parseFloat(v.card.style.top) / Math.max(1, hh)));
      write(POS_KEY, v.x.toFixed(4) + ',' + v.y.toFixed(4));
    } else {
      // um toque no cabeçalho também expande ou recolhe
      v.toggle.click();
    }
  };
  v.head.addEventListener('pointerup', end);
  v.head.addEventListener('pointercancel', () => { drag = null; place(v); });
}

const ICON_OPEN = '<svg viewBox="0 0 12 12" width="12" height="12" fill="currentColor" aria-hidden="true"><path d="M1,4 L6,9 L11,4 Z"/></svg>';
const ICON_CLOSED = '<svg viewBox="0 0 12 12" width="12" height="12" fill="currentColor" aria-hidden="true"><path d="M4,1 L9,6 L4,11 Z"/></svg>';

function row(cells, css = '') {
  const r = document.createElement('div');
  r.style.cssText = 'display:flex;gap:10px;align-items:baseline;' + css;
  for (const [text, style] of cells) {
    const c = document.createElement('span');
    c.textContent = text;
    c.style.cssText = style;
    r.appendChild(c);
  }
  return r;
}

function render(v) {
  const d = v.data;
  if (!d) { v.card.style.display = 'none'; return; }
  const collapsed = isCollapsed(v);
  v.card.style.display = 'block';
  v.toggle.innerHTML = collapsed ? ICON_CLOSED : ICON_OPEN;
  v.toggle.title = collapsed ? 'Expandir o painel' : 'Recolher o painel';
  v.toggle.setAttribute('aria-label', v.toggle.title);
  const tool = d.tool > 0 ? 'T' + String(d.tool).padStart(2, '0') : 'T--';
  v.body.replaceChildren();
  if (collapsed) {
    // Uma linha: eixos, ferramenta e zero de peça
    v.title.textContent = d.axes.map(a => a.n + ' ' + fmt(a.p)).join('  ') + '  ·  ' + tool + '  ·  ' + d.wcs;
    v.body.style.display = 'none';
    place(v);
    return;
  }
  v.title.textContent = 'PAINEL · ' + (d.line > 0 ? 'linha ' + d.line : 'início');
  v.body.style.display = 'block';
  const dim = 'color:#9baab9;';
  v.body.appendChild(row([['ABSOLUTA (' + d.unit + ')', dim + 'flex:1'], ['FALTA', dim + 'width:76px;text-align:right']], 'font-size:10px;margin-bottom:2px'));
  for (const a of d.axes) {
    v.body.appendChild(row([
      [a.n, 'color:var(--cc-accent,#f0883e);font-weight:700;width:14px'],
      [fmt(a.p), 'flex:1;text-align:right;font-size:15px;font-weight:700'],
      [fmt(a.d), 'width:76px;text-align:right;color:' + (Math.abs(a.d) < 0.0005 ? '#6b7a8a' : '#d4a017')],
    ]));
  }
  const sep = document.createElement('div');
  sep.style.cssText = 'height:1px;background:#303d4b;margin:6px 0';
  v.body.appendChild(sep);
  const feed = d.feed === null ? 'rápido' : 'F' + fmt(d.feed, 0);
  const spindle = d.spindle > 0 ? 'S' + fmt(d.spindle, 0) + (d.css ? ' m/min' : ' rpm') : 'S--';
  v.body.appendChild(row([[tool, 'color:#3b82f6;font-weight:700'], [feed, ''], [spindle, '']], 'flex-wrap:wrap'));
  v.body.appendChild(row([[d.g.join(' '), 'color:#2fa87c;white-space:normal']], 'margin-top:4px'));
  v.body.appendChild(row([['ZERO DE PEÇA', dim + 'font-size:10px'], [d.wcs, 'color:var(--cc-accent,#f0883e);font-weight:700']], 'margin-top:4px'));
  place(v);
}

/** Atualiza os valores mostrados (json do ControlPanelState; vazio esconde o cartão). */
export function update(host, json) {
  const v = host?.__panel;
  if (!v) return;
  v.data = json ? JSON.parse(json) : null;
  render(v);
}

export function dispose(host) {
  if (host) host.replaceChildren();
}
