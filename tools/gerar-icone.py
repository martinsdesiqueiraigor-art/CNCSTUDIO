"""Gera o ícone do CncStudio (Assets/cncstudio.ico e .png) com Pillow.

Desenho: fresa de topo (laranja) sobre uma trajetória (verde, com arco azul), fundo escuro.
Rodar: python tools/gerar-icone.py
"""
from PIL import Image, ImageDraw

S = 1024  # desenha grande e reduz (bordas suaves)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Fundo arredondado
d.rounded_rectangle([40, 40, S - 40, S - 40], radius=200, fill=(30, 36, 46, 255), outline=(70, 82, 98, 255), width=16)

# Trajetória: G01 verde em degraus e um G02 azul
w = 46
d.line([(170, 800), (400, 800), (400, 640), (680, 640)], fill=(61, 220, 106, 255), width=w, joint="curve")
d.arc([520, 640 - w // 2, 840 + w // 2, 960], start=270, end=360, fill=(127, 216, 255, 255), width=w)
# Rápido G00 tracejado vermelho
for x in range(170, 560, 90):
    d.line([(x, 300), (x + 50, 300)], fill=(255, 77, 77, 255), width=30)

# Fresa: suporte cinza + corpo laranja com canais
d.rectangle([600, 110, 760, 230], fill=(138, 146, 156, 255))
d.rectangle([630, 230, 730, 560], fill=(255, 176, 64, 255))
for y in range(260, 540, 70):
    d.line([(630, y), (730, y + 45)], fill=(200, 120, 20, 255), width=18)
d.polygon([(630, 560), (730, 560), (705, 615), (655, 615)], fill=(255, 176, 64, 255))

sizes = [16, 24, 32, 48, 64, 128, 256]
img.resize((256, 256), Image.LANCZOS).save("src/CncStudio.App/Assets/cncstudio.png")
img.save("src/CncStudio.App/Assets/cncstudio.ico", sizes=[(s, s) for s in sizes])
print("ok")
