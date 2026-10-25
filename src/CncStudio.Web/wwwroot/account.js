// Conta do Casillas Code (Supabase): entrar, criar conta, recuperar senha, situação da licença
// e "Sair da conta". Os formulários são HTML nativo (o teclado do celular funciona neles).
//
// Guardado no navegador:
//   cc.auth    — sessão (tokens) da conta
//   cc.device  — identificador deste aparelho (sorteado uma vez)
//   cc.license — última situação confirmada pelo servidor (vale 7 dias sem internet)

const SUPABASE_URL = 'https://maayjshlsxvxtrgjpcep.supabase.co'; // projeto "Casillas": conta única da plataforma
const SUPABASE_KEY = 'sb_publishable_1p4-6-tNmiBhz0edN-UQDQ_sbwJwABo'; // chave pública (pode ficar no app)
const OFFLINE_DAYS = 7;
const SUPPORT_EMAIL = 'casillas.usinagem.br@gmail.com';

// ───────────── Armazenamento ─────────────

function load(key) {
    try { return JSON.parse(localStorage.getItem(key) || 'null'); } catch { return null; }
}

function save(key, value) {
    try {
        if (value === null) localStorage.removeItem(key);
        else localStorage.setItem(key, JSON.stringify(value));
    } catch { }
}

function deviceId() {
    // Prévias de teste (<ramo>.casillas-code.pages.dev) são um endereço novo a cada ramo: dividem um só aparelho,
    // para não gastar as vagas da conta.
    const h = location.hostname;
    if (h.endsWith('.casillas-code.pages.dev')) return 'previa-web';
    let id = load('cc.device');
    if (!id) {
        id = (crypto.randomUUID ? crypto.randomUUID() : 'd' + Date.now().toString(36) + Math.random().toString(36).slice(2, 12));
        save('cc.device', id);
    }
    return id;
}

function deviceLabel() {
    const ua = navigator.userAgent || '';
    const os = /Android/i.test(ua) ? 'Android' : /iPhone|iPad/i.test(ua) ? 'iPhone/iPad' : /Windows/i.test(ua) ? 'Windows'
        : /Mac OS/i.test(ua) ? 'Mac' : /Linux/i.test(ua) ? 'Linux' : 'Navegador';
    const installed = window.matchMedia && window.matchMedia('(display-mode: standalone)').matches;
    return os + (installed ? ' (app instalado)' : ' (navegador)');
}

// ───────────── API ─────────────

class NetworkError extends Error { }

