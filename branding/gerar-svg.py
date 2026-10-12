"""Gera os SVGs da marca Casillas Code nesta pasta (branding/).

Símbolo: "C" laranja do Casillas + pastilha azul + linhas de código.
Rodar dentro de branding/:  python gerar-svg.py
"""
OR1, OR2 = "#ff9a3c", "#e8661a"     # laranja (degradê sutil)
BL = "#2f81f7"                      # azul da pastilha e das linhas
BL_D = "#58a6ff"                    # azul do "Code" no fundo escuro

def defs(): return (f'<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">'
                    f'<stop offset="0" stop-color="{OR1}"/><stop offset="1" stop-color="{OR2}"/></linearGradient></defs>')

def symbol(mono=None, small=False):
    c = "url(#g)" if not mono else mono
    b = BL if not mono else mono
    o = "#f0883e" if not mono else mono
    p = [f'<path d="M137.5,60.1 A62,62 0 1 0 137.5,139.9" fill="none" stroke="{c}" stroke-width="36"/>',
         f'<polygon points="150,82 168,100 150,118 132,100" fill="{b}"/>']
    if small:  # ícone pequeno: 2 linhas grossas
        p += [f'<rect x="178" y="80" width="30" height="13" rx="6.5" fill="{o}"/>',
              f'<rect x="178" y="107" width="44" height="13" rx="6.5" fill="{b}"/>']
    else:
        p += [f'<rect x="178" y="72" width="24" height="9" rx="4.5" fill="{o}"/>',
              f'<rect x="178" y="95.5" width="46" height="9" rx="4.5" fill="{b}"/>',
              f'<rect x="178" y="119" width="34" height="9" rx="4.5" fill="{b}"/>']
    return "".join(p)

def svg(inner, w, h, vb): return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{vb}">{defs()}{inner}</svg>\n'

def wordmark(fg, sub, code, mono=None):
    return (f'<g transform="translate(-6,0)">{symbol(mono)}</g>'
            f'<text x="250" y="118" font-family="Inter" font-weight="800" font-size="88" fill="{mono or fg}" letter-spacing="-1.5">Casillas'
            f'<tspan fill="{mono or code}"> Code</tspan></text>'
            f'<text x="254" y="160" font-family="Inter" font-weight="500" font-size="20" fill="{mono or sub}" letter-spacing="6.6">EDITOR E SIMULADOR CNC</text>')

W = 820
files = {
 "simbolo.svg": svg(symbol(), 236, 200, "0 0 236 200"),
 "simbolo-mono-escuro.svg": svg(symbol("#0d1117"), 236, 200, "0 0 236 200"),
 "simbolo-mono-claro.svg": svg(symbol("#ffffff"), 236, 200, "0 0 236 200"),
 "assinatura-escuro.svg": svg(wordmark("#ffffff", "#9aa4b2", BL_D), W, 200, f"0 0 {W} 200"),
 "assinatura-claro.svg": svg(wordmark("#0d1117", "#5b6472", BL), W, 200, f"0 0 {W} 200"),
 "assinatura-mono.svg": svg(wordmark("", "", "", mono="#0d1117"), W, 200, f"0 0 {W} 200"),
 "icone-app.svg": svg(f'<rect width="200" height="200" rx="44" fill="#0d1117"/><g transform="translate(16,20) scale(.68)">{symbol()}</g>', 200, 200, "0 0 200 200"),
 "icone-app-claro.svg": svg(f'<rect width="200" height="200" rx="44" fill="#ffffff"/><g transform="translate(16,20) scale(.68)">{symbol()}</g>', 200, 200, "0 0 200 200"),
 "icone-pequeno.svg": svg(f'<rect width="200" height="200" rx="40" fill="#0d1117"/><g transform="translate(4,12) scale(.8)">{symbol(small=True)}</g>', 200, 200, "0 0 200 200"),
}
for n, s in files.items(): open(n, "w").write(s)

