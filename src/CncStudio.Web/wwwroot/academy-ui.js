// Casillas Code: Academy (cursos). Tela em HTML; conteúdo, correção e progresso vêm do Core (C#).
// call(comando, argsJson) -> JSON. Comandos: catalog | lesson | final | grade | done.
// O progresso fica no aparelho (localStorage, opcional). Resolve com JSON {name, text, view3d} ou null.
const PROGRESS_KEY = 'cc.academy.progress';

export function openAcademy(call) {
  return new Promise(resolve => {
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

    const css = (el, text) => { el.style.cssText = text; return el; };
    const make = (tag, text, style) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (style) css(el, style); return el; };
    const btn = (label, bg, onClick, grow = 1) => {
      const b = make('button', label, `flex:${grow};min-width:110px;min-height:46px;border:1px solid #4a5665;border-radius:8px;background:${bg};color:white;font-weight:700;font-size:15px`);
      b.type = 'button'; b.addEventListener('click', onClick); return b;
    };
    const badge = text => make('span', text, 'display:inline-block;background:#3b2f12;color:#f3c969;border:1px solid #7a6122;border-radius:999px;font-size:11px;font-weight:700;padding:2px 9px;margin-left:8px;vertical-align:middle');

    const root = make('div', null, 'position:fixed;inset:0;z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px/1.5 system-ui,sans-serif;padding:14px 14px 70px;box-sizing:border-box');
    const wrap = make('div', null, 'max-width:680px;margin:0 auto');
    root.append(wrap);
    document.body.append(root);

    const finish = value => { root.remove(); resolve(value); };
    const screen = (...nodes) => { wrap.replaceChildren(...nodes); root.scrollTop = 0; };
    const header = (title, onBack, isDraft) => {
      const bar = make('div', null, 'display:flex;align-items:center;gap:10px;margin-bottom:10px');
      const back = btn(onBack ? '‹ Voltar' : 'Fechar', '#30363d', onBack || (() => finish(null)), 0);
      back.style.minWidth = '88px';
      const h = make('h2', title, 'margin:0;font-size:19px;flex:1');
      if (isDraft) h.append(badge('Rascunho'));
      bar.append(back, h);
      return bar;
    };
    const card = style => make('div', null, 'border:1px solid #303d4b;border-radius:10px;padding:12px;margin-bottom:10px;' + (style || ''));
    const hint = text => make('p', text, 'color:#9baab9;font-size:13px;margin:0 0 12px');

    // ---------- Início: trilhas e níveis ----------
    let activeTrack = 'Comum';
    const home = () => {
      const cat = ask('catalog', {});
      if (!cat.tracks) { screen(header('Academy'), hint('Não foi possível carregar os cursos.' + (cat.error ? ' (' + cat.error + ')' : ''))); return; }
      const tabs = make('div', null, 'display:flex;gap:8px;margin-bottom:12px');
      cat.tracks.forEach(t => {
        const b = btn(t.name, t.name === activeTrack ? '#b45b1f' : '#21262d', () => { activeTrack = t.name; home(); });
        tabs.append(b);
      });
      const list = make('div');
      const track = cat.tracks.find(t => t.name === activeTrack) || cat.tracks[0];
      track.levels.forEach(l => {
        const m = l.module;
        const available = m && !m.soon;
        const c = card(available ? 'cursor:pointer;background:#131a22' : 'opacity:.55');
        const top = make('div', null, 'display:flex;justify-content:space-between;gap:8px;align-items:baseline');
        top.append(make('strong', l.level, 'font-size:16px'));
        if (available) {
          const done = m.lessons.filter(x => x.done).length;
          top.append(make('span', `${done}/${m.lessons.length} aulas`, 'color:#9baab9;font-size:13px'));
        } else {
          top.append(make('span', 'Em breve', 'color:#9baab9;font-size:13px'));
        }
        c.append(top);
        if (m) c.append(make('div', m.title, 'color:#c9d6e2;font-size:14px;margin-top:2px'));
        if (available) {
          if (m.draft) c.append(make('div', 'Conteúdo em rascunho: ainda não revisado.', 'color:#f3c969;font-size:12px;margin-top:4px'));
          c.addEventListener('click', () => moduleScreen(m));
        }
        list.append(c);
      });
      screen(header('Academy'), hint('Cursos de CNC em português, do básico ao avançado. Conteúdo em rascunho até a revisão.'), tabs, list);
    };

    // ---------- Módulo: aulas e teste final ----------
    const moduleScreen = m => {
      const fresh = ask('catalog', {}).tracks.flatMap(t => t.levels).map(l => l.module).find(x => x && x.id === m.id) || m;
      const list = make('div');
      fresh.lessons.forEach((l, i) => {
        const c = card('cursor:pointer;background:#131a22;display:flex;gap:10px;align-items:center');
        c.append(make('span', l.done ? '✓' : String(i + 1), `min-width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;background:${l.done ? '#1f6f43' : '#21262d'}`));
        c.append(make('span', l.title, 'flex:1'));
        c.addEventListener('click', () => lessonScreen(l.id, fresh));
        list.append(c);
      });
      if (fresh.hasFinal) {
        const best = fresh.finalBest >= 0 ? ` (melhor nota: ${fresh.finalBest}%)` : '';
        const c = card('cursor:pointer;background:#2a1d10;border-color:#7a4a1f');
        c.append(make('strong', 'Teste final do módulo' + best));
        c.append(make('div', 'Perguntas de todas as aulas, com leitura de programa. Dá para fazer sem passar por todas as aulas.', 'color:#c9d6e2;font-size:13px'));
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
      let graded = false;
      quiz.forEach((q, qi) => {
        const c = card();
        c.append(make('div', `${qi + 1}. ${q.prompt}`, 'font-weight:600;margin-bottom:8px;white-space:pre-wrap'));
        q.options.forEach((opt, oi) => {
          const b = make('button', `${'ABCD'[oi]}) ${opt}`, 'display:block;width:100%;text-align:left;min-height:44px;margin-bottom:6px;border:1px solid #4a5665;border-radius:8px;background:#161b22;color:#e6edf3;font-size:14px;padding:8px 10px;white-space:normal');
          b.type = 'button';
          b.addEventListener('click', () => {
            if (graded) return;
            answers[qi] = oi;
            optionButtons[qi].forEach((x, xi) => { x.style.background = xi === oi ? '#2d4a63' : '#161b22'; });
          });
          optionButtons[qi].push(b);
          c.append(b);
        });
        if (q.hint) {
          const h = make('div', 'Dica: ' + q.hint, 'color:#9baab9;font-size:12px;display:none');
          const show = make('button', 'Ver dica', 'background:none;border:none;color:#7cc4ff;font-size:13px;padding:4px 0;cursor:pointer');
          show.type = 'button'; show.addEventListener('click', () => { h.style.display = 'block'; show.remove(); });
          c.append(show, h);
        }
        c.explain = make('div', '', 'display:none;margin-top:8px;padding:8px;border-radius:8px;background:#0a0f14;font-size:13px;white-space:pre-wrap');
        c.append(c.explain);
        q.card = c;
        box.append(c);
      });
      const result = make('div', '', 'margin:10px 0;font-weight:700;font-size:16px');
      const grade = btn('Corrigir', '#b45b1f', () => {
        if (graded) return;
        if (answers.some(a => a < 0) && !confirm('Há perguntas em branco. Corrigir assim mesmo?')) return;
        const r = ask('grade', { quizId, ids: quiz.map(q => q.id), answers });
        if (!r.ok) { result.textContent = 'Não foi possível corrigir.'; return; }
        graded = true;
        saveProgress(r.progress);
        quiz.forEach((q, qi) => {
          optionButtons[qi].forEach((b, oi) => {
            if (oi === q.correct) b.style.background = '#1f6f43';
            else if (oi === answers[qi]) b.style.background = '#8a2d2d';
          });
          q.card.explain.style.display = 'block';
          q.card.explain.textContent = (answers[qi] === q.correct ? '✓ Correto. ' : '✗ Resposta certa: ' + 'ABCD'[q.correct] + '. ') + q.explanation;
        });
        result.textContent = `${r.correct} de ${r.total} (${r.percent}%) — ${r.passed ? 'aprovado!' : 'precisa de ' + passPercent + '% para passar. Revise e tente de novo.'}`;
        result.style.color = r.passed ? '#6fd39b' : '#ff9a83';
        grade.remove();
        onResult(r);
      }, 1);
      box.append(result, grade);
      return box;
    };


    // ---------- Leitura linha a linha com mini 3D (desenho em aramado, sem biblioteca) ----------
    const walkViewer = walk => {
      const box = make('div');
      const canvas = make('canvas', null, 'width:100%;height:250px;background:#0a0f14;border:1px solid #30445a;border-radius:9px;touch-action:none;display:block');
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
        ctx.fillText('arraste para girar · azul = rápido (G00) · laranja = corte (G01)', 8 * k, canvas.height - 8 * k);
      };

      // girar com o dedo ou o mouse
      let drag = null;
      canvas.addEventListener('pointerdown', e => { drag = [e.clientX, e.clientY]; canvas.setPointerCapture(e.pointerId); });
      canvas.addEventListener('pointermove', e => {
        if (!drag) return;
        az += (e.clientX - drag[0]) * 0.01; el = Math.max(-1.4, Math.min(1.4, el + (e.clientY - drag[1]) * 0.01));
        drag = [e.clientX, e.clientY]; draw();
      });
      canvas.addEventListener('pointerup', () => { drag = null; });

      // lista de linhas do programa e a explicação
      const listBox = make('div', null, 'background:#0a0f14;border:1px solid #30445a;border-radius:9px;margin:8px 0;max-height:210px;overflow:auto');
      const rows = lines.map((l, i) => {
        const r = make('div', l.text, 'padding:5px 10px;font:12px/1.4 ui-monospace,Consolas,monospace;color:#c9d6e2;white-space:pre;cursor:pointer;border-left:3px solid transparent');
        r.addEventListener('click', () => { stopPlay(); select(i, 1); });
        listBox.append(r); return r;
      });
      const explain = make('div', 'Toque numa linha do programa para ver o que ela faz e por onde a ferramenta anda.', 'background:#131a22;border:1px solid #303d4b;border-radius:9px;padding:10px;font-size:14px;margin-bottom:8px');
      const select = (i, frac) => {
        sel = Math.max(-1, Math.min(lines.length - 1, i)); t = frac;
        rows.forEach((r, j) => { r.style.background = j === sel ? '#2d4a63' : 'transparent'; r.style.borderLeftColor = j === sel ? '#ffb454' : 'transparent'; });
        if (sel >= 0) {
          explain.textContent = `Linha ${lines[sel].n}: ${lines[sel].explain}`;
          rows[sel].scrollIntoView({ block: 'nearest' });
        }
        draw();
      };

      const stopPlay = () => { playing = false; clearTimeout(timer); cancelAnimationFrame(raf); playBtn.textContent = '▶ Reproduzir'; };
      const animate = onDone => {
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
        animate(() => { timer = setTimeout(next, 250); });
      };
      const playBtn = btn('▶ Reproduzir', '#b45b1f', () => {
        if (playing) { stopPlay(); return; }
        if (sel + 1 >= lines.length) select(-1, 1);
        playing = true; playBtn.textContent = '❚❚ Pausar'; next();
      }, 2);
      const controls = make('div', null, 'display:flex;gap:8px;margin-top:8px');
      controls.append(btn('‹ Linha', '#30363d', () => { stopPlay(); select(sel - 1, 1); }), playBtn, btn('Linha ›', '#30363d', () => { stopPlay(); select(sel + 1, 1); }));

      box.append(canvas, controls, listBox, explain);
      new ResizeObserver(draw).observe(canvas);
      setTimeout(draw, 0);
      box.stop = stopPlay;
      return box;
    };


    // ---------- Texto em cartões curtos, com pausa rápida no meio ----------
    const cardReader = (text, pause) => {
      const pars = text.split(/\n\s*\n/).filter(s => s.trim());
      const steps = pars.map(s => ({ kind: 'text', text: s }));
      if (pause && pars.length >= 4) steps.splice(Math.ceil(pars.length / 2), 0, { kind: 'pause', q: pause });
      const box = card('background:#131a22');
      const body = make('div', null, 'min-height:120px');
      const dots = make('div', null, 'display:flex;gap:6px;justify-content:center;margin:8px 0');
      const nav = make('div', null, 'display:flex;gap:8px');
      let i = 0;
      const prev = btn('‹ Anterior', '#30363d', () => { i = Math.max(0, i - 1); show(); });
      const nextBtn = btn('Próximo ›', '#b45b1f', () => { i = Math.min(steps.length - 1, i + 1); show(); }, 2);
      nav.append(prev, nextBtn);
      const show = () => {
        const s = steps[i];
        body.replaceChildren();
        if (s.kind === 'text') {
          body.append(make('p', s.text, 'margin:0;font-size:16px;line-height:1.6'));
        } else {
          body.append(make('strong', '⏸ Pausa rápida (não vale nota)', 'color:#f3c969'));
          body.append(make('div', s.q.prompt, 'margin:6px 0 8px;white-space:pre-wrap'));
          const fb = make('div', '', 'font-size:13px;margin-top:6px;white-space:pre-wrap');
          const bs = s.q.options.map((o, oi) => {
            const b = make('button', `${'ABCD'[oi]}) ${o}`, 'display:block;width:100%;text-align:left;min-height:42px;margin-bottom:6px;border:1px solid #4a5665;border-radius:8px;background:#161b22;color:#e6edf3;font-size:14px;padding:8px 10px;white-space:normal');
            b.type = 'button';
            b.addEventListener('click', () => {
              bs.forEach((x, xi) => { x.disabled = true; if (xi === s.q.correct) x.style.background = '#1f6f43'; else if (xi === oi) x.style.background = '#8a2d2d'; });
              fb.textContent = (oi === s.q.correct ? '✓ Correto. ' : '✗ Resposta certa: ' + 'ABCD'[s.q.correct] + '. ') + s.q.explanation;
            });
            body.append(b); return b;
          });
          body.append(fb);
        }
        dots.replaceChildren(...steps.map((_, k) => make('span', null, `width:8px;height:8px;border-radius:50%;background:${k === i ? '#ffb454' : k < i ? '#6b7a8a' : '#2a3441'}`)));
        prev.disabled = i === 0; prev.style.opacity = i === 0 ? '.5' : '1';
        nextBtn.disabled = i === steps.length - 1; nextBtn.style.opacity = i === steps.length - 1 ? '.5' : '1';
        nextBtn.textContent = i === steps.length - 1 ? 'Fim do texto ✓' : 'Próximo ›';
        counter.textContent = `${i + 1} de ${steps.length}`;
      };
      const counter = make('div', '', 'color:#9baab9;font-size:12px;text-align:right');
      box.append(counter, body, dots, nav);
      show();
      return box;
    };

    // ---------- Aramado 3D simples (para o diagrama dos eixos) ----------
    const wire3d = (stock, height) => {
      const canvas = make('canvas', null, `width:100%;height:${height}px;background:#0a0f14;border:1px solid #30445a;border-radius:9px;touch-action:none;display:block`);
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
      let drag = null;
      canvas.addEventListener('pointerdown', e => { drag = [e.clientX, e.clientY]; canvas.setPointerCapture(e.pointerId); });
      canvas.addEventListener('pointermove', e => { if (!drag) return; az += (e.clientX - drag[0]) * 0.01; el = Math.max(-1.4, Math.min(1.4, el + (e.clientY - drag[1]) * 0.01)); drag = [e.clientX, e.clientY]; draw(); });
      canvas.addEventListener('pointerup', () => { drag = null; });
      new ResizeObserver(draw).observe(canvas);
      return { canvas, set: tool => { state = { tool }; draw(); } };
    };

    const slider = (label, min, max, value, onChange) => {
      const row = make('label', null, 'display:grid;grid-template-columns:118px 1fr 40px;align-items:center;gap:8px;min-height:40px');
      const input = make('input', null, 'width:100%;accent-color:#b45b1f');
      input.type = 'range'; input.min = min; input.max = max; input.value = value;
      const out = make('span', String(value), 'text-align:right;font:13px ui-monospace,Consolas,monospace');
      input.addEventListener('input', () => { out.textContent = input.value; onChange(+input.value); });
      row.append(make('span', label, 'font-size:14px'), input, out);
      return row;
    };

    // Diagrama 1: eixos (move a ferramenta com os controles e vê X, Y e Z)
    const diagramEixos = () => {
      const box = make('div');
      box.append(make('strong', 'Eixos X, Y e Z'), make('div', 'Mexa nos controles: a ferramenta anda nos três eixos a partir do zero da peça (canto do bloco, Z0 no topo). Arraste o desenho para girar.', 'color:#9baab9;font-size:13px;margin:2px 0 8px'));
      const w = wire3d({ min: [0, 0, -20], max: [80, 60, 0] }, 230);
      const pos = [20, 20, 15];
      const read = make('div', '', 'font:14px ui-monospace,Consolas,monospace;margin:8px 0');
      const upd = () => { w.set(pos); read.textContent = `X${pos[0]} Y${pos[1]} Z${pos[2]}   ${pos[2] > 0 ? '(acima do topo: seguro)' : pos[2] === 0 ? '(no topo da peça)' : '(dentro do bloco!)'}`; };
      box.append(w.canvas, slider('X (esq→dir)', 0, 80, pos[0], v => { pos[0] = v; upd(); }), slider('Y (frente→fundo)', 0, 60, pos[1], v => { pos[1] = v; upd(); }), slider('Z (baixo→cima)', -20, 40, pos[2], v => { pos[2] = v; upd(); }), read);
      setTimeout(upd, 0);
      return box;
    };

    // Vista de cima simples para os diagramas 2 e 3
    const plan2d = (worldW, worldH, height) => {
      const canvas = make('canvas', null, `width:100%;height:${height}px;background:#0a0f14;border:1px solid #30445a;border-radius:9px;display:block`);
      const ctx = canvas.getContext('2d');
      const m = 22;
      const api = {
        canvas, ctx, k: 1,
        begin() {
          const dpr = window.devicePixelRatio || 1; api.k = dpr;
          const cw = Math.round(canvas.clientWidth * dpr), ch = Math.round(canvas.clientHeight * dpr);
          if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
          ctx.clearRect(0, 0, cw, ch);
          api.s = Math.min((cw - 2 * m * dpr) / worldW, (ch - 2 * m * dpr) / worldH);
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
      box.append(make('strong', 'Absoluto (G90) ou incremental (G91)?'), make('div', 'A ferramenta está em X10 Y10. Digite X e Y e escolha o modo: veja para onde ela vai.', 'color:#9baab9;font-size:13px;margin:2px 0 8px'));
      const plan = plan2d(90, 70, 220);
      let mode = 'G90', X = 40, Y = 10;
      const modes = make('div', null, 'display:flex;gap:8px;margin:8px 0');
      const inputs = make('div', null, 'display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:8px');
      const field = (name, val, set) => { const i = make('input', null, 'min-width:0;width:100%;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:17px'); i.type = 'text'; i.inputMode = 'decimal'; i.value = String(val); i.addEventListener('input', () => { const n = Number(i.value.replace(',', '.')); if (!isNaN(n) && i.value.trim() !== '') { set(n); upd(); } }); const l = make('label', null, 'display:grid;gap:4px;font-size:13px'); l.append(make('span', name), i); return l; };
      const read = make('div', '', 'font-size:14px;margin-top:8px;white-space:pre-wrap');
      const mbtn = {};
      ['G90', 'G91'].forEach(m => { const b = btn(m === 'G90' ? 'G90 absoluto' : 'G91 incremental', '#21262d', () => { mode = m; upd(); }); mbtn[m] = b; modes.append(b); });
      inputs.append(field('X', X, v => { X = v; }), field('Y', Y, v => { Y = v; }));
      const upd = () => {
        Object.keys(mbtn).forEach(m => { mbtn[m].style.background = m === mode ? '#b45b1f' : '#21262d'; });
        const dx = mode === 'G90' ? X : 10 + X, dy = mode === 'G90' ? Y : 10 + Y;
        plan.begin();
        for (let g = 0; g <= 80; g += 10) { plan.line([g, 0], [g, 60], '#1c2733', 1); plan.text(String(g), g, 0, '#6b7a8a', 10, -4, 14); }
        for (let g = 0; g <= 60; g += 10) { plan.line([0, g], [80, g], '#1c2733', 1); if (g) plan.text(String(g), 0, g, '#6b7a8a', 10, -18, 4); }
        plan.rect(0, 0, 80, 60, 'rgba(120,140,160,.10)', '#5b6b7b');
        plan.line([10, 10], [dx, dy], '#ffb454', 2, [6, 4]);
        plan.dot(10, 10, '#7cc4ff', 5); plan.text('início X10 Y10', 10, 10, '#7cc4ff', 11, 8, -8);
        plan.dot(dx, dy, '#ff5c5c', 6); plan.text(`destino X${dx} Y${dy}`, dx, dy, '#ff9a83', 11, 8, -8);
        read.textContent = mode === 'G90'
          ? `G90 X${X} Y${Y}  →  vai para o ponto X${dx} Y${dy}.\nEm absoluto, X e Y dizem ONDE a ferramenta fica.`
          : `G91 X${X} Y${Y}  →  anda ${X} em X e ${Y} em Y a partir do ponto atual e chega em X${dx} Y${dy}.\nEm incremental, X e Y dizem QUANTO a ferramenta anda.`;
      };
      box.append(plan.canvas, modes, inputs, read);
      new ResizeObserver(upd).observe(plan.canvas);
      setTimeout(upd, 0);
      return box;
    };

    // Diagrama 3: zero máquina, zero peça (G54) e o erro de medição
    const diagramZeros = () => {
      const box = make('div');
      box.append(make('strong', 'Zero máquina, zero peça e G54'), make('div', 'A mesa tem o zero da máquina (canto). O G54 guarda onde está o zero da PEÇA. Mexa no G54 e no erro de medição: a ferramenta vai para X20 Y20 do G54.', 'color:#9baab9;font-size:13px;margin:2px 0 8px'));
      const plan = plan2d(210, 150, 240);
      let ox = 60, oy = 40, err = 0;
      const read = make('div', '', 'font-size:14px;margin-top:8px;white-space:pre-wrap');
      const upd = () => {
        plan.begin();
        plan.rect(0, 0, 200, 140, 'rgba(120,140,160,.08)', '#5b6b7b');
        plan.dot(0, 0, '#6bb0ff', 6); plan.text('zero máquina', 0, 0, '#6bb0ff', 11, 8, -8);
        const realX = ox + err;
        plan.rect(realX, oy, 80, 60, 'rgba(255,180,84,.18)', '#ffb454');
        if (err !== 0) plan.rect(ox, oy, 80, 60, null, '#7cc4ff', [5, 4]);
        plan.dot(realX, oy, '#ffb454', 5); plan.text('zero da peça (real)', realX, oy, '#ffb454', 11, 8, 14);
        if (err !== 0) plan.text(`G54 aponta aqui (X${ox} Y${oy})`, ox, oy, '#7cc4ff', 11, -8, -8);
        plan.dot(ox + 20, oy + 20, '#ff5c5c', 6); plan.text('X20 Y20', ox + 20, oy + 20, '#ff9a83', 11, 8, -8);
        const lands = 20 - err;
        read.textContent = `G54 = X${ox} Y${oy} (valor guardado no controle).\nG00 X20 Y20 leva a ferramenta a X${ox + 20} Y${oy + 20} da máquina.\n` + (err === 0
          ? 'Como o G54 está certo, isso é X20 Y20 da PEÇA.'
          : `Mas a peça está ${err > 0 ? err + ' mm mais à direita' : -err + ' mm mais à esquerda'}: a ferramenta chega a X${lands} da peça, e não X20. Esse é o erro de quem confia só no G54.`);
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
      if (e.prefix) box.append(make('pre', e.prefix, 'background:#0a0f14;border:1px solid #30445a;border-bottom:none;border-radius:8px 8px 0 0;padding:8px 10px;margin:8px 0 0;font:13px/1.45 ui-monospace,Consolas,monospace;color:#6b7a8a'));
      const ta = make('textarea', null, `width:100%;box-sizing:border-box;min-height:84px;background:#161b22;color:#e6edf3;border:1px solid #4a5665;border-radius:${e.prefix ? '0 0 8px 8px' : '8px'};padding:8px 10px;font:14px/1.45 ui-monospace,Consolas,monospace;margin-bottom:8px`);
      ta.value = e.initial; ta.spellcheck = false; ta.autocapitalize = 'characters'; ta.rows = Math.max(3, e.initial.split('\n').length + 1);
      const out = make('div', '', 'font-size:14px;margin:6px 0;white-space:pre-wrap');
      const row = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap');
      const ans = make('div', 'Gabarito: ' + ex.answer, 'display:none;color:#6fd39b;font-size:14px;margin-top:6px;white-space:pre-wrap');
      row.append(btn('Conferir', '#b45b1f', () => {
        const r = ask('check', { id: lessonId, text: ta.value });
        if (!r.ok) { out.textContent = 'Não foi possível conferir.'; out.style.color = '#ff9a83'; return; }
        out.style.color = r.passed ? '#6fd39b' : '#ff9a83';
        out.textContent = r.passed ? '✓ Certo! O programa faz o que o exercício pede.' : '✗ Ainda não:\n' + r.messages.join('\n');
      }, 2), btn('Recomeçar', '#30363d', () => { ta.value = e.initial; out.textContent = ''; }), btn('Ver gabarito', '#30363d', () => { ans.style.display = 'block'; }));
      box.append(ta, row, out, ans);
      return box;
    };

    // ---------- Aula ----------
    const lessonScreen = (id, mod) => {
      const l = ask('lesson', { id });
      if (!l.ok) { screen(header('Aula', () => moduleScreen(mod)), hint('Aula não encontrada.')); return; }
      const parts = [header(l.title, () => moduleScreen(mod), l.draft)];
      parts.push(make('p', 'Objetivo: ' + l.goal, 'color:#9baab9;margin:0 0 10px'));
      if (l.video && l.video.url) {
        const v = card('background:#1a1424;border-color:#5b3d7a');
        v.append(make('strong', '🎬 Vídeo desta aula'), make('div', l.video.title || '', 'color:#c9d6e2;font-size:13px;margin:2px 0 8px'));
        const a = make('a', 'Assistir no YouTube', 'display:inline-block;min-height:44px;line-height:44px;padding:0 16px;border-radius:8px;background:#7a1f1f;color:white;font-weight:700;text-decoration:none');
        a.href = l.video.url; a.target = '_blank'; a.rel = 'noopener noreferrer';
        v.append(a); parts.push(v);
      }
      parts.push(cardReader(l.text, l.quiz && l.quiz.length ? l.quiz[0] : null));
      if (l.commonMistake && !/^Descrito no texto/i.test(l.commonMistake)) {
        const c = card('background:#2a1d10;border-color:#7a4a1f');
        c.append(make('strong', 'Erro comum no chão de fábrica'), make('div', l.commonMistake, 'margin-top:4px'));
        parts.push(c);
      }
      (l.diagrams || []).forEach(id => {
        const make_ = DIAGRAMS[id];
        if (!make_) return;
        const c = card('background:#10161d');
        c.append(make_());
        parts.push(c);
      });
      if (l.program) {
        const c = card();
        c.append(make('strong', 'Programa de exemplo: ' + l.programName));
        if (l.walk && l.walk.lines.length) {
          c.append(make('div', 'Veja a ferramenta andar e leia o programa linha a linha.', 'color:#9baab9;font-size:13px;margin:2px 0 8px'));
          c.append(walkViewer(l.walk));
        } else {
          c.append(make('pre', l.program, 'background:#0a0f14;border:1px solid #30445a;border-radius:8px;padding:10px;margin:8px 0;overflow:auto;font:12px/1.45 ui-monospace,Consolas,monospace;color:#c9d6e2'));
        }
        c.append(make('div', l.warning, 'color:#f3c969;font-size:12px;margin:8px 0'));
        const row = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap');
        const open = view3d => finish(JSON.stringify({ name: l.programName.toUpperCase(), text: l.program, view3d }));
        row.append(btn('Abrir no editor', '#2d4a63', () => open(false)), btn('Ver em 3D completo', '#30363d', () => open(true), 2));
        c.append(row);
        parts.push(c);
      }
      if (l.exercise) {
        const c = card();
        c.append(make('strong', 'Exercício'), make('div', l.exercise.prompt, 'margin:4px 0 8px;white-space:pre-wrap'));
        if (l.exercise.editor) {
          c.append(exerciseEditor(l.id, l.exercise));
        } else {
          const ans = make('div', 'Gabarito: ' + l.exercise.answer, 'display:none;color:#6fd39b;font-size:14px;margin-bottom:6px;white-space:pre-wrap');
          const show = btn('Ver gabarito', '#30363d', () => { ans.style.display = 'block'; show.remove(); }, 0);
          c.append(ans, show);
        }
        parts.push(c);
      }
      parts.push(make('h3', 'Quiz da aula', 'margin:16px 0 8px'));
      parts.push(quizBlock(l.quizId, l.quiz, 70, r => {
        if (r.passed) wrap.append(hint('Aula concluída! Volte ao módulo para seguir.'));
      }));
      screen(...parts);
    };

    // ---------- Teste final ----------
    const finalScreen = mod => {
      const t = ask('final', { module: mod.id, seed: Math.floor(Math.random() * 1e6) });
      if (!t.ok) { screen(header('Teste final', () => moduleScreen(mod)), hint('Teste não encontrado.')); return; }
      const intro = hint(`Nota mínima: ${t.passPercent}%. ${t.canSkip ? 'Quem já conhece o assunto pode fazer este teste direto, sem passar pelas aulas.' : ''}`);
      screen(header('Teste final: ' + mod.title, () => moduleScreen(mod), t.draft), intro, quizBlock(t.quizId, t.quiz, t.passPercent, () => {}));
    };

    home();
  });
}
