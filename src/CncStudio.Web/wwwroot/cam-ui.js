// Casillas CAM (web, celular): forma + operação de fresa + 4º eixo opcional. Formulário em HTML; o cálculo é do Core (C#).
// catalogJson: [{group,id,label,kind:"num"|"select"|"check",def,unit,options,hint,when,show}]
// generate(valuesJson) -> JSON {ok, program, errors[], warnings[]}. Resolve com JSON {text, view3d} ou null.
export function openCam(catalogJson, generate, lastJson) {
  return new Promise(resolve => {
    let catalog;
    try { catalog = JSON.parse(catalogJson); } catch { resolve(null); return; }
    let last = {};
    try { last = JSON.parse(lastJson || '{}') || {}; } catch { last = {}; }

    const css = (el, text) => { el.style.cssText = text; return el; };
    const make = (tag, text, style) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (style) css(el, style); return el; };
    const inputStyle = 'min-width:0;width:100%;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:17px';

    const root = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h,0px);z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px system-ui,sans-serif;padding:14px 14px 60px;box-sizing:border-box');
    const wrap = make('div', null, 'max-width:640px;margin:0 auto');
    wrap.append(make('h2', 'Casillas CAM', 'margin:0 0 4px'),
      make('p', 'Escolha a forma e a operação: o programa é calculado na hora. Rascunho: simule e rode no ar antes de usar na máquina.', 'color:#9baab9;font-size:13px;margin:0 0 12px'));

    const rows = {};   // id -> { field, input, row }
    let timer = 0;
    const groups = {};
    for (const f of catalog) {
      if (!groups[f.group]) {
        const box = make('div', null, 'display:flex;flex-direction:column;gap:6px;border:1px solid #303d4b;border-radius:9px;padding:12px;margin-bottom:12px');
        box.append(make('strong', f.group, 'color:#8ab4ff'));
        groups[f.group] = box;
        wrap.append(box);
      }
      const saved = last[f.id];
      const row = make('label', null, 'display:grid;grid-template-columns:minmax(0,1fr) minmax(110px,1fr);align-items:center;gap:12px;min-height:46px');
      let input;
      if (f.kind === 'select') {
        input = make('select', null, inputStyle);
        f.options.forEach((o, i) => { const op = document.createElement('option'); op.value = String(i); op.textContent = o; input.append(op); });
        input.value = String(saved ?? f.def);
      } else if (f.kind === 'check') {
        input = make('input', null, 'width:26px;height:26px;justify-self:end');
        input.type = 'checkbox';
        input.checked = saved ?? f.def;
      } else {
        input = make('input', null, inputStyle);
        input.type = 'text'; input.inputMode = 'decimal'; input.autocomplete = 'off';
        input.value = String(saved ?? f.def).replace('.', ',');
      }
      input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(update, 120); });
      input.addEventListener('change', () => { clearTimeout(timer); timer = setTimeout(update, 60); });
      row.append(make('span', f.label + (f.unit ? ' (' + f.unit + ')' : '')), input);
      groups[f.group].append(row);
      const hint = f.hint ? make('div', f.hint, 'color:#8b9aa9;font-size:12px;margin:-4px 0 4px') : null;
      if (hint) groups[f.group].append(hint);
      rows[f.id] = { f, input, row, hint };
    }

    const values = () => {
      const v = {};
      for (const { f, input } of Object.values(rows)) {
        if (f.kind === 'check') v[f.id] = input.checked;
        else if (f.kind === 'select') v[f.id] = Number(input.value);
        else { const raw = input.value.replace(',', '.').trim(); v[f.id] = raw === '' || isNaN(Number(raw)) ? null : Number(raw); }
      }
      return v;
    };

    const visibility = v => {
      for (const { f, row, hint } of Object.values(rows)) {
        let on = true;
        if (f.when) on = !!v[f.when];
        if (on && f.show) { const [k, val] = f.show.split('='); on = String(v[k]) === val; }
        row.style.display = on ? '' : 'none';
        if (hint) hint.style.display = on ? '' : 'none';
      }
    };

    const errors = make('div', '', 'color:#ff9a83;font-size:13px;line-height:1.5;margin-bottom:8px;white-space:pre-wrap');
    const warnings = make('div', '', 'color:#f3c969;font-size:13px;line-height:1.5;margin-bottom:8px;white-space:pre-wrap');
    const preview = make('pre', '', 'background:#0a0f14;border:1px solid #30445a;border-radius:9px;padding:10px;margin:0 0 12px;max-height:260px;overflow:auto;font:12px/1.45 ui-monospace,Consolas,monospace;color:#c9d6e2;white-space:pre');
    const actions = make('div', null, 'display:flex;gap:8px;flex-wrap:wrap');
    const button = (label, bg, grow) => { const b = make('button', label, `flex:${grow};min-width:120px;min-height:48px;border:1px solid #4a5665;border-radius:8px;background:${bg};color:white;font-weight:700;font-size:15px`); b.type = 'button'; actions.append(b); return b; };
    const cancel = button('Cancelar', '#30363d', 1);
    const openBtn = button('Abrir no editor', '#2d4a63', 1);
    const open3d = button('Ver em 3D', '#b45b1f', 2);
    wrap.append(errors, warnings, preview, actions);
    root.append(wrap);
    document.body.append(root);

    let lastText = '';
    const update = () => {
      const v = values();
      visibility(v);
      let result;
      try { result = JSON.parse(generate(JSON.stringify(v))); }
      catch (e) { result = { ok: false, program: '', errors: ['Não foi possível gerar: ' + e], warnings: [] }; }
      lastText = result.ok ? result.program : '';
      errors.textContent = result.ok ? '' : result.errors.join('\n');
      warnings.textContent = (result.warnings || []).join('\n');
      preview.textContent = result.ok ? result.program : 'Corrija os campos acima para ver o programa.';
      openBtn.disabled = open3d.disabled = !result.ok;
      openBtn.style.opacity = open3d.style.opacity = result.ok ? '1' : '0.5';
    };
    update();

    const onClose = () => finish(null);
    const finish = value => { window.removeEventListener('casillas:close-overlays', onClose); root.remove(); window.dispatchEvent(new CustomEvent('casillas:module-closed', { detail: 'cam' })); resolve(value); };
    window.addEventListener('casillas:close-overlays', onClose);
    cancel.addEventListener('click', () => finish(null));
    const done = view3d => { if (lastText) finish(JSON.stringify({ text: lastText, view3d, values: values() })); };
    openBtn.addEventListener('click', () => done(false));
    open3d.addEventListener('click', () => done(true));
  });
}
