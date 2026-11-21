# CAM-STOCK-03b — Volta (tela: peça × bruto)

## O que foi feito
- Chip "Peça × bruto" na barra sob o 3D e chave "Peça × bruto: Ligada" ao lado de "Bruto por operação" (preferência do app `CamStockCompare`, nasce ligada, nada no arquivo).
- 3D: gouge em vermelho e excesso em âmbar sobre o bruto da operação (só com bruto por operação ligado e índice ≥ 0).
- Árvore: selo por operação (gouge / excesso / ok); "sem estado" segue o do bruto.
- Aba "Verificação" nas propriedades: Desligada / Esta operação / Fim do programa, tolerância, volumes, % dentro, regiões com "Ver no 3D" (retângulo no 3D).
- Console: gouge como **Erro** (uma linha por operação); excesso não vira mensagem.
- Cálculo fora da tela, junto do bruto por operação. Sem peça 3D ou com voxel: chave e chip cinza com a razão.

## Comportamento de referência copiado
Fusion (verde/vermelho por desvio), iCAM e VERICUT AUTO-DIFF (excesso e gouge separados). Sempre "indicativo, nunca garantia".

## Casos de canto
- Sem peça STEP: indisponível, dica no chip.
- Voxel (rotativo/cilindro deitado): "indisponível com voxel".
- Bruto inteiro (índice < 0): sem cores.
- Operação desligada: texto na aba, sem cores.

## Testes alterados
Nenhum. Core 2138 verdes, incluindo os 8 G-codes (`CamJobGoldenTests`). Nada novo no Core nesta fatia. Build Release do App: 0 avisos.

## O que o Igor confere no .exe (3D não desenha no ambiente do Code)
1. BOLTED_FLANGE + Bolsão 3D automático: excesso nos 6 furos, sem gouge.
2. Arquivo com Contorno por fora Ø94 até Z−16: gouge na borda do flange Ø100, selo "gouge" e linha vermelha no console.
3. Chave e chip ligam/desligam juntos; chip cinza sem peça.
4. Aba Verificação: modos e "Ver no 3D".
5. Abrir arquivo antigo (v8): abre e gera igual.

## Avisos
- A hachura do gouge da imagem virou vermelho sólido no 3D.
- Valores da imagem eram exemplos.
- O STEP real do BOLTED_FLANGE não abre no Linux: tela não testada com ele aqui.
