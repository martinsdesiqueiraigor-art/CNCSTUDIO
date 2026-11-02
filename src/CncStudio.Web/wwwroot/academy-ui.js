// Casillas Code: Academy (cursos). Tela em HTML; conteúdo, correção e progresso vêm do Core (C#).
// call(comando, argsJson) -> JSON. Comandos: catalog | lesson | final | grade | check | done.
// O progresso fica no aparelho (localStorage, opcional). Resolve com JSON {name, text, view3d} ou null.
// Visual em academy.css (cores do app: escuro, claro e CMYK).
const PROGRESS_KEY = 'cc.academy.progress';
// Última aula aberta e a aba de cada aula: ao reabrir a Academy, volta ao mesmo lugar
const UI_KEY = 'cc.academy.ui';
import { moduleState } from './account.js';
import { ensureAccess, openModule } from './platform-ui.js';

// O módulo 1 fica aberto para todos (com ou sem conta); os demais exigem teste ativo ou licença
const OPEN_MODULES = new Set(['acad-m1']);
const CHANNEL_URL = 'https://www.youtube.com/@casillasusinagembr';

const readUi = () => { try { return JSON.parse(localStorage.getItem(UI_KEY) || '{}') || {}; } catch { return {}; } };
const writeUi = ui => { try { localStorage.setItem(UI_KEY, JSON.stringify(ui)); } catch { /* sem armazenamento: vale só nesta sessão */ } };
// Segue a opção "Modo claro do app" (o mesmo valor que o C# guarda)
const theme = () => { try { return localStorage.getItem('cc.theme') === 'light' ? 'light' : 'dark'; } catch { return 'dark'; } };
const reduceMotion = () => !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

let cssReady = null;
const loadCss = () => cssReady || (cssReady = new Promise(done => {
  if (document.getElementById('acad-css')) { done(); return; }
  const link = document.createElement('link');
  link.id = 'acad-css'; link.rel = 'stylesheet'; link.href = new URL('./academy.css', import.meta.url).href;
  link.onload = link.onerror = () => done();
  document.head.append(link);
  setTimeout(done, 1500);
}));

// ---------- Faixa "Voltar à aula" acima do Code (enquanto o programa veio da Academy) ----------
// Fica numa faixa própria no topo e encolhe a área do app (#out) na mesma altura: não cobre nenhum controle do Code.
let returnProgram = '';
const onProgramLoaded = e => {
  const name = String((e.detail && e.detail.name) || '').toUpperCase();
  if (name !== returnProgram) removeReturn();
};
function removeReturn() {
  const el = document.getElementById('acad-return');
  if (el) el.remove();
  document.documentElement.classList.remove('acad-ret');
  window.removeEventListener('casillas:close-overlays', removeReturn);
  window.removeEventListener('casillas:program-loaded', onProgramLoaded);
  if (el) window.dispatchEvent(new Event('resize'));
}
function showReturn(lessonTitle, programName) {
  removeReturn();
  returnProgram = String(programName || '').toUpperCase();
  const el = document.createElement('div');
  el.id = 'acad-return'; el.className = 'acad acad-return'; el.dataset.theme = theme();
  el.setAttribute('role', 'region'); el.setAttribute('aria-label', 'Academy');
  const back = document.createElement('button');
  back.type = 'button'; back.className = 'back';
  const label = document.createElement('span');
  label.textContent = lessonTitle;
  back.append('‹ Voltar à aula', label);
  back.addEventListener('click', () => { removeReturn(); openModule('academy'); });
  const close = document.createElement('button');
  close.type = 'button'; close.className = 'close'; close.textContent = '×'; close.setAttribute('aria-label', 'Fechar a faixa Voltar à aula');
  close.addEventListener('click', removeReturn);
  el.append(back, close);
  const out = document.getElementById('out');
  if (out && out.parentNode) out.parentNode.insertBefore(el, out); else document.body.prepend(el);
  document.documentElement.classList.add('acad-ret');
  window.dispatchEvent(new Event('resize'));
  // outro módulo aberto pela barra inferior ou outro programa no editor: a faixa sai
  window.addEventListener('casillas:close-overlays', removeReturn);
  window.addEventListener('casillas:program-loaded', onProgramLoaded);
}