async function call(path, { method = 'POST', body, token } = {}) {
    let response;
    try {
        response = await fetch(SUPABASE_URL + path, {
            method,
            headers: {
                'apikey': SUPABASE_KEY,
                'Content-Type': 'application/json',
                ...(token ? { 'Authorization': 'Bearer ' + token } : {}),
            },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
    } catch (e) {
        throw new NetworkError('sem conexão');
    }

    const text = await response.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = text; }
    if (!response.ok) {
        const message = (data && (data.msg || data.error_description || data.message || data.error)) || ('erro ' + response.status);
        const error = new Error(String(message));
        error.status = response.status;
        error.code = data && (data.error_code || data.code);
        throw error;
    }
    return data;
}

function storeSession(data, email) {
    const session = {
        access_token: data.access_token,
        refresh_token: data.refresh_token,
        expires_at: data.expires_at || Math.floor(Date.now() / 1000) + (data.expires_in || 3600),
        email: email || (data.user && data.user.email) || '',
    };
    save('cc.auth', session);
    return session;
}

/** Sessão válida (renova o token se estiver para vencer). Sem internet, devolve a guardada. */
async function session() {
    const s = load('cc.auth');
    if (!s) return null;
    if (s.expires_at - 60 > Date.now() / 1000) return s;
    try {
        const data = await call('/auth/v1/token?grant_type=refresh_token', { body: { refresh_token: s.refresh_token } });
        return storeSession(data, s.email);
    } catch (e) {
        if (e instanceof NetworkError) return { ...s, offline: true };
        save('cc.auth', null); // sessão revogada ou expirada de vez
        return null;
    }
}

function returnUrl() {
    return location.origin + location.pathname;
}

/** Volta do link do e-mail (confirmação de conta ou nova senha): os tokens vêm no endereço. */
let recoveryPending = false;
async function consumeLinkFromEmail() {
    const hash = location.hash || '';
    if (hash.indexOf('access_token=') < 0 && hash.indexOf('error=') < 0) return;
    const params = new URLSearchParams(hash.slice(1));
    history.replaceState(null, '', location.pathname + location.search);
    if (params.get('error')) return;
    const data = {
        access_token: params.get('access_token'),
        refresh_token: params.get('refresh_token'),
        expires_at: Number(params.get('expires_at')) || undefined,
        expires_in: Number(params.get('expires_in')) || 3600,
    };
    let email = '';
    try { email = (await call('/auth/v1/user', { method: 'GET', token: data.access_token })).email; } catch { }
    storeSession(data, email);
    if (params.get('type') === 'recovery') recoveryPending = true;
}

// ───────────── Situação da conta ─────────────

/** Converte a resposta da plataforma para o formato que o app já usa (módulo Code) e guarda todos os módulos. */
function fromPlatform(raw) {
    const mods = raw.modules || {};
    const out = {
        modules: mods,
        trial_ends_at: raw.trial && raw.trial.ends_at,
        max_devices: raw.devices && raw.devices.limit,
        devices_used: raw.devices && raw.devices.used,
    };
    return { ...out, ...codeView(mods.code, out.trial_ends_at) };
}

function codeView(m, trialEnds) {
    if (!m) return { state: 'expired' };
    if (m.state === 'VALID') return { state: 'licensed', kind: m.valid_until ? 'subscription' : 'perpetual', expires_at: m.valid_until, updates_until: m.updates_until };
    if (m.state === 'TRIAL') return { state: 'trial', trial_ends_at: m.valid_until || trialEnds };
    if (m.state === 'DEVICE_LIMIT') return { state: 'device_limit' };
    return { state: 'expired' };
}

/** Sem internet: reavalia as datas guardadas (teste e assinaturas vencem mesmo offline). */
function recompute(cached) {
    const now = Date.now();
    const mods = {};
    for (const [slug, m] of Object.entries(cached.modules || {})) {
        let st = m.state;
        if ((st === 'TRIAL' || st === 'VALID') && m.valid_until && Date.parse(m.valid_until) < now) st = 'EXPIRED';
        mods[slug] = { ...m, state: st, has_access: st === 'TRIAL' || st === 'VALID' };
    }
    return { ...cached, modules: mods, ...codeView(mods.code, cached.trial_ends_at) };
}

/** Estado de um módulo da plataforma na última situação conhecida: VALID | TRIAL | EXPIRED | DEVICE_LIMIT | null (sem conta). */
export function moduleState(slug) {
    const c = load('cc.license');
    if (!c || !c.modules) return null;
    const m = recompute(c).modules[slug];
    return m ? m.state : null;
}


/**
 * Situação da conta neste aparelho (JSON):
 * { signedIn, email, state, kind, expires_at, updates_until, trial_ends_at, max_devices, devices_used,
 *   offline, recovery }
 * state = licensed | trial | expired | device_limit | device_revoked
 */
export async function accountCheck() {
    await consumeLinkFromEmail();
    const s = await session();
    if (!s) return JSON.stringify({ signedIn: false });

    const base = { signedIn: true, email: s.email, recovery: recoveryPending };
    try {
        if (s.offline) throw new NetworkError('sem conexão');
        const raw = await call('/rest/v1/rpc/platform_access_v1', {
            token: s.access_token,
            body: { p_device_id: deviceId() },
        });
        const status = fromPlatform(raw);
        save('cc.license', { ...status, checked_at: Date.now(), email: s.email });
        return JSON.stringify({ ...base, ...status, offline: false });
    } catch (e) {
        if (!(e instanceof NetworkError)) {
            if (e.status === 401) { save('cc.auth', null); return JSON.stringify({ signedIn: false }); }
            return JSON.stringify({ ...base, state: 'expired', error: e.message, offline: false });
        }
        // Sem internet: vale a última situação confirmada, por até 7 dias
        const cached = load('cc.license');
        if (cached && Date.now() - cached.checked_at < OFFLINE_DAYS * 86400000) {
            return JSON.stringify({ ...base, ...recompute(cached), offline: true });
        }
        return JSON.stringify({ ...base, state: 'expired', offline: true });
    }
}

// ───────────── Telas (HTML nativo) ─────────────

const COLORS = { bg: '#161b22', field: '#0d1117', text: '#e6edf3', soft: '#8b949e', line: '#30363d', brand: '#f0883e', danger: '#da3633', ok: '#3fb950' };

function shell(titleText) {
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;z-index:9998;display:flex;align-items:center;justify-content:center;' +
        'background:rgba(0,0,0,.6);padding:16px;font-family:system-ui,-apple-system,Roboto,sans-serif;overflow:auto';
    const box = document.createElement('div');
    box.style.cssText = `width:100%;max-width:400px;background:${COLORS.bg};color:${COLORS.text};border:1px solid ${COLORS.line};` +
        'border-radius:14px;padding:20px;display:flex;flex-direction:column;gap:12px;box-shadow:0 12px 40px rgba(0,0,0,.5);margin:auto';
    const logo = document.createElement('img');
    logo.src = './assinatura.svg';
    logo.alt = 'Casillas Code';
    logo.style.cssText = 'height:34px;align-self:center;margin-bottom:2px';
    const title = document.createElement('div');
    title.style.cssText = 'font-weight:600;font-size:17px;text-align:center';
    title.textContent = titleText;
    box.append(logo, title);
    overlay.appendChild(box);
    document.body.appendChild(overlay);
    return { overlay, box, title };
}

function field(box, label, type, autocomplete) {
    const wrap = document.createElement('label');
    wrap.style.cssText = `display:flex;flex-direction:column;gap:4px;font-size:13px;color:${COLORS.soft}`;
    wrap.textContent = label;
    const input = document.createElement('input');
    input.type = type;
    if (autocomplete) input.autocomplete = autocomplete;
    input.setAttribute('autocapitalize', 'off');
    input.setAttribute('autocorrect', 'off');
    input.spellcheck = false;
    input.style.cssText = `font-size:16px;padding:11px 12px;border-radius:9px;border:1px solid ${COLORS.line};` +
        `background:${COLORS.field};color:${COLORS.text};outline:none`;
    wrap.appendChild(input);
    box.appendChild(wrap);
    return { wrap, input };
}

function button(box, text, kind) {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = text;
    const styles = {
        primary: `background:${COLORS.brand};color:#0d1117;border:1px solid ${COLORS.brand};font-weight:600`,
        danger: `background:transparent;color:#ff7b72;border:1px solid ${COLORS.danger}`,
        normal: `background:transparent;color:${COLORS.text};border:1px solid ${COLORS.line}`,
        link: `background:transparent;color:${COLORS.soft};border:0;text-decoration:underline;min-height:32px`,
    };
    b.style.cssText = 'min-height:44px;padding:8px 16px;border-radius:9px;font-size:15px;cursor:pointer;' + styles[kind || 'normal'];
    box.appendChild(b);
    return b;
}

function note(box) {
    const n = document.createElement('div');
    n.style.cssText = 'font-size:14px;line-height:1.4;min-height:0;display:none;padding:10px 12px;border-radius:9px';
    box.appendChild(n);
    return {
        show(text, kind) {
            n.textContent = text;
            n.style.display = text ? 'block' : 'none';
            n.style.background = kind === 'ok' ? 'rgba(63,185,80,.12)' : 'rgba(218,54,51,.14)';
            n.style.color = kind === 'ok' ? '#7ee787' : '#ffa198';
        },
    };
}

function translate(error) {
    const m = (error && error.message || '').toLowerCase();
    if (error instanceof NetworkError) return 'Sem conexão com a internet. Tente de novo quando estiver on-line.';
    if (m.includes('invalid login')) return 'E-mail ou senha incorretos.';
    if (m.includes('not confirmed')) return 'Falta confirmar o e-mail: abra a mensagem que enviamos e toque no link.';
    if (m.includes('already registered') || m.includes('already been registered')) return 'Este e-mail já tem conta. Use "Entrar".';
    if (m.includes('password should be') || m.includes('weak')) return 'A senha precisa ter pelo menos 6 caracteres.';
    if (m.includes('rate limit') || m.includes('too many') || (error && error.status === 429))
        return 'Muitas tentativas seguidas. Espere alguns minutos e tente de novo.';
    if (m.includes('valid email') || m.includes('invalid email')) return 'Confira o e-mail digitado.';
    if (m.includes('same password') || m.includes('different from the old')) return 'A nova senha precisa ser diferente da anterior.';
    return 'Não deu certo: ' + (error && error.message || 'erro desconhecido');
}

/** Linha com links para os termos de uso e a política de privacidade. */
function legalLinks(box, prefix) {
    const p = document.createElement('div');
    p.style.cssText = `font-size:12px;color:${COLORS.soft};text-align:center;line-height:1.5`;
    const link = (href, text) => {
        const a = document.createElement('a');
        a.href = href;
        a.target = '_blank';
        a.rel = 'noopener';
        a.textContent = text;
        a.style.color = '#58a6ff';
        return a;
    };
    p.append(prefix, link('./termos.html', 'Termos de uso'), ' e a ', link('./privacidade.html', 'Política de privacidade'), '.');
    box.appendChild(p);
    return p;
}

function busy(buttons, on) {
    for (const b of buttons) { b.disabled = on; b.style.opacity = on ? '.6' : '1'; }
}

/**
 * Tela de entrada. Resolve com 'ok' (entrou) ou 'guest' (usar sem conta).
 * Modos: entrar, criar conta, esqueci a senha.
 */
export function accountLogin(reason) {
    return new Promise(resolve => {
        const { overlay, box, title } = shell('Entrar');
        if (reason) {
            const r = document.createElement('div');
            r.style.cssText = `font-size:14px;color:${COLORS.soft};text-align:center;line-height:1.4`;
            r.textContent = reason;
            box.appendChild(r);
        }
        const name = field(box, 'Nome', 'text', 'name');
        name.input.setAttribute('autocapitalize', 'words');
        const email = field(box, 'E-mail', 'email', 'email');
        const pass = field(box, 'Senha', 'password', 'current-password');
        const pass2 = field(box, 'Repita a senha', 'password', 'new-password');
        const message = note(box);
        const main = button(box, 'Entrar', 'primary');
        const swap = button(box, 'Criar conta (30 dias grátis)', 'normal');
        const forgot = button(box, 'Esqueci a senha', 'link');
        const guest = button(box, 'Usar sem conta (sem simular, salvar nem exportar)', 'link');
        const consent = legalLinks(box, 'Ao criar a conta, você concorda com os ');
        let mode = 'login';

        const last = load('cc.lastEmail');
        if (last) email.input.value = last;

        function setMode(m) {
            mode = m;
            message.show('');
            name.wrap.style.display = m === 'signup' ? 'flex' : 'none';
            pass.wrap.style.display = m === 'forgot' ? 'none' : 'flex';
            pass2.wrap.style.display = m === 'signup' ? 'flex' : 'none';
            pass.input.autocomplete = m === 'signup' ? 'new-password' : 'current-password';
            title.textContent = m === 'login' ? 'Entrar' : m === 'signup' ? 'Criar conta' : 'Recuperar senha';
            main.textContent = m === 'login' ? 'Entrar' : m === 'signup' ? 'Criar conta' : 'Enviar link por e-mail';
            swap.textContent = m === 'login' ? 'Criar conta (30 dias grátis)' : 'Já tenho conta — entrar';
            forgot.style.display = m === 'login' ? 'block' : 'none';
            consent.firstChild.textContent = m === 'signup' ? 'Ao criar a conta, você concorda com os ' : 'Veja os ';
        }
        setMode('login');

        const close = value => { overlay.remove(); resolve(value); };

        async function submit() {
            const e = email.input.value.trim().toLowerCase();
            const p = pass.input.value;
            if (!/^\S+@\S+\.\S+$/.test(e)) { message.show('Digite um e-mail válido.'); email.input.focus(); return; }
            save('cc.lastEmail', e);
            busy([main, swap, forgot, guest], true);
            try {
                if (mode === 'login') {
                    if (!p) { message.show('Digite a senha.'); return; }
                    const data = await call('/auth/v1/token?grant_type=password', { body: { email: e, password: p } });
                    storeSession(data, e);
                    close('ok');
                } else if (mode === 'signup') {
                    if (p.length < 6) { message.show('A senha precisa ter pelo menos 6 caracteres.'); return; }
                    if (p !== pass2.input.value) { message.show('As duas senhas não são iguais.'); return; }
                    const data = await call('/auth/v1/signup?redirect_to=' + encodeURIComponent(returnUrl()), {
                        body: { email: e, password: p, data: { full_name: name.input.value.trim() } },
                    });
                    if (data && data.access_token) { storeSession(data, e); close('ok'); return; }
                    setMode('login');
                    message.show('Conta criada! Enviamos um link de confirmação para ' + e +
                        '. Abra o e-mail (veja também o spam) e toque no link: o app abre já com a conta.', 'ok');
                } else {
                    await call('/auth/v1/recover?redirect_to=' + encodeURIComponent(returnUrl()), { body: { email: e } });
                    setMode('login');
                    message.show('Se houver conta com ' + e + ', enviamos um link para criar uma senha nova.', 'ok');
                }
            } catch (err) {
                message.show(translate(err));
            } finally {
                busy([main, swap, forgot, guest], false);
            }
        }

        main.addEventListener('click', submit);
        for (const f of [email, pass, pass2]) f.input.addEventListener('keydown', ev => { if (ev.key === 'Enter') submit(); });
        swap.addEventListener('click', () => setMode(mode === 'login' ? 'signup' : 'login'));
        forgot.addEventListener('click', () => setMode('forgot'));
        guest.addEventListener('click', () => close('guest'));
    });
}

/** Depois do link de "Esqueci a senha": pede a senha nova. Resolve com true se trocou. */
export function accountNewPassword() {
    recoveryPending = false;
    return new Promise(resolve => {
        const { overlay, box } = shell('Criar senha nova');
        const pass = field(box, 'Senha nova', 'password', 'new-password');
        const pass2 = field(box, 'Repita a senha', 'password', 'new-password');
        const message = note(box);
        const ok = button(box, 'Salvar senha', 'primary');
        const cancel = button(box, 'Agora não', 'link');
        const close = v => { overlay.remove(); resolve(v); };
        ok.addEventListener('click', async () => {
            if (pass.input.value.length < 6) { message.show('A senha precisa ter pelo menos 6 caracteres.'); return; }
            if (pass.input.value !== pass2.input.value) { message.show('As duas senhas não são iguais.'); return; }
            busy([ok, cancel], true);
            try {
                const s = await session();
                await call('/auth/v1/user', { method: 'PUT', token: s && s.access_token, body: { password: pass.input.value } });
                close(true);
            } catch (err) {
                message.show(translate(err));
            } finally {
                busy([ok, cancel], false);
            }
        });
        cancel.addEventListener('click', () => close(false));
        pass.input.focus();
    });
}

function date(iso) {
    try { return new Date(iso).toLocaleDateString('pt-BR'); } catch { return iso; }
}

function daysLeft(iso) {
    return Math.max(0, Math.ceil((Date.parse(iso) - Date.now()) / 86400000));
}

/** Frase da situação (também usada no topo do app). */
export function accountSummary(json) {
    const s = JSON.parse(json || '{}');
    if (!s.signedIn) return 'sem conta';
    switch (s.state) {
        case 'licensed':
            return s.kind === 'perpetual' ? 'licença vitalícia' : 'assinatura até ' + date(s.expires_at);
        case 'trial': {
            const d = daysLeft(s.trial_ends_at);
            return 'teste: ' + (d <= 1 ? 'último dia' : d + ' dias');
        }
        case 'device_limit': return 'limite de aparelhos';
        case 'device_revoked': return 'aparelho bloqueado';
        default: return 'teste encerrado';
    }
}

/**
 * Painel "Conta": situação, aparelhos e "Sair da conta".
 * Resolve com 'signout', 'login' ou 'close'.
 */
export function accountPanel(json) {
    const s = JSON.parse(json || '{}');
    return new Promise(resolve => {
        const { overlay, box } = shell('Conta');
        const close = v => { overlay.remove(); resolve(v); };
        const line = (label, value, strong) => {
            const row = document.createElement('div');
            row.style.cssText = `display:flex;justify-content:space-between;gap:12px;font-size:14px;border-bottom:1px solid ${COLORS.line};padding:8px 0`;
            const a = document.createElement('span'); a.textContent = label; a.style.color = COLORS.soft;
            const b = document.createElement('span'); b.textContent = value; b.style.textAlign = 'right';
            if (strong) b.style.fontWeight = '600';
            row.append(a, b);
            box.appendChild(row);
        };

        if (!s.signedIn) {
            const t = document.createElement('div');
            t.style.cssText = `font-size:14px;color:${COLORS.soft};text-align:center;line-height:1.45`;
            t.textContent = 'Sem conta o app abre e edita, mas não simula, salva nem exporta. ' +
                'Crie uma conta para testar todos os módulos por 30 dias.';
            box.appendChild(t);
            button(box, 'Entrar ou criar conta', 'primary').addEventListener('click', () => close('login'));
            button(box, 'Fechar', 'normal').addEventListener('click', () => close('close'));
            return;
        }

        line('E-mail', s.email || '—');
        line('Situação', accountSummary(json), true);
        if (s.state === 'licensed' && s.kind === 'perpetual' && s.updates_until) line('Atualizações até', date(s.updates_until));
        if (s.max_devices) line('Aparelhos em uso', (s.devices_used || 0) + ' de ' + s.max_devices);
        if (s.offline) line('Conexão', 'sem internet (última confirmação guardada)');

        const info = document.createElement('div');
        info.style.cssText = `font-size:13px;color:${COLORS.soft};line-height:1.45`;
        info.textContent =
            s.state === 'device_limit' ? 'Esta conta já está em uso no número máximo de aparelhos. Toque em "Sair da conta" em um deles para liberar a vaga.'
          : s.state === 'device_revoked' ? 'Este aparelho foi bloqueado para esta conta. Fale com o suporte: ' + SUPPORT_EMAIL
          : s.state === 'expired' ? 'O período de teste terminou: o app continua abrindo e editando, mas simular, salvar e exportar precisam de licença.'
          : 'Sair da conta libera a vaga deste aparelho para usar em outro.';
        box.appendChild(info);

        const support = document.createElement('a');
        support.href = 'mailto:' + SUPPORT_EMAIL + '?subject=' + encodeURIComponent('Casillas Code - ' + (s.email || ''));
        support.textContent = 'Suporte e licenças: ' + SUPPORT_EMAIL;
        support.style.cssText = `font-size:13px;color:#58a6ff;text-align:center;word-break:break-all`;
        box.appendChild(support);


        // Ativar um código de licença (módulo ou pacote completo)
        const code = field(box, 'Tem um código de licença?', 'text', 'off');
        code.input.placeholder = 'XXXX-XXXX-XXXX-XXXX';
        code.input.style.textTransform = 'uppercase';
        const msg = note(box);
        const go = button(box, 'Ativar código', 'normal');
        go.addEventListener('click', async () => {
            const value = code.input.value.trim();
            if (!value) { msg.show('Digite o código recebido na compra.', 'err'); return; }
            go.disabled = true;
            try {
                const fresh = await session();
                if (!fresh || fresh.offline) throw new NetworkError('sem conexão');
                const r = await call('/rest/v1/rpc/platform_activate_v1', {
                    token: fresh.access_token, body: { p_license_code: value, p_device_id: deviceId() } });
                const texts = {
                    ACTIVATION_DENIED: 'Código não encontrado ou já usado. Confira os caracteres.',
                    INVALID_REQUEST: 'Código em formato inválido.',
                    ALREADY_ACTIVE: 'Você já tem licença vitalícia deste módulo.',
                };
                if (r.result === 'SUCCESS') { msg.show('Código ativado! Atualizando…', 'ok'); setTimeout(() => close('refresh'), 900); return; }
                if (r.result === 'RATE_LIMITED') msg.show('Muitas tentativas. Tente de novo em ' + Math.ceil((r.retry_after_seconds || 60) / 60) + ' min.', 'err');
                else msg.show(texts[r.result] || 'Não foi possível ativar o código.', 'err');
            } catch (e) { msg.show(translate(e), 'err'); }
            go.disabled = false;
        });

        button(box, 'Sair da conta', 'danger').addEventListener('click', () => close('signout'));
        legalLinks(box, 'Veja os ');
        button(box, 'Fechar', 'normal').addEventListener('click', () => close('close'));
    });
}

/** Sair da conta: libera a vaga deste aparelho e apaga a sessão guardada. */
export async function accountSignOut() {
    const s = load('cc.auth');
    if (s) {
        try {
            const fresh = await session();
            if (fresh && !fresh.offline) {
                await call('/rest/v1/rpc/platform_release_device_v1', { token: fresh.access_token, body: { p_device_id: deviceId() } });
                await call('/auth/v1/logout', { token: fresh.access_token }).catch(() => { });
            }
        } catch { }
    }
    save('cc.auth', null);
    save('cc.license', null);
    return true;
}
