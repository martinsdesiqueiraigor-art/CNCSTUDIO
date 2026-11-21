# CAM-ILHAS-02: tela do bolsão automático (Volta)
Ramo: `ajuste-CAM-ILHAS-02` (base `origin/dev` em `2a4a15a`). Tamanho G. G-code, `PocketIslands`, `CamJob`/`CamJobJson` (arquivo), torno, Web e os 8 G-codes de referência ficaram intactos.
Imagem aprovada pelo Igor (21/11, 04:51): quadro "CAM fresa — bolsão do 3D automático (proposta CAM-ILHAS-02)".

## O que entrou
- **Laços (aba Estratégia do bolsão do 3D):** botões **Automático**, Todos e Só o de fora. No Automático:
  - resumo numa nota ("1 ilha · 8 furos ignorados · borda aberta. A borda vai até o bruto + raio da fresa + 1 mm.");
  - lista dos laços agrupados, com traço na cor e o efeito ("Borda de fora Ø100 — desce · aberta", "Ilha Ø40 — sobe · ilha", "8 furos Ø10 — descem · ignorados");
  - laço misto: nota vermelha com "escolha Todos ou Só o de fora".
- **Árvore:** sob Geometria, uma linha por papel na cor dele ("1 ilha", "8 furos ignorados", "borda aberta"). Mouse na linha (árvore ou lista) acende aqueles laços em branco no 3D.
- **3D:** com o bolsão automático escolhido, os laços da face aparecem em cor por cima de tudo: verde (ilha), cinza (ignorado), laranja (borda aberta), azul (parede do bolsão fechado), vermelho (misto).
- **Peça:** menu com Sólida, **Transparente**, Aramado e Oculta.
  - Com peça STEP, Sólida e Transparente ficam ativas (antes "Sólida" ficava apagada e o chip dizia "Aramado" com a peça desenhada opaca).
  - Aramado e Transparente valem para a peça STEP (antes ela era sempre opaca).
  - Com o bruto de uma operação na tela (bruto por operação), a peça sólida aparece transparente para não esconder o material.
- **Bruto: Oculto** esconde também o material da operação.
- **Core:**
  - `PocketLoopReport` (novo): papel, nome e cor de cada laço, resumo e grupos, com a mesma classificação da geração (`MeshFeatures.ClassifiedLoops`).
  - `CamViewOptions`: valor novo `peca=transparente` (programa antigo ignora e fica no padrão).
  - Cena: papel novo `SceneRole.Part` e `SceneSnapshot.PartOpacity` (padrão 1). Nada muda no Code.

## Decisões técnicas da execução
- **Cena do Core mexida (fora da lista da ficha, só acréscimo):** a opacidade era só do material; a peça transparente precisava de um papel próprio. O Code não usa o papel novo.
- **Tracejado no 3D:** as linhas do 3D não fazem tracejado; os laços vão em cor cheia. O tracejado ficou nos traços da árvore e da lista.
- **Parede do bolsão fechado:** ganhou cor (azul) e o texto "bolsão fechado", que a imagem não mostrava (a flange tem borda aberta).
- **Peça transparente com o material:** a troca é automática só quando a peça está em Sólida; Aramado e Oculta valem como escolhidos.

## Testes
- Core: suíte inteira, **2123 verdes** (5 novos em `PocketLoopReportTests`), com `CamJobGoldenTests` (8 G-codes intactos). Nenhum teste existente mudou.
- App: build Release, **0 avisos e 0 erros**.
- Tela: a conferir no .exe (o STEP e o 3D não abrem no ambiente Linux do Claude).

## O Igor testa no .exe
1. BOLTED_FLANGE: remover o bolsão antigo, Bolsão novo na face do topo da flange. Estratégia → Laços: Automático marcado, resumo e lista.
2. Com a operação escolhida: laços coloridos no 3D; linhas sob Geometria na árvore; mouse acende em branco.
3. Peça: Transparente mostra o cubo usinado por baixo; Bruto: Oculto esconde o material.
4. Bolsão antigo (Todos ou Só o de fora) abre com o botão dele marcado; trocar para Automático refaz e gera certo.

## Sem teste
- 125% e 150%; trabalho sem peça STEP (só DXF); vários bolsões automáticos; nada em máquina.

## Só com ordem do Igor
Merge no `dev`.
