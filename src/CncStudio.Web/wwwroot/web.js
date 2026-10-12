// Funções do navegador usadas pelo Casillas Code (chamadas do C# via JSImport).
// Usam elementos HTML nativos onde o celular funciona melhor: seletor de arquivo e editor de texto.

/** Aparelho de toque (celular/tablet): teclado virtual, sem mouse. */
export function isTouch() {
    return window.matchMedia('(pointer: coarse)').matches;
}

/**
 * Abre o seletor de arquivos do aparelho e lê o arquivo como texto.
 * Resolve com "nome\nconteúdo" ou null se o usuário cancelar.
 */
export function pickFile() {
    return new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        // Sem filtro de tipo: no Android, extensões como .nc não têm tipo conhecido e ficariam cinza
        input.style.display = 'none';
        document.body.appendChild(input);

        let done = false;
        const finish = value => {
            if (done) return;
            done = true;
            input.remove();
            resolve(value);
        };

        input.addEventListener('change', async () => {
            const file = input.files && input.files[0];
            if (!file) { finish(null); return; }
            try {
                finish(file.name + '\n' + await file.text());
            } catch {
                finish(null);
            }
        });
        input.addEventListener('cancel', () => finish(null));
        input.click();
    });
}

/**
 * Editor de texto nativo em tela cheia (o teclado do celular funciona 100% aqui).
 * Abre com o cursor no início da linha "line". Resolve com o texto novo ou null (cancelar).
 */
export function editText(text, line, title) {
    return new Promise(resolve => {
        const overlay = document.createElement('div');
        overlay.style.cssText =
            'position:fixed;inset:0;z-index:9999;display:flex;flex-direction:column;background:#0d1117;' +
            'padding:env(safe-area-inset-top,0px) 0 env(safe-area-inset-bottom,0px) 0;' +
            'font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;color:#c9d1d9';

        const bar = document.createElement('div');
        bar.style.cssText = 'display:flex;align-items:center;gap:8px;padding:8px 10px;background:#161b22;border-bottom:1px solid #30363d';

        const label = document.createElement('div');
        label.textContent = title || 'Editar programa';
        label.style.cssText = 'flex:1;min-width:0;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis';

        const button = (caption, primary) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.textContent = caption;
            b.style.cssText = 'min-height:38px;padding:6px 14px;border-radius:6px;font-size:15px;cursor:pointer;' +
                (primary ? 'background:#f0883e;color:#0d1117;border:1px solid #f0883e;font-weight:600'
                         : 'background:transparent;color:#c9d1d9;border:1px solid #30363d');
            return b;
        };
        const cancel = button('Cancelar', false);
        const apply = button('Aplicar', true);

        const area = document.createElement('textarea');
        area.value = text;
        area.spellcheck = false;
        area.setAttribute('autocapitalize', 'characters'); // G-code é em maiúsculas
        area.setAttribute('autocorrect', 'off');
        area.setAttribute('autocomplete', 'off');
        area.wrap = 'off';
        area.style.cssText =
            'flex:1;width:100%;box-sizing:border-box;margin:0;padding:10px 12px;border:0;outline:none;resize:none;' +
            'background:#0d1117;color:#e6edf3;caret-color:#f0883e;white-space:pre;overflow:auto;' +
            'font:15px/1.5 ui-monospace,"DejaVu Sans Mono",Menlo,Consolas,monospace;tab-size:4';

        bar.append(label, cancel, apply);
        overlay.append(bar, area);
        document.body.appendChild(overlay);

        const close = value => {
            overlay.remove();
            resolve(value);
        };
        cancel.addEventListener('click', () => close(null));
        apply.addEventListener('click', () => close(area.value));

        // Cursor no início da linha pedida, rolando para ela ficar no terço de cima
        const lines = text.split('\n');
        let offset = 0;
        for (let i = 0; i < Math.min(Math.max(line, 1) - 1, lines.length); i++) offset += lines[i].length + 1;
        area.focus({ preventScroll: true });
        area.setSelectionRange(offset, offset);
        const lineHeight = 15 * 1.5;
        area.scrollTop = Math.max(0, (Math.max(line, 1) - 1) * lineHeight - area.clientHeight / 3);
    });
}
