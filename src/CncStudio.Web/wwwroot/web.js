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
        input.accept = '.nc,.mpf,.spf,.txt,.tap,.cnc,.iso,.ngc,.eia,.h,text/plain,application/octet-stream,*/*';
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

// ───────────── Botão "Abrir" com <input type="file"> de verdade ─────────────
// Um campo de arquivo transparente fica exatamente em cima do botão "Abrir" desenhado pelo Avalonia.
// Assim o toque do usuário cai direto no campo nativo (alguns navegadores e apps só abrem o seletor
// de arquivos com um toque real, não com um clique feito por código).

let fileInput = null;
let pendingResolve = null;
let pickedQueue = [];

function ensureFileInput() {
    if (fileInput) return fileInput;
    fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.title = 'Abrir programa CNC';
    // Sem "accept", o Android mostra só câmera/fotos. Com tipos de arquivo + "*/*" ele abre o
    // gerenciador de arquivos (Downloads, Drive, WhatsApp...), onde ficam os .nc/.mpf
    fileInput.accept = '.nc,.mpf,.spf,.txt,.tap,.cnc,.iso,.ngc,.eia,.h,text/plain,application/octet-stream,*/*';
    fileInput.style.cssText = 'position:fixed;z-index:50;opacity:0;cursor:pointer;margin:0;padding:0;border:0;font-size:0';
    fileInput.addEventListener('change', async () => {
        const file = fileInput.files && fileInput.files[0];
        if (!file) return;
        let payload;
        try {
            payload = file.name + '\n' + await file.text();
        } catch {
            payload = null;
        }
        fileInput.value = ''; // permite abrir o mesmo arquivo de novo
        if (payload === null) return;
        if (pendingResolve) {
            const resolve = pendingResolve;
            pendingResolve = null;
            resolve(payload);
        } else {
            pickedQueue.push(payload);
        }
    });
    document.body.appendChild(fileInput);
    return fileInput;
}

/** Posiciona o campo de arquivo sobre o botão (coordenadas da tela do Avalonia, em px CSS). */
export function placeFileInput(x, y, width, height, visible) {
    const input = ensureFileInput();
    const host = document.getElementById('out');
    const r = host ? host.getBoundingClientRect() : { left: 0, top: 0 };
    input.style.left = (r.left + x) + 'px';
    input.style.top = (r.top + y) + 'px';
    input.style.width = Math.max(0, width) + 'px';
    input.style.height = Math.max(0, height) + 'px';
    input.style.display = visible ? 'block' : 'none';
}

/** Espera o próximo arquivo escolhido pelo campo sobre o botão: "nome\nconteúdo". */
export function nextPickedFile() {
    if (pickedQueue.length > 0) return Promise.resolve(pickedQueue.shift());
    return new Promise(resolve => { pendingResolve = resolve; });
}

// ───────────── Instalar como app (PWA) ─────────────
// O Chrome avisa (beforeinstallprompt) quando o site pode ser instalado; guardamos o aviso para
// mostrar o botão "Instalar" e abrir a janela de instalação quando o usuário tocar nele.
let installEvent = null;
window.addEventListener('beforeinstallprompt', e => { e.preventDefault(); installEvent = e; });
window.addEventListener('appinstalled', () => { installEvent = null; });

/** O navegador permite instalar agora? */
export function canInstall() {
    return installEvent !== null;
}

/** Abre a janela de instalação. Resolve com "accepted" ou "dismissed". */
export async function promptInstall() {
    if (!installEvent) return 'unavailable';
    const e = installEvent;
    installEvent = null;
    await e.prompt();
    const choice = await e.userChoice;
    return choice && choice.outcome ? choice.outcome : 'dismissed';
}
