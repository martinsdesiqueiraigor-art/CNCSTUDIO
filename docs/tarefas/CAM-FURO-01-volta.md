# CAM-FURO-01 — Volta (furação reconhecida)

## O que foi feito
- Core (novo, só acréscimo): `HoleRecognizer` acha os furos verticais da peça (faces `Hole` da malha) e agrupa por diâmetro, topo e fundo (tolerância 0,01 mm). Passante quando o fundo do furo coincide com o fundo da peça. Do maior para o menor diâmetro.
- Ficam de fora, com a razão em texto: furo com degrau (faces coaxiais com Ø diferente), furo inclinado e furo cego com fundo cônico.
- "Já tem operação": grupo cujos furos (centro e Ø) já estão nos círculos de uma Furação do trabalho aparece desmarcado.
- Criação: uma Furação por grupo marcado, pela rota que já existia (`CamJobFromGeometry.Add`, o mesmo clique no 3D). Nasce **sem ferramenta**; ciclo e bicada ficam como a Furação nova traz; fundo vem do furo. Furo profundo (> 3 × Ø) só ganha nota.
- Tela: item "Furação reconhecida…" em Operações ▾ (apagado, com dica, sem peça STEP lida); painel à direita com grupos, caixa de marcar, não reconhecidos e botões Fechar / Criar operações (N); círculos azuis (marcados) e cinza (de fora) no topo dos furos no 3D.
- Arquivo: nada novo (segue versão 8). G-code: nada mudou.

## Comportamento de referência copiado
Fusion (furos por diâmetro, "mesmo diâmetro"), SOLIDWORKS CAM (reconhecer Hole por diâmetro e profundidade), Mastercam (seleção de furos por eixo).

## Diferenças da imagem aprovada (avisar o Igor)
- "Ciclo sugerido G83" e "Saída + 1 mm" viraram nota de furo profundo e fundo do furo (regra "nada suposto").
- Os furos no 3D têm círculo colorido, **sem número** (a cena não desenha texto).
- O aviso "Conferir" no console para o que ficou de fora **não foi feito**: os não reconhecidos aparecem no painel com a razão.
- Valores da imagem eram exemplos.

## Casos de canto
- Sem STEP lido: item do menu apagado com dica.
- Painel fecha ao escolher outro item na árvore; Fechar não cria nada.
- Furo passante: fundo exato na base da peça (risco antigo, igual ao clique no 3D).
- Furo muito facetado pode não fechar 300° e aparecer como curva: não entra na lista.

## Testes
- Novos: `HoleRecognizerTests` (8): grupo de 6 passantes, passante × cego, ordem por Ø, degrau, inclinado, fundo cônico, "já tem operação", criação sem ferramenta.
- Core: 2146 verdes (2138 + 8), 8 G-codes intactos. App compila sem avisos.
- **Testes existentes alterados: nenhum.** O item novo na paleta (`CamPalette.cs`) não quebrou `CamPaletteTests`.
- O 3D não desenha no ambiente sem tela: a tela não foi vista. STEP real não abre no Linux.

## O que o Igor confere no .exe
1. BOLTED_FLANGE: Operações ▾ → "Furação reconhecida…" mostra "6 × Ø9 passante" marcado e o Ø44 como degrau (não reconhecido); círculos azuis nos 6 furos.
2. "Criar operações (1)": Furação com os 6 furos, sem ferramenta, selo amarelo até escolher a broca.
3. Com a broca, G-code e simulação furam os 6 furos nos lugares certos; peça × bruto sem gouge.
4. Abrir o painel de novo: o grupo aparece como "já tem operação" e não duplica.
5. Arquivo antigo (v8) abre e gera igual; salvo continua v8.
