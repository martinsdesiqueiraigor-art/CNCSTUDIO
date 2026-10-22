// Funções do navegador usadas pelo Casillas Code (chamadas do C# via JSImport).
// Usam elementos HTML nativos onde o celular funciona melhor: seletor de arquivo e editor de texto.

// Conta (entrar, situação da licença, sair): ver account.js
export * from './account.js';

// Folha de setup em tela cheia (PDF / imprimir / compartilhar): ver setupsheet.js
export * from './setupsheet.js';

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

// Cada botão que abre arquivos ("open", "import") tem o seu campo nativo transparente por cima.
// O resultado é JSON: [{"name": "...", "text": "..."}, ...] (um item, ou vários na importação).

const ACCEPT = '.nc,.mpf,.spf,.txt,.tap,.cnc,.iso,.ngc,.eia,.h,text/plain,application/octet-stream';
const fileInputs = {};   // id → <input>
const pending = {};      // id → resolve da Promise que espera
const queues = {};       // id → resultados que chegaram antes de alguém esperar

function ensureFileInput(id, multiple) {
    if (fileInputs[id]) return fileInputs[id];
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = !!multiple;
    // Sem "accept", o Android oferece câmera/fotos. Só com tipos de texto/arquivo ele vai direto ao
    // gerenciador de arquivos (Downloads, Drive, WhatsApp...), onde ficam os .nc/.mpf
    input.accept = ACCEPT;
    input.style.cssText = 'position:fixed;z-index:50;opacity:0;cursor:pointer;margin:0;padding:0;border:0;font-size:0';
    input.addEventListener('change', async () => {
        const files = Array.from(input.files || []);
        const result = [];
        for (const f of files) {
            try { result.push({ name: f.name, text: await f.text() }); } catch { }
        }
        input.value = ''; // permite escolher o mesmo arquivo de novo
        if (result.length === 0) return;
        const payload = JSON.stringify(result);
        if (pending[id]) {
            const resolve = pending[id];
            delete pending[id];
            resolve(payload);
        } else {
            (queues[id] = queues[id] || []).push(payload);
        }
    });
    document.body.appendChild(input);
    fileInputs[id] = input;
    return input;
}

/** Posiciona o campo de arquivo "id" sobre um botão (coordenadas da tela do Avalonia, em px CSS). */
export function placeFileInput(id, multiple, x, y, width, height, visible) {
    const input = ensureFileInput(id, multiple);
    const host = document.getElementById('out');
    const r = host ? host.getBoundingClientRect() : { left: 0, top: 0 };
    input.style.left = (r.left + x) + 'px';
    input.style.top = (r.top + y) + 'px';
    input.style.width = Math.max(0, width) + 'px';
    input.style.height = Math.max(0, height) + 'px';
    input.style.display = visible ? 'block' : 'none';
}

/** Espera a próxima escolha no campo "id": JSON [{name, text}]. */
export function nextPickedFiles(id) {
    const q = queues[id];
    if (q && q.length > 0) return Promise.resolve(q.shift());
    return new Promise(resolve => { pending[id] = resolve; });
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

// ───────────── Memória do app (estado entre aberturas) e vibração ─────────────

/** Lê um valor guardado no aparelho (ou null). */
export function storageGet(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

/** Guarda um valor no aparelho (falha em silêncio se o navegador não deixar). */
export function storageSet(key, value) {
    try { localStorage.setItem(key, value); } catch { }
}

/** Vibração curta no toque (Android; no iPhone o navegador não vibra). */
export function vibrate(ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch { }
}

/** Chama o .NET quando o app vai para segundo plano (para salvar o estado na hora). */
export function onHidden(callback) {
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') callback();
    });
}

// ───────────── Biblioteca de programas (IndexedDB, fica no aparelho) ─────────────
// Cada registro: { path: "Pasta/Sub/arquivo.nc", kind: "file"|"folder", text, size, modified }

let dbPromise = null;
function db() {
    if (!dbPromise) {
        dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open('casillas-code', 1);
            req.onupgradeneeded = () => req.result.createObjectStore('library', { keyPath: 'path' });
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
    }
    return dbPromise;
}

async function tx(mode, work) {
    const d = await db();
    return new Promise((resolve, reject) => {
        const t = d.transaction('library', mode);
        const store = t.objectStore('library');
        let result;
        Promise.resolve(work(store)).then(r => { result = r; });
        t.oncomplete = () => resolve(result);
        t.onerror = () => reject(t.error);
    });
}

function req(r) {
    return new Promise((resolve, reject) => { r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error); });
}

