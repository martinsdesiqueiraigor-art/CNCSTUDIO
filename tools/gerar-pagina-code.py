# Gera casillas-landing/code/index.html a partir de wwwroot/conheca.html (só troca os links)
import re, sys
src, dst = sys.argv[1], sys.argv[2]
s = open(src).read()
APP = 'https://casillas-code.pages.dev/'
s = s.replace('href="./conheca/', 'href="./assets/').replace('src="./conheca/', 'src="./assets/')
s = s.replace('src="./assinatura.svg"', 'src="./assets/assinatura-code.svg"')
s = s.replace('href="./icone.png"', 'href="' + APP + 'icone.png"')
s = s.replace('href="./termos.html"', 'href="' + APP + 'termos.html"').replace('href="./privacidade.html"', 'href="' + APP + 'privacidade.html"')
s = s.replace('href="./"', 'href="' + APP + '"')
s = s.replace('href="https://martinsdesiqueiraigor-art.github.io/Casillas-landing/app/" target="_blank" rel="noopener"', 'href="../app/"')
s = s.replace('content="https://casillas-code.pages.dev/conheca/computador.webp"', 'content="https://casillas.com.br/code/assets/computador.webp"')
s = s.replace('<meta name="robots" content="index, follow" />', '<meta name="robots" content="index, follow" />\n<link rel="canonical" href="https://casillas.com.br/code/" />')
# Logo do topo leva ao início do site; menu com Início e Combo
s = s.replace('<img src="./assets/assinatura-code.svg" alt="Casillas Code" />\n    <nav>',
              '<a href="../" aria-label="Início Casillas"><img src="./assets/assinatura-code.svg" alt="Casillas Code" /></a>\n    <nav>\n      <a class="btn small hide-sm" href="../#combo">Combo</a>', 1)
left = re.findall(r'(?:href|src)="\./(?!assets/)[^"]*"', s)
if left: print('links relativos restantes:', left)
open(dst, 'w').write(s)
