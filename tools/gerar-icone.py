"""Gera os ícones do Casillas Code a partir dos SVGs de branding/.

Saídas em src/CncStudio.App/Assets/:
  casillas-code.ico  (16–256 px; 16–32 usam o desenho simplificado)
  casillas-code.png  (256 px, ícone do app)
  simbolo.png        (símbolo sem fundo, usado na faixa de comandos)
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
    browser.close()

big.save(ASSETS / "casillas-code.png")
symbol.save(ASSETS / "simbolo.png")

# .ico com várias resoluções: as pequenas vêm do desenho simplificado
frames = {s: (small if s <= 32 else big).resize((s, s), Image.LANCZOS) for s in [16, 24, 32, 48, 64, 128, 256]}
frames[256].save(ASSETS / "casillas-code.ico", sizes=[(s, s) for s in frames],
                 append_images=[frames[s] for s in frames if s != 256])
print("ok")
