// Folha de setup na versão web: mostra a folha (HTML gerado pelo núcleo) em tela cheia,
// com "PDF / Imprimir" (impressão do aparelho) e "Compartilhar" (gera um PDF A4 aqui mesmo).
// As bibliotecas de PDF (lib/) só são carregadas quando o usuário toca em Compartilhar.

const A4_WIDTH_PX = 794; // 210 mm a 96 dpi: a folha é desenhada na largura do papel

function loadScript(src) {
    return new Promise((resolve, reject) => {
        if (document.querySelector('script[data-src="' + src + '"]')) { resolve(); return; }
        const s = document.createElement('script');
        s.src = src;
        s.dataset.src = src;
        s.onload = () => resolve();
        s.onerror = () => reject(new Error('não foi possível carregar ' + src));
        document.head.appendChild(s);
    });
}

async function pdfLibraries() {
    await loadScript('./lib/html2canvas.min.js');
    await loadScript('./lib/jspdf.umd.min.js');
    return { html2canvas: window.html2canvas, jsPDF: window.jspdf.jsPDF };
}

/** Desenha a folha num iframe fora da tela, na largura do A4, e devolve o PDF (Blob). */
async function buildPdf(html) {
    const { html2canvas, jsPDF } = await pdfLibraries();
    const frame = document.createElement('iframe');
    frame.style.cssText = `position:fixed;left:-10000px;top:0;width:${A4_WIDTH_PX}px;height:1123px;border:0;background:#fff`;
    document.body.appendChild(frame);
    try {
        await new Promise(resolve => { frame.onload = resolve; frame.srcdoc = html; });
        const doc = frame.contentDocument;
        await new Promise(r => setTimeout(r, 150)); // fontes e SVG
        const body = doc.body;
        const height = Math.max(body.scrollHeight, doc.documentElement.scrollHeight);
        frame.style.height = height + 'px';
        const canvas = await html2canvas(body, {
            scale: 2, backgroundColor: '#ffffff', windowWidth: A4_WIDTH_PX, width: A4_WIDTH_PX, height,
            useCORS: false, logging: false,
        });

        // Fatia a imagem em páginas A4 (margem de 8 mm)
        const pdf = new jsPDF({ unit: 'mm', format: 'a4', orientation: 'portrait' });
        const margin = 8, pageW = 210 - 2 * margin, pageH = 297 - 2 * margin;
        const pxPerMm = canvas.width / pageW;
        const sliceH = Math.floor(pageH * pxPerMm);
        for (let y = 0, page = 0; y < canvas.height; y += sliceH, page++) {
            const h = Math.min(sliceH, canvas.height - y);
            const part = document.createElement('canvas');
            part.width = canvas.width;
            part.height = h;
            const ctx = part.getContext('2d');
            ctx.fillStyle = '#fff';
            ctx.fillRect(0, 0, part.width, h);
            ctx.drawImage(canvas, 0, y, canvas.width, h, 0, 0, canvas.width, h);
            if (page > 0) pdf.addPage();
            pdf.addImage(part.toDataURL('image/jpeg', 0.92), 'JPEG', margin, margin, pageW, h / pxPerMm);
        }
        return pdf.output('blob');
    } finally {
        frame.remove();
    }
}

async function sharePdf(blob, fileName) {
    const file = new File([blob], fileName, { type: 'application/pdf' });
    if (navigator.canShare && navigator.canShare({ files: [file] })) {
        try {
            await navigator.share({ files: [file], title: fileName });
            return 'shared';
        } catch (e) {
            if (e && e.name === 'AbortError') return 'cancelled';
            // O PDF demorou e o navegador "esqueceu" o toque: pede mais um toque para enviar
            if (e && e.name === 'NotAllowedError') return 'needs-tap';
        }
    }
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
    return 'downloaded';
}

/**
 * Mostra a folha de setup em tela cheia. Resolve quando o usuário volta.
 * fileBase: nome do PDF sem extensão (ex.: "Setup_04_G71").
 */
export function showSetupSheet(html, fileBase) {
    return new Promise(resolve => {
        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;inset:0;z-index:9997;display:flex;flex-direction:column;background:#30363d;' +
            'font-family:system-ui,-apple-system,Roboto,sans-serif';

        const bar = document.createElement('div');
        bar.style.cssText = 'display:flex;gap:8px;align-items:center;padding:8px 10px;padding-top:calc(env(safe-area-inset-top,0px) + 8px);' +
            'background:#161b22;border-bottom:1px solid #30363d';
        const mk = (text, primary) => {
            const b = document.createElement('button');
            b.type = 'button';
            b.textContent = text;
            b.style.cssText = 'min-height:40px;padding:8px 14px;border-radius:9px;font-size:15px;cursor:pointer;' +
                (primary ? 'background:#f0883e;color:#0d1117;border:1px solid #f0883e;font-weight:600'
                         : 'background:transparent;color:#e6edf3;border:1px solid #30363d');
            return b;
        };
        const back = mk('← Voltar', false);
        const spacer = document.createElement('div');
        spacer.style.flex = '1';
        const print = mk('PDF / Imprimir', false);
        const share = mk('Compartilhar', true);
        bar.append(back, spacer, print, share);

        const status = document.createElement('div');
        status.style.cssText = 'display:none;padding:8px 12px;font-size:14px;background:#161b22;color:#ffd8b5;border-bottom:1px solid #30363d';

        const frame = document.createElement('iframe');
        frame.style.cssText = 'flex:1;width:100%;border:0;background:#fff';
        frame.srcdoc = html;

        overlay.append(bar, status, frame);
        document.body.appendChild(overlay);

        const say = text => { status.textContent = text; status.style.display = text ? 'block' : 'none'; };
        const close = () => { overlay.remove(); resolve('closed'); };

        back.addEventListener('click', close);
        print.addEventListener('click', () => {
            say('Na impressão, escolha "Salvar como PDF" para guardar o arquivo.');
            try { frame.contentWindow.focus(); frame.contentWindow.print(); } catch { say('Este navegador não deixou imprimir daqui.'); }
        });
        const fileName = (fileBase || 'Folha_de_setup') + '.pdf';
        let ready = null; // PDF já gerado, esperando o toque para enviar
        share.addEventListener('click', async () => {
            share.disabled = true; print.disabled = true;
            try {
                if (!ready) {
                    say('Gerando o PDF…');
                    ready = await buildPdf(html);
                }
                const result = await sharePdf(ready, fileName);
                if (result === 'needs-tap') {
                    share.textContent = 'Enviar PDF';
                    say('PDF pronto: toque em "Enviar PDF".');
                    return;
                }
                share.textContent = 'Compartilhar';
                say(result === 'downloaded' ? 'PDF baixado (pasta Downloads do aparelho).' : '');
            } catch (e) {
                say('Não foi possível gerar o PDF: ' + (e && e.message || e));
            } finally {
                share.disabled = false; print.disabled = false;
            }
        });
    });
}
