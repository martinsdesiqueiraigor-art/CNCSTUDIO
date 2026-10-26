// Casillas Loja (teste): catálogo simples e pedido pelo WhatsApp. Sem pagamento no app, sem preço fixo no código.
const WHATS = '5519996816755';

export async function openStore() {
  const make = (tag, text, style) => { const el = document.createElement(tag); if (text != null) el.textContent = text; if (style) el.style.cssText = style; return el; };
  const btn = (label, bg, color, onClick) => {
    const b = make('button', label, `min-height:46px;padding:0 16px;border:1px solid ${bg};border-radius:10px;background:${bg};color:${color};font:700 15px system-ui,sans-serif;cursor:pointer`);
    b.type = 'button'; b.addEventListener('click', onClick); return b;
  };

  const root = make('div', null, 'position:fixed;left:0;right:0;top:0;bottom:var(--dock-h,0px);z-index:10030;background:#0d1117;color:#e6edf3;overflow-y:auto;overscroll-behavior:contain;font:15px/1.4 system-ui,sans-serif;padding:12px 12px 90px');
  const wrap = make('div', null, 'max-width:680px;margin:0 auto');
  root.append(wrap);
  document.body.append(root);

  const finish = () => {
    window.removeEventListener('casillas:close-overlays', finish);
    root.remove();
    bar.remove();
    window.dispatchEvent(new CustomEvent('casillas:module-closed', { detail: 'loja' }));
  };
  window.addEventListener('casillas:close-overlays', finish);

  const head = make('div', null, 'display:flex;align-items:center;gap:10px;margin-bottom:10px');
  head.append(btn('Fechar', '#30363d', '#e6edf3', finish), make('h2', 'Casillas Loja', 'margin:0;font-size:19px;flex:1;color:#2fd0c8'));
  wrap.append(head);

  let data;
  try {
    const r = await fetch('./loja/catalogo.json', { cache: 'no-cache' });
    data = await r.json();
  } catch {
    wrap.append(make('div', 'Não foi possível carregar o catálogo. Confira a internet e tente de novo.', 'color:#ff9a83'));
    return;
  }

  wrap.append(make('div', data.aviso, 'border:1px solid #7a6122;background:#3b2f12;color:#f3c969;border-radius:10px;padding:10px;margin-bottom:12px;font-size:13px'));

  const picked = new Set();
  const bar = make('div', null, 'position:fixed;left:0;right:0;bottom:var(--dock-h,0px);z-index:10031;display:none;padding:10px 12px;background:#161b22;border-top:1px solid #303d4b');
  const order = btn('', '#2fd0c8', '#0d1117', () => {
    const lines = data.itens.filter(i => picked.has(i.id)).map(i => '• ' + i.nome);
    const text = 'Olá! Tenho interesse nestes itens da Casillas Loja:\n' + lines.join('\n') + '\n\nPode me passar preço e prazo?';
    window.open('https://wa.me/' + WHATS + '?text=' + encodeURIComponent(text), '_blank', 'noopener');
  });
  order.style.width = '100%';
  bar.append(order);
  document.body.append(bar);
  const refresh = () => {
    bar.style.display = picked.size ? 'block' : 'none';
    order.textContent = 'Pedir pelo WhatsApp (' + picked.size + ')';
  };

  for (const item of data.itens) {
    const card = make('div', null, 'border:1px solid #30363d;border-radius:12px;padding:14px;margin-bottom:12px;background:#131a22');
    card.append(make('div', item.categoria, 'font-size:11px;font-weight:700;color:#2fd0c8;text-transform:uppercase;letter-spacing:.04em'));
    card.append(make('h3', item.nome, 'margin:2px 0 4px;font-size:18px'));
    card.append(make('div', item.resumo, 'color:#9baab9;margin-bottom:8px'));
    card.append(make('div', item.descricao, 'font-size:13px;color:#c9d6e2;margin-bottom:10px'));
    const table = make('div', null, 'display:grid;grid-template-columns:1fr 1fr;gap:4px 10px;font-size:13px;margin-bottom:10px');
    for (const [k, v] of item.specs) {
      table.append(make('span', k, 'color:#9baab9'), make('span', v, v.startsWith('A definir') ? 'color:#f3c969' : ''));
    }
    card.append(table);
    card.append(make('div', 'Preço: ' + item.preco, 'font-weight:700;margin-bottom:10px'));
    const pick = btn('Adicionar ao pedido', '#30363d', '#e6edf3', () => {
      if (picked.has(item.id)) picked.delete(item.id); else picked.add(item.id);
      pick.textContent = picked.has(item.id) ? '✓ No pedido (tocar para tirar)' : 'Adicionar ao pedido';
      pick.style.background = picked.has(item.id) ? '#1f6f6a' : '#30363d';
      refresh();
    });
    pick.style.width = '100%';
    card.append(pick);
    wrap.append(card);
  }
}