export function openAcademy(call) {
  removeReturn();
  return loadCss().then(() => new Promise(resolve => {
    let progress = '';
    try { progress = localStorage.getItem(PROGRESS_KEY) || ''; } catch { progress = ''; }
    const saveProgress = text => {
      if (!text) return;
      progress = text;
      try { localStorage.setItem(PROGRESS_KEY, text); } catch { /* sem armazenamento: vale só nesta sessão */ }
    };
    const ask = (cmd, args) => {
      try { return JSON.parse(call(cmd, JSON.stringify({ ...args, progress }))); }
      catch (e) { return { ok: false, error: 'JS: ' + e }; }
    };
    const ui = readUi();
    ui.tabs = ui.tabs || {};
    const saveUi = () => writeUi(ui);

    const make = (tag, text, cls, style) => {
      const el = document.createElement(tag);
      if (text != null) el.textContent = text;
      if (cls) el.className = cls;
      if (style) el.style.cssText = style;
      return el;
    };
    // kind: '' (neutro), 'primary' (laranja), 'ok' (verde), 'ghost' (link)
    const btn = (label, kind, onClick, cls) => {
      const b = make('button', label, 'ac-btn' + (kind ? ' ' + kind : '') + (cls ? ' ' + cls : ''));
      b.type = 'button'; b.addEventListener('click', onClick); return b;
    };
    const badge = draft => make('span', draft ? 'Rascunho' : 'Revisado', 'ac-badge ' + (draft ? 'draft' : 'ok'));
    const card = cls => make('div', null, 'ac-card' + (cls ? ' ' + cls : ''));
    const hint = text => make('p', text, 'ac-hint');

    const root = make('div', null, 'acad acad-root');
    root.dataset.theme = theme();
    root.setAttribute('aria-label', 'Casillas Academy');
    const wrap = make('div', null, 'ac-wrap');
    root.append(wrap);
    document.body.append(root);

    let stops = []; // animações do mini 3D a parar ao trocar de tela
    const stopAll = () => { stops.forEach(s => s()); stops = []; };
    const onClose = () => finish(null);
    const finish = value => { stopAll(); window.removeEventListener('casillas:close-overlays', onClose); root.remove(); window.dispatchEvent(new CustomEvent('casillas:module-closed', { detail: 'academy' })); resolve(value); };
    window.addEventListener('casillas:close-overlays', onClose);
    const screen = (...nodes) => { stopAll(); if (navEl) { navEl.remove(); navEl = null; } wrap.replaceChildren(...nodes); root.scrollTop = 0; };
    const header = (title, onBack, draft) => {
      const bar = make('div', null, 'ac-top');
      const back = btn(onBack ? '‹ Voltar' : 'Fechar', '', onBack || (() => finish(null)));
      const h = make('h2', title);
      if (draft != null) h.append(badge(draft));
      bar.append(back, h);
      return bar;
    };

    // ---------- Cores: código G, termos-chave e tipos de cartão ----------
    const AXIS_COLORS = { X: '#ff7b7b', Y: '#6bdc8c', Z: '#6bb0ff' };
    const wordColor = w => {
      const c = w[0].toUpperCase();
      if (c === 'G') return '#ffb454';
      if (c === 'M') return '#c792ea';
      if (AXIS_COLORS[c]) return AXIS_COLORS[c];
      if (c === 'F' || c === 'S') return '#4fd1c5';
      if (c === 'T' || c === 'H' || c === 'D') return '#f3c969';
      if (c === 'O' || c === 'N') return '#8b9aa9';
      return null;
    };
    const CODE_WORD = /\b([GMXYZFSTHDON]-?\d+(?:\.\d+)?)(?![\w.])/g;
    // Cria os trechos de um texto: palavras de G-code em fonte de código e **termos** em destaque
    const richInto = (el, text) => {
      text.split(/(\*\*[^*]+\*\*)/).forEach(chunk => {
        if (/^\*\*[^*]+\*\*$/.test(chunk)) {
          el.append(make('strong', chunk.slice(2, -2), 'ac-strong'));
          return;
        }
        let last = 0, m;
        CODE_WORD.lastIndex = 0;
        while ((m = CODE_WORD.exec(chunk))) {
          if (!wordColor(m[1])) continue;
          if (m.index > last) el.append(document.createTextNode(chunk.slice(last, m.index)));
          el.append(make('code', m[1], null, 'font-weight:700;font-family:ui-monospace,Consolas,monospace'));
          last = m.index + m[1].length;
        }
        if (last < chunk.length) el.append(document.createTextNode(chunk.slice(last)));
      });
      return el;
    };
    const rich = (tag, text, cls) => richInto(make(tag, null, cls), text);
    // Linha de programa inteira colorida (comentário em cinza); só dentro de painel de código (escuro)
    const codeLine = (text, cls) => {
      const el = make('div', null, cls);
      const m = text.match(/^(.*?)(\(.*\))?\s*$/);
      const code = m ? m[1] : text, comment = m && m[2] ? m[2] : '';
      let last = 0, w;
      const re = /([A-Za-z])(-?\d+(?:\.\d+)?)/g;
      while ((w = re.exec(code))) {
        if (w.index > last) el.append(document.createTextNode(code.slice(last, w.index)));
        const col = wordColor(w[1]);
        el.append(col ? make('span', w[0], null, `color:${col};font-weight:600`) : document.createTextNode(w[0]));
        last = w.index + w[0].length;
      }
      if (last < code.length) el.append(document.createTextNode(code.slice(last)));
      if (comment) el.append(make('span', comment, null, 'color:#6b7a8a'));
      return el;
    };
    // Tipos de cartão do texto: [conceito] [atenção] [dica] [exemplo]
    const KINDS = {
      conceito: { icon: '💡', label: 'Conceito', color: '#4aa3ff' },
      atenção: { icon: '⚠️', label: 'Atenção', color: '#ff6b6b' },
      dica: { icon: '✅', label: 'Dica', color: '#3fb27f' },
      exemplo: { icon: '🧩', label: 'Exemplo', color: '#f0883e' },
    };
    const parseKind = par => {
      const m = par.match(/^\[(conceito|atenção|dica|exemplo)\]\s*/i);
      return m ? { tag: m[1].toLowerCase(), text: par.slice(m[0].length) } : { tag: 'conceito', text: par };
    };
    const paragraphs = text => (text || '').split(/\n\s*\n/).filter(s => s.trim());
    const LEVEL_COLORS = { 'Introdução': '#4aa3ff', 'Iniciante': '#3fb27f', 'Básico': '#d9a520', 'Intermediário': '#f0883e', 'Avançado': '#e5534b', 'Especialista': '#a371f7' };
    const callout = (kindTag, title, text) => {
      const k = KINDS[kindTag] || KINDS.conceito;
      const c = card('ac-callout');
      c.style.setProperty('--kind', k.color);
      c.append(make('div', `${k.icon} ${title || k.label}`, 'ac-kind'), rich('p', text, null));
      c.lastChild.style.margin = '0';
      return c;
    };

    // ---------- Abas inferiores: Início, Trilhas, Conquistas ----------
    let activeTrack = 'Comum', currentTab = 'inicio', navEl = null;
    const TABS = [['inicio', '🏠', 'Início'], ['trilhas', '🗺️', 'Trilhas'], ['conquistas', '🏅', 'Conquistas']];
    const bar = (pct, color, label) => {
      const o = make('div', null, 'ac-bar');
      o.setAttribute('role', 'progressbar'); o.setAttribute('aria-valuemin', '0'); o.setAttribute('aria-valuemax', '100');
      o.setAttribute('aria-valuenow', String(Math.round(pct)));
      if (label) o.setAttribute('aria-label', label);
      o.append(make('div', null, null, `width:${Math.max(0, Math.min(100, pct))}%` + (color ? `;background:${color}` : '')));
      return o;
    };
    const ring = (pct, label) => {
      const el = make('div', null, 'ac-ring');
      const r = 40, c = 2 * Math.PI * r;
      el.innerHTML = `<svg viewBox="0 0 100 100" width="92" height="92" aria-hidden="true"><circle cx="50" cy="50" r="${r}" fill="none" stroke="var(--border)" stroke-width="10"/><circle cx="50" cy="50" r="${r}" fill="none" stroke="var(--accent)" stroke-width="10" stroke-linecap="round" stroke-dasharray="${(c * pct / 100).toFixed(1)} ${c.toFixed(1)}" transform="rotate(-90 50 50)"/></svg>`;
      el.append(make('span', label));
      return el;
    };
    const available = cat => cat.tracks.flatMap(t => t.levels.map(l => ({ track: t.name, level: l.level, module: l.module }))).filter(x => x.module && !x.module.soon);
    const findModule = id => {
      const cat = ask('catalog', {});
      return cat.tracks ? cat.tracks.flatMap(t => t.levels).map(l => l.module).find(x => x && x.id === id) : null;
    };
    const mkNav = () => {
      const n = make('nav', null, 'ac-nav');
      n.setAttribute('aria-label', 'Academy');
      TABS.forEach(([id, icon, label]) => {
        const b = make('button');
        b.type = 'button';
        if (id === currentTab) b.setAttribute('aria-current', 'page');
        const ic = make('span', icon, 'ic'); ic.setAttribute('aria-hidden', 'true');
        b.append(ic, make('span', label));
        b.addEventListener('click', () => { currentTab = id; home(); });
        n.append(b);
      });
      return n;
    };
    const tabScreen = (title, ...nodes) => {
      const top = header(title);
      top.querySelector('h2').prepend(make('div', 'Casillas Academy', 'ac-brand'));
      screen(top, ...nodes.filter(Boolean));
      navEl = mkNav();
      root.append(navEl);
    };

    const home = () => {
      ui.last = null; saveUi();
      const cat = ask('catalog', {});
      if (!cat.tracks) { tabScreen('Academy', hint('Não foi possível carregar os cursos.' + (cat.error ? ' (' + cat.error + ')' : ''))); return; }
      if (currentTab === 'trilhas') trilhasTab(cat);
      else if (currentTab === 'conquistas') conquistasTab(cat);
      else inicioTab(cat);
    };

    const TRACK_INFO = { Comum: ['📘', 'Base para fresa e torno'], Fresa: ['🛠️', 'Fresamento CNC'], Torno: ['⚙️', 'Torneamento CNC'] };
    const inicioTab = cat => {
      const mods = available(cat);
      const lessons = mods.flatMap(x => x.module.lessons.map(l => ({ ...l, mod: x.module, track: x.track, level: x.level })));
      const done = lessons.filter(l => l.done).length;
      const pct = lessons.length ? Math.round(100 * done / lessons.length) : 0;
      const next = lessons.find(l => !l.done);
      const top = card('ac-hero');
      top.append(ring(pct, pct + '%'));
      const info = make('div', null, null, 'flex:1;min-width:0');
      info.append(make('div', 'Olá! Bora estudar CNC?', null, 'font-size:18px;font-weight:700'), make('div', `${done} de ${lessons.length} aulas concluídas`, 'ac-muted'));
      if (cat.streak > 0) info.append(make('div', `🔥 ${cat.streak} ${cat.streak === 1 ? 'dia' : 'dias'} seguidos`, 'ac-streak'));
      top.append(info);

      const cont = card('tint');
      if (next) {
        const doneIn = next.mod.lessons.filter(l => l.done).length, total = next.mod.lessons.length;
        const pos = next.mod.lessons.findIndex(l => l.id === next.id) + 1;
        const modPct = Math.round(100 * doneIn / total);
        cont.append(make('div', done > 0 ? 'Em andamento' : 'Comece por aqui', 'ac-label'));
        cont.append(make('div', next.mod.title, null, 'font-size:18px;font-weight:700;margin:4px 0 2px'));
        const row = make('div', null, null, 'display:flex;align-items:center;gap:10px');
        row.append(make('div', null, null, 'flex:1'), make('span', modPct + '%', 'ac-muted'));
        row.firstChild.append(bar(modPct, null, 'Progresso do módulo'));
        cont.append(row, make('div', `Aula ${pos} de ${total} · ${next.title}`, 'ac-muted', 'margin:6px 0 12px'));
        cont.append(btn(done > 0 ? '▶ Continuar' : '▶ Começar', 'primary', () => lessonScreen(next.id, next.mod), 'wide'));
        cont.lastChild.style.width = '100%';
      } else {
        cont.append(make('div', lessons.length ? '🎉 Você concluiu todas as aulas disponíveis!' : 'Os cursos estão chegando.', null, 'font-weight:700'), make('div', 'Faça os testes finais em Trilhas e volte em breve: há novos módulos a caminho.', 'ac-muted', 'margin-top:4px'));
      }

      const tracks = make('div', null, 'ac-tracks');
      cat.tracks.forEach(t => {
        const mods = t.levels.map(l => l.module).filter(m => m && !m.soon);
        const nL = mods.reduce((s, m) => s + m.lessons.length, 0), nD = mods.reduce((s, m) => s + m.lessons.filter(x => x.done).length, 0);
        const [icon, sub] = TRACK_INFO[t.name] || ['📘', ''];
        const b = make('button', null, 'ac-track');
        b.type = 'button';
        const ic = make('span', icon, 'ic'); ic.setAttribute('aria-hidden', 'true');
        b.append(ic, make('strong', t.name), make('span', sub, 'ac-muted'), make('span', mods.length ? `${nD}/${nL} aulas` : 'Em breve', 'ac-muted', 'margin-top:auto;font-weight:700'));
        b.addEventListener('click', () => { activeTrack = t.name; currentTab = 'trilhas'; home(); });
        tracks.append(b);
      });
      tabScreen('Início', hint('Cursos de CNC em português. Conteúdo em rascunho até a revisão.'), top, cont, make('div', 'Trilhas de aprendizado', 'ac-section'), tracks);
    };

    const trilhasTab = cat => {
      const seg = make('div', null, 'ac-seg');
      cat.tracks.forEach(t => {
        const b = make('button', t.name);
        b.type = 'button'; b.setAttribute('aria-pressed', String(t.name === activeTrack));
        b.addEventListener('click', () => { activeTrack = t.name; home(); });
        seg.append(b);
      });
      const track = cat.tracks.find(t => t.name === activeTrack) || cat.tracks[0];
      const list = make('div', null, 'ac-timeline');
      let currentMarked = false;
      track.levels.forEach(l => {
        const m = l.module, ok = m && !m.soon;
        const lc = LEVEL_COLORS[l.level] || 'var(--border)';
        const nDone = ok ? m.lessons.filter(x => x.done).length : 0;
        const complete = ok && nDone === m.lessons.length && (!m.hasFinal || m.badge);
        const isCurrent = ok && !complete && !currentMarked;
        if (isCurrent) currentMarked = true;
        const node = make('div', null, 'ac-node');
        node.style.setProperty('--lc', lc);
        const dot = make('div', complete ? '✓' : '', 'ac-dot' + (complete ? ' done' : isCurrent ? ' now' : ok ? '' : ' soon'));
        dot.setAttribute('aria-hidden', 'true');
        const c = card(ok ? 'click' : 'off');
        const head = make('div', null, 'ac-node-head');
        head.append(make('strong', l.level + (m && m.badge ? '  🏅' : ''), null, `color:${lc}`), make('span', ok ? (!OPEN_MODULES.has(m.id) && moduleState('academy') === 'EXPIRED' ? '🔒 Licença · ' : '') + `${nDone}/${m.lessons.length} aulas` : 'Em breve', 'ac-muted'));
        c.append(head);
        if (m) c.append(make('div', m.title, null, 'font-size:14px;margin-top:2px'));
        if (m && m.description) c.append(make('div', m.description, 'ac-muted', 'font-size:12px;margin-top:2px'));
        if (ok) {
          c.append(bar(100 * nDone / m.lessons.length, lc, 'Progresso de ' + l.level));
          if (m.draft) c.append(make('div', 'Conteúdo em rascunho: ainda não revisado.', null, 'color:var(--warn);font-size:12px;margin-top:6px'));
          c.setAttribute('role', 'button'); c.tabIndex = 0;
          const go = async () => { if (OPEN_MODULES.has(m.id) || await ensureAccess('academy')) moduleScreen(m); };
          c.addEventListener('click', go);
          c.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); } });
        }
        node.append(dot, c);
        list.append(node);
      });
      tabScreen('Trilhas', seg, list);
    };

    const conquistasTab = cat => {
      const mods = available(cat);
      const lessons = mods.flatMap(x => x.module.lessons);
      const done = lessons.filter(l => l.done).length;
      const stats = make('div', null, 'ac-stats');
      const stat = (n, label) => { const s = make('div', null, 'ac-stat'); s.append(make('b', String(n)), make('span', label, 'ac-muted')); return s; };
      const best = mods.length ? Math.max(...mods.map(x => x.module.finalBest)) : -1;
      stats.append(stat(`🔥 ${cat.streak || 0}`, 'dias seguidos'), stat(`${done}/${lessons.length}`, 'aulas concluídas'), stat(mods.filter(x => x.module.badge).length, 'selos de módulo'), stat(best >= 0 ? best + '%' : '—', 'melhor teste final'));
      const badges = card();
      badges.append(make('strong', 'Selos de módulo'), make('div', 'Conclua todas as aulas e passe no teste final para ganhar o selo.', 'ac-muted', 'margin-top:2px'));
      const grid = make('div', null, 'ac-badges');
      cat.tracks.forEach(t => t.levels.forEach(l => {
        if (!l.module) return;
        const m = l.module, earned = !!m.badge;
        const b = make('div', null, 'ac-medal' + (earned ? ' got' : ''));
        b.style.setProperty('--lc', LEVEL_COLORS[l.level] || 'var(--border)');
        b.append(make('div', earned ? '🏅' : '🔒', null, 'font-size:30px'), make('div', m.soon ? 'Em breve' : (t.name + ' · ' + l.level), null, 'font-size:12px;font-weight:700'), make('div', m.title, 'ac-muted', 'font-size:11px;margin-top:2px'));
        grid.append(b);
      }));
      badges.append(grid);
      const tests = card();
      tests.append(make('strong', 'Notas dos testes finais'));
      const withTest = mods.filter(x => x.module.hasFinal);
      if (!withTest.length) tests.append(make('div', 'Ainda não há testes finais.', 'ac-muted', 'margin-top:4px'));
      withTest.forEach(x => {
        const b = x.module.finalBest;
        const row = make('div', null, null, 'margin-top:8px');
        row.append(make('div', `${x.track} · ${x.level}: ${b >= 0 ? b + '%' : 'ainda não feito'}`, null, 'font-size:14px'), bar(Math.max(0, b), b >= 70 ? 'var(--ok)' : null, 'Nota do teste final'));
        tests.append(row);
      });
      tabScreen('Conquistas', stats, badges, tests);
    };

    // ---------- Módulo: aulas e teste final ----------
    const moduleScreen = m => {
      ui.last = null; saveUi();
      const fresh = findModule(m.id) || m;
      const list = make('div');
      fresh.lessons.forEach((l, i) => {
        const c = make('button', null, 'ac-card click ac-lesson-item');
        c.type = 'button';
        c.append(make('span', l.done ? '✓' : String(i + 1), 'ac-num' + (l.done ? ' done' : '')), make('span', l.title, null, 'flex:1'));
        if (l.done) c.setAttribute('aria-label', l.title + ' (concluída)');
        c.addEventListener('click', () => lessonScreen(l.id, fresh));
        list.append(c);
      });
      if (fresh.hasFinal) {
        const best = fresh.finalBest >= 0 ? ` (melhor nota: ${fresh.finalBest}%)` : '';
        const c = make('button', null, 'ac-card tint click');
        c.type = 'button'; c.style.cssText = 'display:block;width:100%;text-align:left';
        c.append(make('strong', 'Teste final do módulo' + best), make('div', 'Perguntas de todas as aulas, com leitura de programa. Dá para fazer sem passar por todas as aulas.', 'ac-muted'));
        c.addEventListener('click', () => finalScreen(fresh));
        list.append(c);
      }
      screen(header(fresh.title, home, fresh.draft), list);
    };

    // ---------- Quiz (aula e teste final) ----------
    const quizBlock = (quizId, quiz, passPercent, onResult) => {
      const box = make('div');
      const answers = quiz.map(() => -1);
      const optionButtons = quiz.map(() => []);
      const pickedRow = quiz.map(() => null);
      const explains = [];
      let graded = false;
      // depois de responder, só a escolhida fica visível (com "trocar"); ao corrigir, todas voltam com cores
      const refresh = qi => {
        const picked = answers[qi], collapsed = picked >= 0 && !graded;
        optionButtons[qi].forEach((b, oi) => { b.style.display = collapsed && oi !== picked ? 'none' : 'block'; b.classList.toggle('picked', oi === picked && !graded); b.setAttribute('aria-pressed', String(oi === picked)); });
        pickedRow[qi].style.display = collapsed ? 'block' : 'none';
      };
      quiz.forEach((q, qi) => {
        const c = card();
        c.setAttribute('role', 'group');
        const prompt = make('div', `${qi + 1}. ${q.prompt}`, 'ac-q');
        prompt.id = `${quizId}-q${qi}`;
        c.setAttribute('aria-labelledby', prompt.id);
        c.append(prompt);
        q.options.forEach((opt, oi) => {
          const b = make('button', `${'ABCD'[oi]}) ${opt}`, 'ac-opt');
          b.type = 'button';
          b.addEventListener('click', () => {
            if (graded) return;
            answers[qi] = oi;
            refresh(qi);
          });
          optionButtons[qi].push(b);
          c.append(b);
        });
        const again = make('button', '↺ Trocar resposta', 'ac-link', 'display:none');
        again.type = 'button';
        again.addEventListener('click', () => { answers[qi] = -1; refresh(qi); });
        pickedRow[qi] = again;
        c.append(again);
        if (q.hint) {
          const h = rich('div', 'Dica: ' + q.hint, 'ac-muted');
          h.style.display = 'none';
          const show = make('button', 'Ver dica', 'ac-link');
          show.type = 'button'; show.addEventListener('click', () => { h.style.display = 'block'; show.remove(); });
          c.append(show, h);
        }
        const ex = make('div', '', 'ac-explain');
        ex.style.display = 'none';
        ex.setAttribute('aria-live', 'polite');
        explains.push(ex);
        c.append(ex);
        box.append(c);
      });
      const result = make('div', '', 'ac-result');
      result.setAttribute('aria-live', 'polite');
      const grade = btn('Corrigir', 'primary', () => {
        if (graded) return;
        if (answers.some(a => a < 0) && !confirm('Há perguntas em branco. Corrigir assim mesmo?')) return;
        const r = ask('grade', { quizId, ids: quiz.map(q => q.id), answers });
        if (!r.ok || !r.results) { result.textContent = 'Não foi possível corrigir.'; return; }
        graded = true;
        saveProgress(r.progress);
        // a resposta certa e a explicação só chegam do Core depois da correção (grade.results)
        quiz.forEach((q, qi) => {
          const res = r.results[qi];
          pickedRow[qi].style.display = 'none';
          optionButtons[qi].forEach((b, oi) => {
            b.style.display = 'block';
            b.classList.remove('picked');
            if (oi === res.right) b.classList.add('right');
            else if (oi === answers[qi]) b.classList.add('wrong');
          });
          explains[qi].style.display = 'block';
          explains[qi].textContent = (res.ok ? '✓ Correto. ' : '✗ Resposta certa: ' + 'ABCD'[res.right] + '. ') + res.explanation;
        });
        result.textContent = `${r.correct} de ${r.total} (${r.percent}%) — ${r.passed ? 'aprovado!' : 'precisa de ' + passPercent + '% para passar. Revise e tente de novo.'}`;
        result.className = 'ac-result ' + (r.passed ? 'ac-good' : 'ac-badt');
        grade.remove();
        onResult(r);
      });
      grade.style.width = '100%';
      box.append(result, grade);
      return box;
    };

    // ---------- Leitura linha a linha com mini 3D (desenho em aramado, sem biblioteca) ----------
    const walkViewer = walk => {
      const box = make('div');
      const canvas = make('canvas', null, 'ac-canvas', 'height:250px');
      canvas.setAttribute('role', 'img');
      canvas.setAttribute('aria-label', 'Desenho do percurso da ferramenta');
      const ctx = canvas.getContext('2d');
      const lines = walk.lines;
      const [x0, y0, z0] = walk.min, [x1, y1, z1] = walk.max;
      // enquadramento: bruto, origem e todo o percurso
      let lo = [Math.min(x0, 0), Math.min(y0, 0), Math.min(z0, 0)], hi = [Math.max(x1, 0), Math.max(y1, 0), Math.max(z1, 0)];
      lines.forEach(l => l.path.forEach(p => { for (let i = 0; i < 3; i++) { lo[i] = Math.min(lo[i], p[i]); hi[i] = Math.max(hi[i], p[i]); } }));
      const center = [0, 1, 2].map(i => (lo[i] + hi[i]) / 2);
      const radius = Math.max(1, Math.hypot(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]) / 2);
      let az = -0.7, el = 0.55, sel = -1, t = 1, playing = false, timer = 0, raf = 0;

      const project = (p, w, h) => {
        const x = p[0] - center[0], y = p[1] - center[1], z = p[2] - center[2];
        const ca = Math.cos(az), sa = Math.sin(az), ce = Math.cos(el), se = Math.sin(el);
        const rx = x * ca - y * sa, ry = x * sa + y * ca;
        const s = Math.min(w, h) / (radius * 2.5);
        return [w / 2 + rx * s, h / 2 - (z * ce + ry * se) * s];
      };
      const seg = (a, b, color, width, dash) => {
        const w = canvas.width, h = canvas.height;
        const pa = project(a, w, h), pb = project(b, w, h);
        ctx.strokeStyle = color; ctx.lineWidth = width; ctx.setLineDash(dash || []);
        ctx.beginPath(); ctx.moveTo(pa[0], pa[1]); ctx.lineTo(pb[0], pb[1]); ctx.stroke();
      };
      const polyline = (pts, upTo, color, width, dash) => {
        // desenha só a fração "upTo" (0..1) do comprimento da polilinha
        let total = 0; const len = [];
        for (let i = 1; i < pts.length; i++) { const d = Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1], pts[i][2] - pts[i - 1][2]); len.push(d); total += d; }
        let left = total * upTo, tip = pts[0];
        for (let i = 1; i < pts.length && left > 0; i++) {
          const d = len[i - 1], f = d === 0 ? 1 : Math.min(1, left / d);
          const end = [0, 1, 2].map(k => pts[i - 1][k] + (pts[i][k] - pts[i - 1][k]) * f);
          seg(pts[i - 1], end, color, width, dash); tip = end; left -= d;
        }
        return tip;
      };

      const draw = () => {
        const dpr = window.devicePixelRatio || 1;
        const cw = Math.round(canvas.clientWidth * dpr), ch = Math.round(canvas.clientHeight * dpr);
        if (!cw || !ch) return; // aba escondida
        if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.lineCap = 'round';
        const k = dpr;
        // bruto
        const c = [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]];
        [[0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7]].forEach(([a, b]) => seg(c[a], c[b], '#5b6b7b', 1 * k));
        // eixos na origem (zero da peça)
        const L = radius * 0.6;
        [['X', [L, 0, 0], '#ff6b6b'], ['Y', [0, L, 0], '#6bdc8c'], ['Z', [0, 0, L], '#6bb0ff']].forEach(([name, end, color]) => {
          seg([0, 0, 0], end, color, 2 * k);
          const pe = project(end, canvas.width, canvas.height);
          ctx.fillStyle = color; ctx.font = `${12 * k}px system-ui`; ctx.fillText(name, pe[0] + 3 * k, pe[1] - 3 * k);
        });
        // percurso: linhas anteriores fracas, linha atual em destaque
        let tool = null;
        lines.forEach((l, i) => {
          if (l.path.length < 2 || i > sel) return;
          const current = i === sel;
          const color = l.rapid ? (current ? '#7cc4ff' : '#3d5d7a') : (current ? '#ffb454' : '#8a6a3a');
          tool = polyline(l.path, current ? t : 1, color, (current ? 3 : 2) * k, l.rapid ? [6 * k, 5 * k] : []);
        });
        if (tool) {
          const p0 = project(tool, canvas.width, canvas.height), p1 = project([tool[0], tool[1], tool[2] + radius * 0.35], canvas.width, canvas.height);
          ctx.strokeStyle = '#e6edf3'; ctx.lineWidth = 3 * k; ctx.setLineDash([]);
          ctx.beginPath(); ctx.moveTo(p0[0], p0[1]); ctx.lineTo(p1[0], p1[1]); ctx.stroke();
          ctx.fillStyle = '#ff5c5c'; ctx.beginPath(); ctx.arc(p0[0], p0[1], 4 * k, 0, 7); ctx.fill();
        }
        ctx.fillStyle = '#9baab9'; ctx.font = `${11 * k}px system-ui`;
        ctx.fillText('azul = rápido (G00) · laranja = corte (G01)', 8 * k, canvas.height - 8 * k);
      };

      // girar com o dedo ou o mouse
      let drag = null, rotate = false;
      canvas.addEventListener('pointerdown', e => { if (!rotate) return; drag = [e.clientX, e.clientY]; canvas.setPointerCapture(e.pointerId); });
      canvas.addEventListener('pointermove', e => {
        if (!drag || !rotate) return;
        az += (e.clientX - drag[0]) * 0.01; el = Math.max(-1.4, Math.min(1.4, el + (e.clientY - drag[1]) * 0.01));
        drag = [e.clientX, e.clientY]; draw();
      });
      canvas.addEventListener('pointerup', () => { drag = null; });

      // lista de linhas do programa e a explicação
      const listBox = make('div', null, 'ac-code ac-walk-list');
      const rows = lines.map((l, i) => {
        const r = codeLine(l.text);
        r.addEventListener('click', () => { stopPlay(); select(i, 1); });
        listBox.append(r); return r;
      });
      const explain = make('div', 'Toque numa linha do programa para ver o que ela faz e por onde a ferramenta anda.', 'ac-explain-line');
      explain.setAttribute('aria-live', 'polite');
      const select = (i, frac) => {
        sel = Math.max(-1, Math.min(lines.length - 1, i)); t = frac;
        rows.forEach((r, j) => r.classList.toggle('sel', j === sel));
        if (sel >= 0) {
          explain.textContent = `Linha ${lines[sel].n}: ${lines[sel].explain}`;
          const r = rows[sel], top = r.offsetTop - listBox.offsetTop;
          if (top < listBox.scrollTop || top + r.offsetHeight > listBox.scrollTop + listBox.clientHeight) listBox.scrollTop = top - 4;
        }
        draw();
      };

      const stopPlay = () => { playing = false; clearTimeout(timer); cancelAnimationFrame(raf); playBtn.textContent = '▶ Reproduzir'; };
      const animate = onDone => {
        if (reduceMotion()) { t = 1; draw(); onDone(); return; }
        const start = performance.now(), dur = lines[sel].path.length >= 2 ? 650 : 450;
        const step = now => {
          t = Math.min(1, (now - start) / dur); draw();
          if (t < 1 && playing) raf = requestAnimationFrame(step); else onDone();
        };
        raf = requestAnimationFrame(step);
      };
      const next = () => {
        if (!playing) return;
        if (sel + 1 >= lines.length) { stopPlay(); return; }
        select(sel + 1, 0);
        animate(() => { timer = setTimeout(next, reduceMotion() ? 700 : 250); });
      };
      const playBtn = btn('▶ Reproduzir', 'primary', () => {
        if (playing) { stopPlay(); return; }
        if (sel + 1 >= lines.length) select(-1, 1);
        playing = true; playBtn.textContent = '❚❚ Pausar'; next();
      });
      playBtn.style.flex = '2';
      const controls = make('div', null, 'ac-row', 'margin-top:8px;flex-wrap:nowrap');
      const prevL = btn('‹ Linha', '', () => { stopPlay(); select(sel - 1, 1); });
      const nextL = btn('Linha ›', '', () => { stopPlay(); select(sel + 1, 1); });
      prevL.style.minWidth = nextL.style.minWidth = '0';
      prevL.setAttribute('aria-label', 'Linha anterior'); nextL.setAttribute('aria-label', 'Próxima linha');
      controls.append(prevL, playBtn, nextL);
      const rotBtn = btn('↻ Girar', '', () => {
        rotate = !rotate;
        rotBtn.setAttribute('aria-pressed', String(rotate));
        rotBtn.textContent = rotate ? '↻ Girando: arraste' : '↻ Girar';
        canvas.style.touchAction = rotate ? 'none' : 'auto';
      });
      rotBtn.setAttribute('aria-pressed', 'false');
      const rotRow = make('div', null, 'ac-row', 'margin-top:8px');
      rotRow.append(rotBtn);

      box.append(canvas, controls, rotRow, listBox, explain);
      new ResizeObserver(draw).observe(canvas);
      setTimeout(draw, 0);
      box.stop = stopPlay;
      stops.push(stopPlay);
      return box;
    };

    // ---------- Texto em cartões curtos, com pausa rápida no meio ----------
    const cardReader = (text, pause, opening) => {
      const pars = paragraphs(text);
      const steps = [];
      if (opening) steps.push({ kind: 'opening', text: opening });
      pars.forEach(s => steps.push({ kind: 'text', ...parseKind(s) }));
      if (pause && pars.length >= 4) steps.splice(Math.ceil(steps.length / 2), 0, { kind: 'pause', q: pause });
      const box = card('ac-reader');
      const body = make('div', null, 'ac-reader-body');
      body.setAttribute('aria-live', 'polite');
      const dots = make('div', null, 'ac-dots');
      dots.setAttribute('aria-hidden', 'true');
      const nav = make('div', null, 'ac-row', 'flex-wrap:nowrap');
      let i = 0;
      const prev = btn('‹ Anterior', '', () => { i = Math.max(0, i - 1); show(); });
      const nextBtn = btn('Próximo ›', 'primary', () => { i = Math.min(steps.length - 1, i + 1); show(); });
      nextBtn.style.flex = '2';
      nav.append(prev, nextBtn);
      const counter = make('div', '', 'ac-muted', 'text-align:right;font-size:12px');
      const show = () => {
        const s = steps[i];
        body.replaceChildren();
        if (s.kind === 'opening') {
          box.style.setProperty('--kind', '#a371f7');
          body.append(make('div', '❓ Para começar', 'ac-kind'), make('p', s.text, null, 'font-size:18px;font-weight:600'));
        } else if (s.kind === 'text') {
          const k = KINDS[s.tag] || KINDS.conceito;
          box.style.setProperty('--kind', k.color);
          body.append(make('div', `${k.icon} ${k.label}`, 'ac-kind'), rich('p', s.text));
        } else {
          box.style.setProperty('--kind', 'var(--accent)');
          body.append(make('div', '⏸ Pausa rápida (não vale nota)', 'ac-kind'));
          body.append(make('div', s.q.prompt, null, 'margin:6px 0 8px;white-space:pre-wrap'));
          const fb = make('div', '', null, 'font-size:13px;margin-top:6px;white-space:pre-wrap');
          const bs = s.q.options.map((o, oi) => {
            const b = make('button', `${'ABCD'[oi]}) ${o}`, 'ac-opt');
            b.type = 'button';
            b.addEventListener('click', () => {
              // correção pelo Core: o quiz chega sem a resposta certa
              const a = ask('answer', { quizId: s.q.quizId, qid: s.q.id, choice: oi });
              if (!a.ok) { fb.textContent = 'Não foi possível corrigir.'; return; }
              bs.forEach((x, xi) => { x.disabled = true; if (xi === a.right) x.classList.add('right'); else if (xi === oi) x.classList.add('wrong'); });
              fb.textContent = (a.correct ? '✓ Correto. ' : '✗ Resposta certa: ' + 'ABCD'[a.right] + '. ') + a.explanation;
            });
            body.append(b); return b;
          });
          body.append(fb);
        }
        dots.replaceChildren(...steps.map((_, k) => make('span', null, k === i ? 'now' : k < i ? 'past' : '')));
        prev.disabled = i === 0;
        nextBtn.disabled = i === steps.length - 1;
        nextBtn.textContent = i === steps.length - 1 ? 'Fim do texto ✓' : 'Próximo ›';
        counter.textContent = `${i + 1} de ${steps.length}`;
      };
      box.append(counter, body, dots, nav);
      show();
      return box;
    };

    // ---------- Aramado 3D simples (para o diagrama dos eixos) ----------
    const wire3d = (stock, height) => {
      const canvas = make('canvas', null, 'ac-canvas', `height:${height}px`);
      const ctx = canvas.getContext('2d');
      const [x0, y0, z0] = stock.min, [x1, y1, z1] = stock.max;
      const center = [(x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1 + 20) / 2];
      const radius = Math.hypot(x1 - x0, y1 - y0, z1 - z0 + 40) / 2;
      let az = -0.7, el = 0.55, state = { tool: [0, 0, 0] };
      const project = p => {
        const x = p[0] - center[0], y = p[1] - center[1], z = p[2] - center[2];
        const ca = Math.cos(az), sa = Math.sin(az), ce = Math.cos(el), se = Math.sin(el);
        const s = Math.min(canvas.width, canvas.height) / (radius * 2.4);
        return [canvas.width / 2 + (x * ca - y * sa) * s, canvas.height / 2 - (z * ce + (x * sa + y * ca) * se) * s];
      };
      const seg = (a, b, color, w, dash) => { const pa = project(a), pb = project(b); ctx.strokeStyle = color; ctx.lineWidth = w; ctx.setLineDash(dash || []); ctx.beginPath(); ctx.moveTo(pa[0], pa[1]); ctx.lineTo(pb[0], pb[1]); ctx.stroke(); };
      const draw = () => {
        const dpr = window.devicePixelRatio || 1;
        const cw = Math.round(canvas.clientWidth * dpr), ch = Math.round(canvas.clientHeight * dpr);
        if (!cw || !ch) return;
        if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
        ctx.clearRect(0, 0, canvas.width, canvas.height); ctx.lineCap = 'round';
        const k = dpr;
        const c = [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0], [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]];
        [[0, 1], [1, 2], [2, 3], [3, 0], [4, 5], [5, 6], [6, 7], [7, 4], [0, 4], [1, 5], [2, 6], [3, 7]].forEach(([a, b]) => seg(c[a], c[b], '#5b6b7b', k));
        [['X', [60, 0, 0], '#ff6b6b'], ['Y', [0, 60, 0], '#6bdc8c'], ['Z', [0, 0, 40], '#6bb0ff']].forEach(([n, e, col]) => {
          seg([0, 0, 0], e, col, 2 * k); const pe = project(e); ctx.fillStyle = col; ctx.font = `${12 * k}px system-ui`; ctx.fillText(n, pe[0] + 3 * k, pe[1] - 3 * k);
        });
        const [tx, ty, tz] = state.tool;
        seg([tx, ty, tz], [tx, ty, 0], '#9baab9', k, [3 * k, 3 * k]);
        seg([tx, ty, 0], [tx, 0, 0], '#ff6b6b', k, [2 * k, 3 * k]);
        seg([tx, ty, 0], [0, ty, 0], '#6bdc8c', k, [2 * k, 3 * k]);
        const p0 = project([tx, ty, tz]), p1 = project([tx, ty, tz + 14]);
        ctx.setLineDash([]); ctx.strokeStyle = '#e6edf3'; ctx.lineWidth = 3 * k; ctx.beginPath(); ctx.moveTo(p0[0], p0[1]); ctx.lineTo(p1[0], p1[1]); ctx.stroke();
        ctx.fillStyle = '#ff5c5c'; ctx.beginPath(); ctx.arc(p0[0], p0[1], 4 * k, 0, 7); ctx.fill();
      };
      new ResizeObserver(draw).observe(canvas);
      return { canvas, set: tool => { state = { tool }; draw(); } };
    };

    const slider = (label, min, max, value, onChange) => {
      const row = make('label', null, 'ac-slider');
      const input = make('input');
      input.type = 'range'; input.min = min; input.max = max; input.value = value;
      const out = make('span', String(value), null, 'text-align:right;font:13px ui-monospace,Consolas,monospace');
      input.addEventListener('input', () => { out.textContent = input.value; onChange(+input.value); });
      row.append(make('span', label, null, 'font-size:14px'), input, out);
      return row;
    };

    // Diagrama 1: eixos (move a ferramenta com os controles e vê X, Y e Z)
    const diagramEixos = () => {
      const box = make('div');
      box.append(make('strong', 'Eixos X, Y e Z'), make('div', 'Mexa nos controles: a ferramenta anda nos três eixos a partir do zero da peça (canto do bloco, Z0 no topo). Arraste o desenho para girar.', 'ac-muted', 'margin:2px 0 8px'));
      const w = wire3d({ min: [0, 0, -20], max: [80, 60, 0] }, 230);
      const pos = [20, 20, 15];
      const read = make('div', '', null, 'font:14px ui-monospace,Consolas,monospace;margin:8px 0');
      const upd = () => { w.set(pos); read.textContent = `X${pos[0]} Y${pos[1]} Z${pos[2]}   ${pos[2] > 0 ? '(acima do topo: seguro)' : pos[2] === 0 ? '(no topo da peça)' : '(dentro do bloco!)'}`; };
      box.append(w.canvas, slider('X (esq→dir)', 0, 80, pos[0], v => { pos[0] = v; upd(); }), slider('Y (frente→fundo)', 0, 60, pos[1], v => { pos[1] = v; upd(); }), slider('Z (baixo→cima)', -20, 40, pos[2], v => { pos[2] = v; upd(); }), read);
      setTimeout(upd, 0);
      return box;
    };

    // Vista de cima simples para os diagramas 2 e 3
    const plan2d = (worldW, worldH, height) => {
      const canvas = make('canvas', null, 'ac-canvas', `height:${height}px`);
      const ctx = canvas.getContext('2d');
      const m = 22;
      const api = {
        canvas, ctx, k: 1,
        begin() {
          const dpr = window.devicePixelRatio || 1; api.k = dpr;
          const cw = Math.round(canvas.clientWidth * dpr), ch = Math.round(canvas.clientHeight * dpr);
          if (!cw || !ch) return false;
          if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
          ctx.clearRect(0, 0, cw, ch);
          api.s = Math.min((cw - 2 * m * dpr) / worldW, (ch - 2 * m * dpr) / worldH);
          return true;
        },
        px(x, y) { return [m * api.k + x * api.s, canvas.height - m * api.k - y * api.s]; },
        line(a, b, col, w, dash) { const pa = api.px(...a), pb = api.px(...b); ctx.strokeStyle = col; ctx.lineWidth = w * api.k; ctx.setLineDash((dash || []).map(d => d * api.k)); ctx.beginPath(); ctx.moveTo(...pa); ctx.lineTo(...pb); ctx.stroke(); },
        rect(x, y, w, h, fill, stroke, dash) { const a = api.px(x, y), b = api.px(x + w, y + h); if (fill) { ctx.fillStyle = fill; ctx.fillRect(a[0], b[1], b[0] - a[0], a[1] - b[1]); } ctx.strokeStyle = stroke; ctx.lineWidth = api.k * 1.5; ctx.setLineDash((dash || []).map(d => d * api.k)); ctx.strokeRect(a[0], b[1], b[0] - a[0], a[1] - b[1]); },
        dot(x, y, col, r) { const p = api.px(x, y); ctx.fillStyle = col; ctx.beginPath(); ctx.arc(p[0], p[1], (r || 5) * api.k, 0, 7); ctx.fill(); },
        text(s, x, y, col, size, dx, dy) { const p = api.px(x, y); ctx.fillStyle = col; ctx.font = `${(size || 12) * api.k}px system-ui`; ctx.fillText(s, p[0] + (dx || 0) * api.k, p[1] + (dy || 0) * api.k); },
      };
      return api;
    };

    // Diagrama 2: absoluto (G90) × incremental (G91)
    const diagramPontoXY = () => {
      const box = make('div');
      box.append(make('strong', 'Absoluto (G90) ou incremental (G91)?'), make('div', 'A ferramenta está em X10 Y10. Digite X e Y e escolha o modo: veja para onde ela vai.', 'ac-muted', 'margin:2px 0 8px'));
      const plan = plan2d(90, 70, 220);
      let mode = 'G90', X = 40, Y = 10;
      const modes = make('div', null, 'ac-row', 'margin:8px 0');
      const inputs = make('div', null, null, 'display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:8px');
      const field = (name, val, set) => { const i = make('input', null, 'ac-field'); i.type = 'text'; i.inputMode = 'decimal'; i.value = String(val); i.addEventListener('input', () => { const n = Number(i.value.replace(',', '.')); if (!isNaN(n) && i.value.trim() !== '') { set(n); upd(); } }); const l = make('label', null, null, 'display:grid;gap:4px;font-size:13px'); l.append(make('span', name), i); return l; };
      const read = make('div', '', null, 'font-size:14px;margin-top:8px;white-space:pre-wrap');
      read.setAttribute('aria-live', 'polite');
      const mbtn = {};
      ['G90', 'G91'].forEach(m => { const b = btn(m === 'G90' ? 'G90 absoluto' : 'G91 incremental', '', () => { mode = m; upd(); }); mbtn[m] = b; modes.append(b); });
      inputs.append(field('X', X, v => { X = v; }), field('Y', Y, v => { Y = v; }));
      const upd = () => {
        Object.keys(mbtn).forEach(m => { mbtn[m].className = 'ac-btn' + (m === mode ? ' primary' : ''); mbtn[m].setAttribute('aria-pressed', String(m === mode)); });
        const dx = mode === 'G90' ? X : 10 + X, dy = mode === 'G90' ? Y : 10 + Y;
        read.textContent = mode === 'G90'
          ? `G90 X${X} Y${Y}  →  vai para o ponto X${dx} Y${dy}.\nEm absoluto, X e Y dizem ONDE a ferramenta fica.`
          : `G91 X${X} Y${Y}  →  anda ${X} em X e ${Y} em Y a partir do ponto atual e chega em X${dx} Y${dy}.\nEm incremental, X e Y dizem QUANTO a ferramenta anda.`;
        if (!plan.begin()) return;
        for (let g = 0; g <= 80; g += 10) { plan.line([g, 0], [g, 60], '#1c2733', 1); plan.text(String(g), g, 0, '#6b7a8a', 10, -4, 14); }
        for (let g = 0; g <= 60; g += 10) { plan.line([0, g], [80, g], '#1c2733', 1); if (g) plan.text(String(g), 0, g, '#6b7a8a', 10, -18, 4); }
        plan.rect(0, 0, 80, 60, 'rgba(120,140,160,.10)', '#5b6b7b');
        plan.line([10, 10], [dx, dy], '#ffb454', 2, [6, 4]);
        plan.dot(10, 10, '#7cc4ff', 5); plan.text('início X10 Y10', 10, 10, '#7cc4ff', 11, 8, -8);
        plan.dot(dx, dy, '#ff5c5c', 6); plan.text(`destino X${dx} Y${dy}`, dx, dy, '#ff9a83', 11, 8, -8);
      };
      box.append(plan.canvas, modes, inputs, read);
      new ResizeObserver(upd).observe(plan.canvas);
      setTimeout(upd, 0);
      return box;
    };

    // Diagrama 3: zero máquina, zero peça (G54) e o erro de medição
    const diagramZeros = () => {
      const box = make('div');
      box.append(make('strong', 'Zero máquina, zero peça e G54'), make('div', 'A mesa tem o zero da máquina (canto). O G54 guarda onde está o zero da PEÇA. Mexa no G54 e no erro de medição: a ferramenta vai para X20 Y20 do G54.', 'ac-muted', 'margin:2px 0 8px'));
      const plan = plan2d(210, 150, 240);
      let ox = 60, oy = 40, err = 0;
      const read = make('div', '', null, 'font-size:14px;margin-top:8px;white-space:pre-wrap');
      read.setAttribute('aria-live', 'polite');
      const upd = () => {
        const lands = 20 - err;
        read.textContent = `G54 = X${ox} Y${oy} (valor guardado no controle).\nG00 X20 Y20 leva a ferramenta a X${ox + 20} Y${oy + 20} da máquina.\n` + (err === 0
          ? 'Como o G54 está certo, isso é X20 Y20 da PEÇA.'
          : `Mas a peça está ${err > 0 ? err + ' mm mais à direita' : -err + ' mm mais à esquerda'}: a ferramenta chega a X${lands} da peça, e não X20. Esse é o erro de quem confia só no G54.`);
        if (!plan.begin()) return;
        plan.rect(0, 0, 200, 140, 'rgba(120,140,160,.08)', '#5b6b7b');
        plan.dot(0, 0, '#6bb0ff', 6); plan.text('zero máquina', 0, 0, '#6bb0ff', 11, 8, -8);
        const realX = ox + err;
        plan.rect(realX, oy, 80, 60, 'rgba(255,180,84,.18)', '#ffb454');
        if (err !== 0) plan.rect(ox, oy, 80, 60, null, '#7cc4ff', [5, 4]);
        plan.dot(realX, oy, '#ffb454', 5); plan.text('zero da peça (real)', realX, oy, '#ffb454', 11, 8, 14);
        if (err !== 0) plan.text(`G54 aponta aqui (X${ox} Y${oy})`, ox, oy, '#7cc4ff', 11, -8, -8);
        plan.dot(ox + 20, oy + 20, '#ff5c5c', 6); plan.text('X20 Y20', ox + 20, oy + 20, '#ff9a83', 11, 8, -8);
      };
      box.append(plan.canvas, slider('G54 em X', 0, 120, ox, v => { ox = v; upd(); }), slider('G54 em Y', 0, 80, oy, v => { oy = v; upd(); }), slider('Erro medição', -10, 10, err, v => { err = v; upd(); }), read);
      new ResizeObserver(upd).observe(plan.canvas);
      setTimeout(upd, 0);
      return box;
    };

    const DIAGRAMS = { 'eixos': diagramEixos, 'ponto-xy': diagramPontoXY, 'zeros': diagramZeros };

    // ---------- Exercício no editor (o Core confere) ----------
    const exerciseEditor = (lessonId, ex) => {
      const box = make('div');
      const e = ex.editor;
      if (e.prefix) box.append(make('pre', e.prefix, 'ac-code ac-prefix'));
      const ta = make('textarea', null, 'ac-textarea');
      ta.value = e.initial; ta.spellcheck = false; ta.autocapitalize = 'characters'; ta.rows = Math.max(3, e.initial.split('\n').length + 1);
      ta.setAttribute('aria-label', 'Sua resposta (programa)');
      const out = make('div', '', null, 'font-size:14px;margin:6px 0;white-space:pre-wrap');
      out.setAttribute('aria-live', 'polite');
      const row = make('div', null, 'ac-row');
      const ans = make('div', 'Gabarito: ' + ex.answer, 'ac-good', 'display:none;font-size:14px;margin-top:6px;white-space:pre-wrap');
      const check = btn('Conferir', 'primary', () => {
        const r = ask('check', { id: lessonId, text: ta.value });
        if (!r.ok) { out.textContent = 'Não foi possível conferir.'; out.className = 'ac-badt'; return; }
        out.className = r.passed ? 'ac-good' : 'ac-badt';
        out.textContent = r.passed ? '✓ Certo! O programa faz o que o exercício pede.' : '✗ Ainda não:\n' + r.messages.join('\n');
      });
      check.style.flex = '2';
      row.append(check, btn('Recomeçar', '', () => { ta.value = e.initial; out.textContent = ''; }), btn('Ver gabarito', '', () => { ans.style.display = 'block'; }));
      box.append(ta, row, out, ans);
      return box;
    };

    // ---------- Aula em abas: Visão geral, Trajetória, Sintaxe, Exemplo, Cuidados, Praticar ----------
    const lessonScreen = (id, mod, wantedTab) => {
      const l = ask('lesson', { id, hideAnswers: true });
      if (!l.ok) { screen(header('Aula', () => moduleScreen(mod)), hint('Aula não encontrada.')); return; }
      const fresh = findModule(mod.id) || mod;
      ui.last = { mod: fresh.id, lesson: l.id }; saveUi();
      const idx = fresh.lessons.findIndex(x => x.id === l.id);
      const total = fresh.lessons.length, nDone = fresh.lessons.filter(x => x.done).length;
      const pct = total ? Math.round(100 * nDone / total) : 0;
      const next = idx >= 0 && idx + 1 < total ? fresh.lessons[idx + 1] : null;
      const pars = paragraphs(l.text).map(parseKind);
      const mistake = l.commonMistake && !/^Descrito no texto/i.test(l.commonMistake) ? l.commonMistake : '';
      const walkOk = !!(l.walk && l.walk.lines && l.walk.lines.length);
      const warning = l.warning || 'Simule antes de rodar na máquina.';

      // Conteúdo de cada aba (aba sem conteúdo não aparece)
      const visao = () => {
        const out = [];
        if (l.goal) { const c = card('tint'); c.append(make('div', '🎯 Objetivo da aula', 'ac-label'), make('div', l.goal, null, 'margin-top:4px')); out.push(c); }
        const v = card();
        const a = make('a', null, 'ac-btn', 'display:inline-flex;align-items:center;justify-content:center;text-decoration:none;flex:none');
        a.target = '_blank'; a.rel = 'noopener noreferrer';
        if (l.video && l.video.url) {
          v.append(make('strong', '🎬 Vídeo desta aula'), make('div', l.video.title || '', 'ac-muted', 'margin:2px 0 8px'));
          a.textContent = 'Assistir no YouTube'; a.href = l.video.url;
        } else {
          // enquanto a aula não tem vídeo próprio: leva ao canal do Casillas
          v.append(make('strong', '🎬 Vídeo desta aula: em breve'), make('div', 'Enquanto isso, veja outros vídeos de usinagem no canal Casillas.', 'ac-muted', 'margin:2px 0 8px'));
          a.textContent = 'Abrir canal no YouTube'; a.href = CHANNEL_URL;
        }
        v.append(a); out.push(v);
        // [atenção] e [exemplo] ficam só nas abas Cuidados e Exemplo
        const readerText = paragraphs(l.text).filter(s => !['atenção', 'exemplo'].includes(parseKind(s).tag)).join('\n\n');
        if (readerText) out.push(cardReader(readerText, l.quiz && l.quiz.length ? { ...l.quiz[0], quizId: l.quizId } : null, l.opening));
        (l.diagrams || []).forEach(d => { const f = DIAGRAMS[d]; if (!f) return; const c = card(); c.append(f()); out.push(c); });
        if (l.summary && l.summary.length) {
          const c = card('ac-callout');
          c.style.setProperty('--kind', 'var(--ok)');
          c.append(make('div', '📌 O que levar desta aula', 'ac-kind'));
          l.summary.forEach((it, k) => c.append(rich('p', `${k + 1}. ${it}`, null)));
          [...c.querySelectorAll('p')].forEach(p => { p.style.margin = '0 0 8px'; });
          out.push(c);
        }
        return out;
      };
      const trajetoria = () => {
        const c = card();
        c.append(make('strong', 'Programa ' + l.programName), make('div', 'Veja a ferramenta andar e leia o programa linha a linha.', 'ac-muted', 'margin:2px 0 8px'), walkViewer(l.walk));
        return [c];
      };
      const exemplo = () => {
        const out = pars.filter(p => p.tag === 'exemplo').map(p => callout('exemplo', null, p.text));
        const c = card();
        c.append(make('strong', 'Programa de exemplo: ' + l.programName), make('div', 'Comentários entre parênteses explicam cada parte.', 'ac-muted', 'margin:2px 0 8px'));
        const code = make('div', null, 'ac-code ac-code-lines');
        code.setAttribute('aria-label', 'Programa ' + l.programName);
        code.tabIndex = 0;
        l.program.replace(/\r\n/g, '\n').split('\n').forEach(t => code.append(codeLine(t || ' ')));
        c.append(code, make('div', '⚠ ' + warning, 'ac-warning'));
        const row = make('div', null, 'ac-row');
        const open = view3d => {
          ui.tabs[l.id] = current; ui.last = { mod: fresh.id, lesson: l.id }; saveUi();
          showReturn(l.title, l.programName);
          finish(JSON.stringify({ name: l.programName.toUpperCase(), text: l.program, view3d }));
        };
        row.append(btn('Abrir no editor', 'primary', () => open(false)), btn('Ver em 3D', '', () => open(true)));
        c.append(row);
        out.push(c);
        return out;
      };
      const cuidados = () => {
        const out = [];
        if (mistake) out.push(callout('atenção', 'Erro comum no chão de fábrica', mistake));
        pars.filter(p => p.tag === 'atenção').forEach(p => out.push(callout('atenção', null, p.text)));
        if (l.warning) out.push(callout('dica', 'Aviso', l.warning));
        return out;
      };
      const praticar = () => {
        const out = [];
        if (l.exercise) {
          const c = card();
          c.append(make('strong', 'Exercício'), make('div', l.exercise.prompt, null, 'margin:4px 0 8px;white-space:pre-wrap'));
          if (l.exercise.editor) {
            c.append(exerciseEditor(l.id, l.exercise));
          } else {
            const ans = make('div', 'Gabarito: ' + l.exercise.answer, 'ac-good', 'display:none;font-size:14px;margin-bottom:6px;white-space:pre-wrap');
            const show = btn('Ver gabarito', '', () => { ans.style.display = 'block'; show.remove(); });
            show.style.flex = 'none';
            c.append(ans, show);
          }
          out.push(c);
        }
        const foot = make('div', null, 'ac-row', 'margin:16px 0 24px');
        const nextLabel = next ? 'Próxima aula ›' : (fresh.hasFinal ? 'Teste final ›' : null);
        const goNext = () => next ? lessonScreen(next.id, fresh, 'visao') : finalScreen(fresh);
        const drawFoot = passed => {
          foot.replaceChildren(btn('‹ Voltar ao módulo', '', () => moduleScreen(fresh)));
          if (nextLabel) foot.append(btn(nextLabel, passed ? 'ok' : 'primary', goNext));
        };
        drawFoot(false);
        if (l.quiz && l.quiz.length) {
          out.push(make('h3', 'Quiz da aula', 'ac-section'));
          out.push(quizBlock(l.quizId, l.quiz, 70, r => { if (r.passed) drawFoot(true); }));
        }
        out.push(foot);
        return out;
      };

      // Sintaxe (JSON v2): blocos em monoespaçado e tabela endereço → conceito; sem dado (v1 ou seção vazia), a aba não aparece
      const syn = l.sections || {};
      const synBlocks = syn.syntaxBlocks || [], synParams = syn.syntaxParams || [];
      const sintaxe = () => {
        const out = [];
        if (synBlocks.length) {
          const c = card();
          c.append(make('strong', 'Como se escreve'));
          const code = make('div', null, 'ac-code ac-code-lines', 'margin-top:8px');
          code.setAttribute('aria-label', 'Formato dos blocos');
          code.tabIndex = 0;
          synBlocks.forEach(b => code.append(codeLine(b)));
          c.append(code);
          out.push(c);
        }
        if (synParams.length) {
          const c = card();
          c.append(make('strong', 'O que cada endereço significa'));
          const table = make('table', null, null, 'width:100%;border-collapse:collapse;margin-top:8px;font-size:14px');
          const head = make('tr');
          ['Endereço', 'Conceito'].forEach(h => head.append(make('th', h, null, 'text-align:left;padding:6px 8px;border-bottom:1px solid var(--border)')));
          table.append(head);
          synParams.forEach(p => {
            const tr = make('tr');
            tr.append(make('td', p.address, null, 'padding:6px 8px;font-family:var(--mono, ui-monospace, monospace);font-weight:700;white-space:nowrap;vertical-align:top;border-bottom:1px solid var(--border)'),
              make('td', p.concept, null, 'padding:6px 8px;border-bottom:1px solid var(--border)'));
            table.append(tr);
          });
          c.append(table);
          out.push(c);
        }
        return out;
      };
      const defs = [
        { id: 'visao', icon: '📖', label: 'Visão geral', build: visao, show: true },
        { id: 'trajetoria', icon: '🧭', label: 'Trajetória', build: trajetoria, show: walkOk },
        { id: 'sintaxe', icon: '🔤', label: 'Sintaxe', build: sintaxe, show: synBlocks.length > 0 || synParams.length > 0 },
        { id: 'exemplo', icon: '🧩', label: 'Exemplo', build: exemplo, show: !!l.program },
        { id: 'cuidados', icon: '⚠️', label: 'Cuidados', build: cuidados, show: !!(mistake || l.warning || pars.some(p => p.tag === 'atenção')) },
        { id: 'praticar', icon: '🎯', label: 'Praticar', build: praticar, show: true },
      ].filter(d => d.show);

      const head = make('div', null, 'ac-lesson-head');
      const top = header(l.title, () => moduleScreen(fresh), l.draft);
      const prog = make('div', null, 'ac-progress');
      prog.append(make('span', idx >= 0 ? `Aula ${idx + 1} de ${total}` : fresh.title), bar(pct, null, 'Aulas concluídas no módulo'), make('span', pct + '%'));
      const tablist = make('div', null, 'ac-tabs');
      tablist.setAttribute('role', 'tablist');
      tablist.setAttribute('aria-label', 'Partes da aula');
      head.append(top, prog, tablist);

      let current = null;
      const tabs = {}, panels = {};
      const body = make('div');
      defs.forEach((d, i) => {
        const t = make('button', null, 'ac-tab');
        t.type = 'button'; t.id = `ac-tab-${d.id}`;
        t.setAttribute('role', 'tab'); t.setAttribute('aria-controls', `ac-panel-${d.id}`);
        const ic = make('span', d.icon, 'ic'); ic.setAttribute('aria-hidden', 'true');
        t.append(ic, make('span', d.label));
        t.addEventListener('click', () => select(d.id, false));
        tablist.append(t);
        const p = make('div', null, 'ac-panel');
        p.id = `ac-panel-${d.id}`; p.hidden = true;
        p.setAttribute('role', 'tabpanel'); p.setAttribute('aria-labelledby', t.id); p.tabIndex = 0;
        p.append(...d.build());
        const nd = defs[i + 1];
        if (nd) { const nb = btn(`Continuar: ${nd.label} ›`, 'primary', () => select(nd.id, true), 'ac-next-tab'); nb.style.width = '100%'; p.append(nb); }
        tabs[d.id] = t; panels[d.id] = p;
        body.append(p);
      });
      tablist.addEventListener('keydown', e => {
        const ids = defs.map(d => d.id), at = ids.indexOf(current);
        let to = null;
        if (e.key === 'ArrowRight') to = ids[(at + 1) % ids.length];
        else if (e.key === 'ArrowLeft') to = ids[(at - 1 + ids.length) % ids.length];
        else if (e.key === 'Home') to = ids[0];
        else if (e.key === 'End') to = ids[ids.length - 1];
        if (to) { e.preventDefault(); select(to, true); }
      });
      const select = (tabId, focus) => {
        if (!panels[tabId]) tabId = defs[0].id;
        if (current && current !== tabId) panels[current].querySelectorAll('*').forEach(n => { if (n.stop) n.stop(); });
        current = tabId;
        defs.forEach(d => {
          const on = d.id === tabId;
          tabs[d.id].setAttribute('aria-selected', String(on));
          tabs[d.id].tabIndex = on ? 0 : -1;
          panels[d.id].hidden = !on;
        });
        ui.tabs[l.id] = tabId; saveUi();
        root.scrollTop = 0;
        tabs[tabId].scrollIntoView({ block: 'nearest', inline: 'nearest' });
        if (focus) tabs[tabId].focus();
      };
      screen(head, body);
      select(wantedTab || ui.tabs[l.id] || defs[0].id, false);
    };

    // ---------- Teste final ----------
    const finalScreen = mod => {
      ui.last = null; saveUi();
      const t = ask('final', { module: mod.id, seed: Math.floor(Math.random() * 1e6), hideAnswers: true });
      if (!t.ok) { screen(header('Teste final', () => moduleScreen(mod)), hint('Teste não encontrado.')); return; }
      const intro = hint(`Nota mínima: ${t.passPercent}%. ${t.canSkip ? 'Quem já conhece o assunto pode fazer este teste direto, sem passar pelas aulas.' : ''}`);
      const foot = make('div', null, 'ac-row', 'margin:16px 0 24px');
      foot.append(btn('‹ Voltar ao módulo', '', () => moduleScreen(mod)), btn('Refazer teste', 'primary', () => finalScreen(mod)));
      screen(header('Teste final: ' + mod.title, () => moduleScreen(mod), t.draft), intro, quizBlock(t.quizId, t.quiz, t.passPercent, () => {}), foot);
    };

    // Reabrir: volta à última aula (e aba) que estava aberta; senão, o Início
    const start = async () => {
      const last = ui.last;
      const mod = last && last.lesson ? findModule(last.mod) : null;
      if (!mod || mod.soon || !mod.lessons || !mod.lessons.some(x => x.id === last.lesson)) { home(); return; }
      if (OPEN_MODULES.has(mod.id)) { lessonScreen(last.lesson, mod); return; }
      home();
      if (await ensureAccess('academy')) lessonScreen(last.lesson, mod);
    };
    start();
  }));
}
