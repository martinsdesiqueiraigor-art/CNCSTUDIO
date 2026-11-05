// Casillas Otimizador: acha repetições num programa CAM Fanuc e propõe laços Macro B SÓ PARA SIMULADOR.
// O cálculo é do Core (C#, OptimizerJson); esta tela só mostra o resultado e entrega o programa ao editor.
// call(comando, argsJson) -> JSON; programText/programName: o programa aberto no editor.
// Resolve com JSON {action: 'editor'|'view3d'|'compare', name, text, original, originalName} ou null.
export function openOptimizer(call, initialText, initialName) {
  return new Promise(resolve => {
    let programText = initialText || '', programName = initialName || 'programa.nc';
    const css = (el, text) => { el.style.cssText = text; return el; };
    const make = (tag, text, style) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (style) css(el, style); return el; };
    const btn = (label, bg, onClick, grow = 1) => {
      const b = make('button', label, `flex:${grow};min-width:120px;min-height:46px;border:1px solid #4a5665;border-radius:8px;background:${bg};color:white;font-weight:700;font-size:15px`);
      b.type = 'button'; b.addEventListener('click', onClick); return b;
    };
    const card = (extra = '') => make('div', null, 'border:1px solid #303d4b;border-radius:10px;padding:12px;margin:0 0 10px;background:#131a22;' + extra);
    const ask = (cmd, args) => { try { return JSON.parse(call(cmd, JSON.stringify(args))); } catch (e) { return { ok: false, error: String(e) }; } };

    const root = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h,0px);z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px/1.5 system-ui,sans-serif;padding:14px 14px 40px;box-sizing:border-box');
    const wrap = make('div', null, 'max-width:680px;margin:0 auto');
    root.append(wrap);
    document.body.append(root);

    const onClose = () => finish(null);
    const finish = value => {
      window.removeEventListener('casillas:close-overlays', onClose);
      root.remove();
      window.dispatchEvent(new CustomEvent('casillas:module-closed', { detail: 'otimizador' }));
      resolve(value);
    };
    window.addEventListener('casillas:close-overlays', onClose);

    const screen = (...nodes) => { wrap.replaceChildren(...nodes); root.scrollTop = 0; };
    const header = (title, onBack) => {
      const row = make('div', null, 'display:flex;align-items:center;gap:10px;margin-bottom:10px');
      row.append(btn(onBack ? '‹ Voltar' : 'Fechar', '#30363d', onBack || (() => finish(null)), 0), make('h2', title, 'margin:0;font-size:19px;flex:1'));
      return row;
    };
    const safety = () => {
      const c = card('background:#2a1414;border-color:#7a2a2a');
      c.append(make('strong', 'Somente para simulador. Não executar na máquina.'), make('div', 'As propostas em Macro B são conferidas bloco a bloco contra o original, mas isso não é homologação de controle. O programa original nunca é alterado.', 'font-size:13px;color:#e6c5c5;margin-top:4px'));
      return c;
    };
    const KINDS = { 'NIVEIS Z': 'Níveis em Z', 'TRANSLACAO XY': 'Cópias em XY', 'PASSOS LINEARES': 'Passos lineares', 'GRADE': 'Grade', 'CIRCULO': 'Círculo (furos ou arcos)', 'HELICE': 'Hélice', 'REPETICAO IDENTICA': 'Repetição idêntica' };
    const pct = x => Math.round(x * 100) + '%';

    // ---------- Entrada ----------
    const msg = make('div', '', 'color:#ff9a83;font-size:14px;margin-top:8px');
    const start = () => {
      msg.textContent = '';
      const lines = programText.trim() ? programText.split(/\r?\n/).length : 0;
      const intro = card();
      intro.append(make('strong', 'Programa a analisar'), make('div', lines ? programName + ' · ' + lines + ' linhas' : 'Nenhum programa aberto no editor.', 'margin:4px 0 8px;color:#c9d6e2'));
      const file = make('input', null, 'display:none'); file.type = 'file'; file.accept = '.nc,.tap,.txt,.mpf,.cnc,.min,.eia';
      file.addEventListener('change', async () => {
        const f = file.files && file.files[0]; if (!f) return;
        try { programText = await f.text(); programName = f.name; start(); } catch { msg.textContent = 'Não foi possível ler o arquivo.'; }
      });
      intro.append(file, btn(lines ? 'Trocar por outro arquivo…' : 'Escolher arquivo…', '#30363d', () => file.click(), 0));
      intro.lastChild.style.margin = '0 0 10px';
      intro.append(make('div', 'Procura repetições (níveis em Z, cópias em XY, bolsões, grades, círculos de furos, hélices e subprogramas) e propõe laços Macro B para você simular no Code.', 'font-size:13px;color:#9baab9'));
      const sel = make('select', null, 'width:100%;box-sizing:border-box;min-height:46px;margin-top:10px;background:#161b22;color:white;border:1px solid #4a5665;border-radius:8px;font-size:15px;padding:8px');
      [['calculadora', 'Entrada decimal tipo calculadora (3401#0=1): X10 = 10 mm'], ['convencional', 'Entrada decimal convencional (3401#0=0): X10 = 0,010 mm']].forEach(([v, t]) => { const o = document.createElement('option'); o.value = v; o.textContent = t; sel.append(o); });
      try { const saved = localStorage.getItem('cc.otimizador.perfil'); if (saved) sel.value = saved; } catch { /* sem armazenamento */ }
      intro.append(make('div', 'Como o controle lê números sem ponto decimal:', 'font-size:13px;color:#9baab9;margin-top:10px'), sel);
      const run = btn('Analisar programa', '#1f6f43', () => {
        try { localStorage.setItem('cc.otimizador.perfil', sel.value); } catch { /* ignora */ }
        msg.textContent = '';
        run.disabled = true; run.textContent = 'Analisando…';
        setTimeout(() => {
          const r = ask('analyze', { text: programText, profile: sel.value });
          run.disabled = false; run.textContent = 'Analisar programa';
          if (!r.ok) { msg.textContent = r.error || 'Não foi possível analisar.'; return; }
          result(r, sel.value);
        }, 30);
      }, 0);
      run.style.width = '100%'; run.style.marginTop = '12px'; if (!lines) run.disabled = true;
      intro.append(run, msg);
      screen(header('Casillas Otimizador'), safety(), intro);
    };

    // ---------- Resultado ----------
    const stat = (value, label, color) => {
      const s = make('div', null, 'flex:1;min-width:70px;text-align:center;border:1px solid #303d4b;border-radius:10px;padding:8px 4px;background:#131a22');
      s.append(make('div', String(value), `font-size:22px;font-weight:800;color:${color || '#e6edf3'}`), make('div', label, 'font-size:12px;color:#9baab9'));
      return s;
    };
    const download = (name, text, type) => {
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([text], { type: type || 'text/plain;charset=utf-8' }));
      a.download = name; document.body.append(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 4000);
    };
    const baseName = () => programName.replace(/\.[^.]+$/, '');

    const proposalCard = p => {
      const ok = p.accepted;
      const c = card(`border-color:${ok ? '#2f7a4f' : '#7a3b3b'}`);
      const top = make('div', null, 'display:flex;gap:8px;align-items:center;flex-wrap:wrap');
      top.append(make('strong', `#${p.number} ${KINDS[p.kind] || p.kind}`, 'flex:1'),
        make('span', ok ? 'Proposta para simulador' : 'Bloqueada', `font-size:11px;font-weight:700;border-radius:999px;padding:2px 9px;${ok ? 'background:#12301f;color:#6fd39b;border:1px solid #2f7a4f' : 'background:#301616;color:#ff9a83;border:1px solid #7a3b3b'}`));
      c.append(top, make('div', `Linhas ${p.startLine} a ${p.endLine} · ${p.count} repetições de ${p.period} blocos` + (ok ? ` · economiza ${p.savedLines} linhas` : ''), 'font-size:13px;color:#c9d6e2;margin-top:4px'));
      (p.blockers || []).forEach(b => c.append(make('div', '⛔ ' + b, 'font-size:13px;color:#ff9a83;margin-top:4px')));
      (p.warnings || []).forEach(w => c.append(make('div', '⚠ ' + w, 'font-size:13px;color:#f3c969;margin-top:4px')));
      if (p.fragment) {
        const d = make('details', null, 'margin-top:8px');
        d.append(make('summary', 'Ver macro gerada', 'cursor:pointer;color:#9ecbff;min-height:32px'));
        d.append(make('pre', p.fragment, 'background:#0a0f14;border:1px solid #30445a;border-radius:8px;padding:10px;margin:6px 0;overflow:auto;font:12px/1.45 ui-monospace,Consolas,monospace;color:#c9d6e2'));
        if (p.expanded && p.expanded.length) {
          d.append(make('div', `Primeiros blocos que o laço reproduz (${p.expandedTotal} no total):`, 'font-size:12px;color:#9baab9'));
          d.append(make('pre', p.expanded.join('\n'), 'background:#0a0f14;border:1px solid #30445a;border-radius:8px;padding:10px;margin:6px 0;overflow:auto;font:12px/1.45 ui-monospace,Consolas,monospace;color:#c9d6e2'));
        }
        c.append(d);
      }
      return c;
    };

    const result = (r, profile) => {
      const parts = [header('Resultado', start), safety()];
      const stats = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap;margin-bottom:10px');
      const ecoPct = r.originalLines > 0 ? Math.round(r.savedLines / r.originalLines * 100) : 0;
      stats.append(stat(r.originalLines, 'linhas antes'), stat(r.convertedLines, 'linhas depois'), stat(r.savedLines, 'linhas a menos', r.savedLines > 0 ? '#6fd39b' : '#e6edf3'), stat(ecoPct + '%', 'economia', r.savedLines > 0 ? '#6fd39b' : '#e6edf3'));
      parts.push(stats, make('div', 'Estimativa: a economia é de linhas do programa, não de tempo de usinagem. O tempo de corte continua o mesmo.', 'font-size:12px;color:#9baab9;margin:-4px 0 10px'));
      const stats2 = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap;margin-bottom:12px');
      stats2.append(stat(r.accepted, 'propostas', '#6fd39b'), stat(r.blocked, 'bloqueadas', r.blocked ? '#ff9a83' : '#e6edf3'), stat(pct(r.coverage), 'blocos entendidos'));
      parts.push(stats2);
      const info = make('div', `${r.operations.length} operação(ões) · CAM: ${r.cam || 'não reconhecido'} · ${r.lineCount} linhas`, 'font-size:13px;color:#9baab9;margin-bottom:10px');
      parts.push(info);

      // ações
      const acts = card('background:#10161d');
      if (r.simulationProgram) {
        acts.append(make('strong', 'Simular a versão otimizada'), make('div', 'Abra no editor e rode o 3D para ver que o resultado é o mesmo.', 'font-size:13px;color:#9baab9;margin:2px 0 8px'));
        const row = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap');
        const pack = action => finish(JSON.stringify({ action, name: baseName() + '_OTIMIZADO.nc', text: r.simulationProgram, original: programText, originalName: programName }));
        row.append(btn('Abrir no editor', '#2d4a63', () => pack('editor')), btn('Ver em 3D', '#30363d', () => pack('view3d')), btn('Comparar com o original', '#30363d', () => pack('compare'), 2));
        acts.append(row);
      } else {
        acts.append(make('strong', 'Nenhuma versão para simular'));
        const why = (r.simulationBlockers || []);
        acts.append(make('div', r.accepted === 0 ? 'Nenhuma repetição foi aceita neste programa.' : 'A versão completa não passou na conferência.', 'font-size:13px;color:#c9d6e2;margin-top:2px'));
        why.forEach(w => acts.append(make('div', '⛔ ' + w, 'font-size:13px;color:#ff9a83;margin-top:4px')));
      }
      const dl = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap;margin-top:10px');
      dl.append(btn('Baixar revisão (.txt)', '#30363d', () => download(baseName() + '_revisao.txt', r.document)));
      const anon = btn('Cópia anonimizada', '#30363d', () => {
        const a = ask('anonymize', { text: programText });
        if (a.ok) download(baseName() + '_anonimo.nc', a.text); else alert(a.error || 'Não foi possível anonimizar.');
      });
      dl.append(anon);
      acts.append(dl, make('div', 'A cópia anonimizada troca os comentários por códigos: serve para compartilhar um programa real sem expor o cliente.', 'font-size:12px;color:#9baab9;margin-top:6px'));
      parts.push(acts);

      parts.push(make('h3', `Propostas (${r.proposals.length})`, 'margin:14px 0 8px'));
      if (r.proposals.length === 0) parts.push(make('p', 'Nenhuma repetição que compense virar laço foi encontrada.', 'color:#9baab9'));
      r.proposals.forEach(p => parts.push(proposalCard(p)));

      const ops = make('details', null, 'margin:10px 0');
      ops.append(make('summary', `Operações encontradas (${r.operations.length})`, 'cursor:pointer;min-height:36px;font-weight:700'));
      r.operations.forEach(o => ops.append(make('div', `${o.number}. ${o.label || '(sem nome)'} · T${o.tool} · ${o.wcs || ''} · linhas ${o.startLine}-${o.endLine} · ${o.originalLines} → ${o.convertedLines}`, 'font-size:13px;color:#c9d6e2;padding:4px 0;border-bottom:1px solid #1f2933')));
      parts.push(ops);

      if (r.unmodeled && r.unmodeled.length) {
        const un = make('details', null, 'margin:10px 0');
        un.append(make('summary', `Blocos que o Otimizador não entende (${r.unmodeled.length})`, 'cursor:pointer;min-height:36px;font-weight:700'));
        r.unmodeled.forEach(u => un.append(make('div', `L${u.line}: ${u.text}  — ${u.note}`, 'font:12px/1.4 ui-monospace,Consolas,monospace;color:#c9d6e2;padding:3px 0;word-break:break-all')));
        parts.push(un);
      }
      parts.push(make('div', null, 'height:12px'), btn('‹ Voltar', '#30363d', start), make('div', null, 'height:24px'));
      screen(...parts);
    };

    start();
  });
}
