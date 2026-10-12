"""Gera os ícones do Casillas Code a partir dos SVGs de branding/.

Saídas em src/CncStudio.App/Assets/:
  casillas-code.ico  (16–256 px; 16–32 usam o desenho simplificado)
  casillas-code.png  (256 px, ícone do app)
  simbolo.png        (símbolo sem fundo, usado na faixa de comandos)
e em src/CncStudio.Web/wwwroot/icons/ (app instalável/PWA):
  icone-192.png, icone-512.png, icone-maskable-512.png (fundo cheio, símbolo na área segura)
Requer: pip install pillow playwright  (o Chromium do Playwright desenha os SVGs)
Rodar na raiz do repositório: python tools/gerar-icone.py
"""
import io
import re
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
BRAND = ROOT / "branding"
ASSETS = ROOT / "src" / "CncStudio.App" / "Assets"
WEB_ICONS = ROOT / "src" / "CncStudio.Web" / "wwwroot" / "icons"


def render(browser, svg_name: str, size: int) -> Image.Image:
    """Desenha o SVG com fundo transparente na largura pedida (altura proporcional)."""
    svg = (BRAND / svg_name).read_text(encoding="utf-8")
    w, h = map(int, re.search(r'width="(\d+)" height="(\d+)"', svg).groups())
    page = browser.new_page(viewport={"width": w, "height": h}, device_scale_factor=size / w)
    page.set_content(f'<html><body style="margin:0;background:transparent">{svg}</body></html>')
    png = page.locator("svg").screenshot(omit_background=True)
    page.close()
    return Image.open(io.BytesIO(png)).convert("RGBA")


with sync_playwright() as p:
    browser = p.chromium.launch()
    big = render(browser, "icone-app.svg", 256)
    small = render(browser, "icone-pequeno.svg", 64)
    symbol = render(browser, "simbolo.svg", 236)
    web512 = render(browser, "icone-app.svg", 512)
    # Ícone "maskable": o Android recorta em círculo/gota, então o fundo ocupa tudo
    # e o símbolo fica dentro da área segura (círculo central de 80%)
    page = browser.new_page(viewport={"width": 512, "height": 512})
    symbol_svg = (BRAND / "simbolo.svg").read_text(encoding="utf-8")
    page.set_content('<html><body style="margin:0;background:#0d1117;width:512px;height:512px;'
                     'display:flex;align-items:center;justify-content:center">'
                     f'<div style="width:300px">{symbol_svg.replace("<svg ", "<svg style=\'width:100%;height:auto\' ", 1)}</div></body></html>')
    maskable = Image.open(io.BytesIO(page.screenshot())).convert("RGBA")
    page.close()
    browser.close()

big.save(ASSETS / "casillas-code.png")
WEB_ICONS.mkdir(parents=True, exist_ok=True)
web512.save(WEB_ICONS / "icone-512.png")
web512.resize((192, 192), Image.LANCZOS).save(WEB_ICONS / "icone-192.png")
maskable.save(WEB_ICONS / "icone-maskable-512.png")
symbol.save(ASSETS / "simbolo.png")

# .ico com várias resoluções: as pequenas vêm do desenho simplificado
frames = {s: (small if s <= 32 else big).resize((s, s), Image.LANCZOS) for s in [16, 24, 32, 48, 64, 128, 256]}
frames[256].save(ASSETS / "casillas-code.ico", sizes=[(s, s) for s in frames],
                 append_images=[frames[s] for s in frames if s != 256])
print("ok")
