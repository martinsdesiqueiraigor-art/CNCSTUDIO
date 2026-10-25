// Casillas Code: geradores de programa (formulário em HTML, igual ao editor de setup).
// O cálculo e a validação são do Core (C#): esta tela só mostra o formulário e pede o programa a cada mudança.
// catalogJson: [{id,title,description,params:[{...}], texts:[{id,label,def,max,hint}], macro:bool}]
// generate(id, valuesJson, mode) -> JSON {ok, program, errors[]}; mode = "plain" | "macro"
// Resolve com JSON {id, mode, text, view3d} ou null se cancelar.
export function openGenerators(catalogJson, generate, lastJson) {
  return new Promise(resolve => {
    let catalog;
    try { catalog = JSON.parse(catalogJson); } catch { resolve(null); return; }
    if (!Array.isArray(catalog) || catalog.length === 0) { resolve(null); return; }
    let last = {};
    try { last = JSON.parse(lastJson || '{}') || {}; } catch { last = {}; }

    const css = (el, text) => { el.style.cssText = text; return el; };
    const make = (tag, text, style) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (style) css(el, style); return el; };
    const inputStyle = 'min-width:0;width:100%;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:17px';

    const root = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h,0px);z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px system-ui,sans-serif;padding:14px 14px 60px;box-sizing:border-box');
    const wrap = make('div', null, 'max-width:640px;margin:0 auto');
    const heading = make('h2', 'Geradores de programa', 'margin:0 0 4px');
    const sub = make('p', 'Preencha os campos: o programa é calculado na hora. Rascunho: confira tudo antes de usar na máquina.', 'color:#9baab9;font-size:13px;margin:0 0 12px');

    const pick = make('select', null, inputStyle);
    catalog.forEach((g, i) => { const o = document.createElement('option'); o.value = String(i); o.textContent = g.title; pick.append(o); });
    const desc = make('p', '', 'color:#9baab9;font-size:13px;margin:8px 0 12px');

    const form = make('div', null, 'display:flex;flex-direction:column;gap:6px;border:1px solid #303d4b;border-radius:9px;padding:12px;margin-bottom:12px');

    const modes = make('div', null, 'display:flex;gap:8px;margin-bottom:6px');
    let mode = last.mode === 'macro' ? 'macro' : 'plain';
    const modeButtons = {};
    const modeHelp = make('p', '', 'color:#9baab9;font-size:12px;margin:0 0 12px');
    [['plain', 'Programa pronto'], ['macro', 'G65 + macro']].forEach(([key, label]) => {
      const b = make('button', label, 'flex:1;min-height:44px;border-radius:8px;border:1px solid #4a5665;color:white;font-weight:700;font-size:14px');
      b.type = 'button';
      b.addEventListener('click', () => { mode = key; refreshModes(); update(); });
      modeButtons[key] = b; modes.append(b);
    });
    const refreshModes = () => {
      for (const k of Object.keys(modeButtons)) modeButtons[k].style.background = k === mode ? '#b45b1f' : '#21262d';
      modeHelp.textContent = mode === 'plain'
        ? 'Cada posição e passe escritos: roda em qualquer controle Fanuc, sem macro instalada.'
        : 'Chamada G65 com a macro no mesmo arquivo: precisa de macro B no controle.';
    };

    const errors = make('div', '', 'color:#ff9a83;font-size:13px;line-height:1.5;margin-bottom:8px;white-space:pre-wrap');
    const preview = make('pre', '', 'background:#0a0f14;border:1px solid #30445a;border-radius:9px;padding:10px;margin:0 0 12px;max-height:260px;overflow:auto;font:12px/1.45 ui-monospace,Consolas,monospace;color:#c9d6e2;white-space:pre');

    const actions = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap');
    const button = (label, bg, grow) => { const b = make('button', label, `flex:${grow};min-width:120px;min-height:48px;border:1px solid #4a5665;border-radius:8px;background:${bg};color:white;font-weight:700;font-size:15px`); b.type = 'button'; actions.append(b); return b; };
    const cancel = button('Cancelar', '#30363d', 1);
    const openBtn = button('Abrir no editor', '#2d4a63', 1);
    const open3d = button('Ver em 3D', '#b45b1f', 2);

    wrap.append(heading, sub, pick, desc, form, modes, modeHelp, errors, preview, actions);
    root.append(wrap);
    document.body.append(root);

    let current = catalog[0];
    let inputs = {};
    let textInputs = {};
    let timer = 0;
    let lastText = '';

    const values = () => {
      const v = {};
      for (const p of current.params) {
        const raw = inputs[p.id].value.replace(',', '.').trim();
        v[p.id] = raw === '' || isNaN(Number(raw)) ? null : Number(raw);
      }
      for (const t of current.texts || []) v[t.id] = textInputs[t.id].value;
      return v;
    };

    const update = () => {
      let result;
      try { result = JSON.parse(generate(current.id, JSON.stringify(values()), current.macro === false ? 'plain' : mode)); }
      catch (e) { result = { ok: false, program: '', errors: ['Não foi possível gerar: ' + e] }; }
      lastText = result.ok ? result.program : '';
      errors.textContent = result.ok ? '' : result.errors.join('\n');
      preview.textContent = result.ok ? result.program : 'Corrija os campos acima para ver o programa.';
      openBtn.disabled = open3d.disabled = !result.ok;
      openBtn.style.opacity = open3d.style.opacity = result.ok ? '1' : '0.5';
    };

    const build = () => {
      form.replaceChildren();
      inputs = {};
      textInputs = {};
      const hasMacro = current.macro !== false;
      modes.style.display = modeHelp.style.display = hasMacro ? '' : 'none';
      desc.textContent = current.description;
      const saved = (last.values && last.values[current.id]) || {};
      for (const t of current.texts || []) {
        const row = make('label', null, 'display:flex;flex-direction:column;gap:6px;margin-bottom:4px');
        row.append(make('span', t.label));
        const input = make('input', null, inputStyle);
        input.type = 'text';
        input.maxLength = t.max;
        input.value = String(saved[t.id] ?? t.def);
        input.autocomplete = 'off';
        input.autocapitalize = 'characters';
        input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(update, 120); });
        textInputs[t.id] = input;
        row.append(input);
        form.append(row);
        if (t.hint) form.append(make('div', t.hint, 'color:#8b9aa9;font-size:12px;margin:-4px 0 4px'));
      }
      for (const p of current.params) {
        const row = make('label', null, 'display:grid;grid-template-columns:minmax(0,1fr) minmax(110px,1fr);align-items:center;gap:12px;min-height:46px');
        const text = make('span', p.label + (p.unit ? ' (' + p.unit + ')' : ''));
        const input = make('input', null, inputStyle);
        input.type = 'text';
        input.inputMode = p.integer && p.min >= 0 ? 'numeric' : 'decimal';
        input.value = String(saved[p.id] ?? p.def).replace('.', ',');
        input.autocomplete = 'off';
        input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(update, 120); });
        inputs[p.id] = input;
        row.append(text, input);
        form.append(row);
        if (p.hint) form.append(make('div', p.hint, 'color:#8b9aa9;font-size:12px;margin:-4px 0 4px'));
      }
      update();
    };

    pick.addEventListener('change', () => { current = catalog[+pick.value]; build(); });
    if (last.id) {
      const i = catalog.findIndex(g => g.id === last.id);
      if (i >= 0) { pick.value = String(i); current = catalog[i]; }
    }
    refreshModes();
    build();

    const onClose = () => finish(null);
    const finish = value => { window.removeEventListener('casillas:close-overlays', onClose); root.remove(); window.dispatchEvent(new CustomEvent('casillas:module-closed', { detail: 'macro' })); resolve(value); };
    window.addEventListener('casillas:close-overlays', onClose);
    cancel.addEventListener('click', () => finish(null));
    const done = view3d => {
      if (!lastText) return;
      finish(JSON.stringify({ id: current.id, mode: current.macro === false ? 'plain' : mode, text: lastText, view3d, values: values() }));
    };
    openBtn.addEventListener('click', () => done(false));
    open3d.addEventListener('click', () => done(true));
  });
}