/** Lista tudo (sem o texto): JSON [{path, kind, size, modified}]. */
export async function libList() {
    const all = await tx('readonly', store => req(store.getAll()));
    const when = ms => {
        try { return new Date(ms).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' }); }
        catch { return ''; }
    };
    return JSON.stringify((all || []).map(e => ({ path: e.path, kind: e.kind, size: e.size || 0, when: e.modified ? when(e.modified) : '' })));
}

/** Texto de um arquivo (ou null). */
export async function libRead(path) {
    const e = await tx('readonly', store => req(store.get(path)));
    return e && e.kind === 'file' ? e.text : null;
}

/** Grava (cria ou substitui) um arquivo. Cria as pastas do caminho. */
export async function libWrite(path, text) {
    const now = Date.now();
    await tx('readwrite', store => {
        const parts = path.split('/');
        for (let i = 1; i < parts.length; i++) {
            const folder = parts.slice(0, i).join('/');
            store.put({ path: folder, kind: 'folder', modified: now });
        }
        store.put({ path, kind: 'file', text, size: text.length, modified: now });
    });
    return true;
}

/** Cria uma pasta (vazia). */
export async function libMakeFolder(path) {
    await tx('readwrite', store => store.put({ path, kind: 'folder', modified: Date.now() }));
    return true;
}

/** Existe arquivo ou pasta com esse caminho? */
export async function libExists(path) {
    const e = await tx('readonly', store => req(store.get(path)));
    return !!e;
}

/** Exclui um arquivo, ou uma pasta com tudo dentro. */
export async function libDelete(path) {
    const all = await tx('readonly', store => req(store.getAllKeys()));
    await tx('readwrite', store => {
        for (const key of all) {
            if (key === path || key.startsWith(path + '/')) store.delete(key);
        }
    });
    return true;
}

/** Renomeia/move um arquivo ou pasta (com tudo dentro). Substitui o que já existir no destino. */
export async function libRename(from, to) {
    const all = await tx('readonly', store => req(store.getAll()));
    await tx('readwrite', store => {
        for (const e of all) {
            if (e.path === from || e.path.startsWith(from + '/')) {
                store.delete(e.path);
                store.put({ ...e, path: to + e.path.substring(from.length), modified: Date.now() });
            }
        }
    });
    return true;
}

/** Compartilha (Android: WhatsApp, Drive, e-mail...) ou baixa os arquivos. files = JSON [{name, text}]. */
export async function shareFiles(filesJson, title) {
    const items = JSON.parse(filesJson);
    const files = items.map(i => new File([i.text], i.name, { type: 'text/plain' }));
    try {
        if (navigator.canShare && navigator.canShare({ files })) {
            await navigator.share({ files, title: title || 'Programas CNC' });
            return 'shared';
        }
    } catch (e) {
        if (e && e.name === 'AbortError') return 'cancelled';
    }
    // Sem compartilhamento: baixa um por um (vão para a pasta Downloads)
    for (const f of files) {
        const url = URL.createObjectURL(f);
        const a = document.createElement('a');
        a.href = url;
        a.download = f.name;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 4000);
    }
    return 'downloaded';
}

// ───────────── Diálogos nativos (o teclado do celular funciona neles) ─────────────

function dialogShell(titleText) {
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:9998;display:flex;align-items:center;justify-content:center;' +
        'background:rgba(0,0,0,.55);padding:16px;font-family:system-ui,-apple-system,Roboto,sans-serif';
    const box = document.createElement('div');
    box.style.cssText = 'width:100%;max-width:420px;background:#161b22;color:#e6edf3;border:1px solid #30363d;' +
        'border-radius:14px;padding:18px;display:flex;flex-direction:column;gap:14px;box-shadow:0 12px 40px rgba(0,0,0,.5)';
    const title = document.createElement('div');
    title.textContent = titleText;
    title.style.cssText = 'font-weight:600;font-size:16px;line-height:1.35';
    box.appendChild(title);
    overlay.appendChild(box);
    return { overlay, box };
}

function dialogButtons(box, cancelText, okText, danger) {
    const row = document.createElement('div');
    row.style.cssText = 'display:flex;justify-content:flex-end;gap:8px';
    const mk = (t, primary) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = t;
        b.style.cssText = 'min-height:40px;padding:8px 16px;border-radius:9px;font-size:15px;cursor:pointer;' +
            (primary ? (danger ? 'background:#da3633;color:#fff;border:1px solid #da3633;font-weight:600'
                               : 'background:#f0883e;color:#0d1117;border:1px solid #f0883e;font-weight:600')
                     : 'background:transparent;color:#c9d1d9;border:1px solid #30363d');
        return b;
    };
    const cancel = mk(cancelText, false);
    const ok = mk(okText, true);
    row.append(cancel, ok);
    box.appendChild(row);
    return { cancel, ok };
}

/** Pede um texto (nome de arquivo/pasta). Resolve com o texto ou null. */
export function promptText(titleText, initial, okText) {
    return new Promise(resolve => {
        const { overlay, box } = dialogShell(titleText);
        const input = document.createElement('input');
        input.type = 'text';
        input.value = initial || '';
        input.setAttribute('autocapitalize', 'characters');
        input.setAttribute('autocorrect', 'off');
        input.spellcheck = false;
        input.style.cssText = 'font-size:16px;padding:10px 12px;border-radius:9px;border:1px solid #30363d;background:#0d1117;color:#e6edf3;outline:none';
        box.appendChild(input);
        const { cancel, ok } = dialogButtons(box, 'Cancelar', okText || 'OK', false);
        document.body.appendChild(overlay);
        const close = v => { overlay.remove(); resolve(v); };
        cancel.addEventListener('click', () => close(null));
        ok.addEventListener('click', () => close(input.value.trim() || null));
        input.addEventListener('keydown', e => { if (e.key === 'Enter') close(input.value.trim() || null); });
        input.focus();
        const dot = input.value.lastIndexOf('.');
        input.setSelectionRange(0, dot > 0 ? dot : input.value.length);
    });
}

/** Pergunta sim/não. Resolve com true/false. */
export function confirmBox(message, okText, danger) {
    return new Promise(resolve => {
        const { overlay, box } = dialogShell(message);
        const { cancel, ok } = dialogButtons(box, 'Cancelar', okText || 'OK', !!danger);
        document.body.appendChild(overlay);
        const close = v => { overlay.remove(); resolve(v); };
        cancel.addEventListener('click', () => close(false));
        ok.addEventListener('click', () => close(true));
    });
}

export function editMachiningSetup(json) { return import('./setup-ui.js').then(m => m.openSetup(json)); }
export function openGenerators(catalogJson, generate, lastJson) { return import('./generators-ui.js').then(m => m.openGenerators(catalogJson, generate, lastJson)); }
