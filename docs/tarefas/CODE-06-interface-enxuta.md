# Tarefa CODE-06: interface enxuta + ajustes do 3D

ID por produto: `CODE-`, `MACRO-`, `CAM-`, `ACAD-`, `BIB-`, `FERR-` + número.

**Para:** Claude (conversa nova)  **Ramo base:** `main` (depois do merge da Fase 1)  **Ramo da entrega:** `ajuste-interface`

## Objetivo
Deixar a tela limpa e com menos espaço gasto em botões, no desktop e na web, seguindo o esboço aprovado
"Casillas Code — interface enxuta" (Design no claude.ai: PC 2D+3D com menu Ver aberto, e celular).

## Pode mexer em
- `src/CncStudio.App/` (MainWindow, GraphicsPanel, GlSceneView)
- `src/CncStudio.Web/` (MainView, Scene3DView, `wwwroot/scene3d.js`)
- `src/CncStudio.Core/Scene/` só para o material transparente e as marcas de troca de ferramenta, com testes

## Entregas
1. **Barra de cima fina:** só ícones (nome no passar do mouse); Exemplos vira menu; Máquina e Dialeto pequenos à direita.
2. **Gráfico:** seletor `2D | 2D + 3D | 3D` e um menu **Ver** (Trajetória, Rápidos, Ferramenta, Meia seção, Modo técnico, Fundo claro, legenda das cores). Somem "Torno 3D", "3D", "Trajetória 3D" e a legenda solta. Ajustar, Medir, Bruto, Ferramentas e as vistas (Iso, Topo, Frente, Lado) viram ícones na mesma linha.
3. **No 2D + 3D**, o lado esquerdo mostra o perfil (ZX no torno, a vista escolhida na fresa); nunca dois 3D.
4. **Faixa de baixo numa linha:** X, Z (Y na fresa), ferramenta, G atual, linha, passe, play pequeno, barra de progresso, velocidade num menu.
5. **Editor recolhível** (seta no topo das abas).
6. Corrigir a legenda "meia seção" que não segue o botão, e o "Fundo claro" que não vale no 3D.
7. Itens do CIMCO Edit:
   1. **Material transparente** (opção no Ver) para ver a ferramenta por dentro.
   2. **Marcas de troca de ferramenta** na barra de progresso; tocar leva à ferramenta.
   3. **Distância percorrida / total** e avanço atual na faixa de baixo.
   4. **Fundo do 3D em degradê** (tema escuro e claro).
   5. **Barra conforme o modo:** ferramentas de simulação só com o gráfico aberto.
8. **Celular:** abas Editor | Gráfico, seletor 2D | 3D, Ver, play embaixo com alvos de 44 px ou mais.

## Pronto quando
- [ ] Desktop e web com o mesmo arranjo do esboço (prints em 360 px, 390 px e 1440 px)
- [ ] Nenhum recurso perdido: tudo o que estava em caixas de marcar está no Ver
- [ ] 3D do desktop gira, move e dá zoom (teste headless de mouse, como na correção de 18/10)
- [ ] Testes do Core passando; novos testes para as marcas de troca e a distância total
- [ ] Prévia gerada e PR aberto para a `main`, com a seção **Volta** do `MODELO.md`

## Fora do escopo
- Comparação de arquivos (CODE-07) e transmissão DNC (CODE-08).
- "Regenerar sólido" com mais detalhe no zoom (fase Avançado).
- Trocar fórmulas ou a simulação do material.

## Referências
- Esboço: Design "Casillas Code — interface enxuta" (claude.ai).
- Tela do CIMCO Edit enviada pelo Igor em 18/10 (aba Simulação).
- Tema: escuro, laranja `#ff7a1a`, azul `#4aa3ff`.
