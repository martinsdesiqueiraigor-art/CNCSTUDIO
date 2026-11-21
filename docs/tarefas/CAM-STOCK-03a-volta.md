# CAM-STOCK-03a: peça × bruto, só Core (Volta)
Ramo: `ajuste-CAM-STOCK-03a` (base `origin/dev` em `463acbf`). Tamanho G. Só Core: nada de tela, G-code ou arquivo.

## O que entrou
- `src/CncStudio.Core/Cam/CamStockCompare.cs` (novo): `PartHeights` (topo da peça por coluna da grade), `Compare` (função pura) e a camada `CamStockCompare.Create(timeline, job, malha)` com `After(i)`, `Final()` e `Summary()` (selo por operação: sem estado, desligada, ok, excesso, gouge).
- Resultado: volume de gouge e de excesso (mm³), % de células dentro, mapa por célula (`CompareCell`) e regiões conexas (tipo, células, volume, profundidade máxima, caixa), gouge primeiro.
- `CamStockTimeline` e os motores (`HeightMap`, `VoxelStock`, `DropCutter`) **não foram alterados**; a camada só chama `ShowAfter` e lê `Map`.
- Testes novos: `CamStockCompareTests` (15).

## Regras da comparação (para a 03b usar sem adivinhar)
- Tolerância = tamanho da célula (padrão); pode ser informada.
- Gouge: célula com peça e bruto abaixo do **menor** topo da peça na vizinhança 3×3 (só células com peça contam) menos a tolerância. Excesso: bruto acima do **maior** topo da vizinhança mais a tolerância. A vizinhança dá uma célula de tolerância lateral: a parede da peça não gera falso alarme, e um corte lateral de uma célula na parede não é visto.
- Sem peça e sem material: a célula não entra na conta. Furo da peça com material no bruto: excesso (só o miolo; a borda de uma célula é tolerância).
- Voxel (eixo rotativo, cilindro deitado): `Create` devolve null com a mensagem `CamStockCompare.VoxelUnavailable`. Sem peça: `NoPart`. Sem bruto, sem zero: motivo do próprio `CamStockTimeline`/zero.
- O zero do trabalho é descontado da malha dentro do `Create` (a mesma conta do bruto). A malha vem do desenho, já colocada (`CamJobPart.Place`).
- `After` deixa o material do `Map` no estado daquela operação, como `ShowAfter`. Não é thread-safe.
- Limite escrito em `CamStockCompare.Disclaimer`: peça pelo topo por coluna, medida da célula, indicativo.

## Comportamento de referência copiado
Fusion (bruto no processo comparado com o modelo), iCAM (peça × bruto em cores) e VERICUT AUTO-DIFF (excesso e corte a mais). Copiado: duas classes de diferença (a mais e a menos), cor própria, volume e regiões. Não copiado: degradê por distância e ajuste da trajetória.

## Provas
- Bruto igual à peça: 0 e 0, 100 % dentro. Bruto maior: só excesso, 40×40×4 = 6400 mm³. Corte além: só gouge, 3200 mm³. Diferença abaixo da tolerância: não acusa; tolerância apertada: acusa.
- Flange sintético do teste de ilhas (cubo, placa, furo): o Bolsão 3D automático dá **gouge 0** e excesso no furo vazado; o mesmo bolsão com fundo −12 dá gouge de ≈2 mm no piso.
- Contorno por fora que entra na flange Ø100 (contorno Ø88, topo da flange em −10, corte em −16): **gouge na borda da flange**, profundidade ≈6 mm.
- Mesma entrada duas vezes: mesmo resultado.
- Core: 2138 verdes (2123 + 15). `CamJobGoldenTests`: 8 G-codes intactos. Nenhum teste existente mudou.

## Medido
Comparação de todas as operações (resumo) com peça de 5040 triângulos: `fresa-bloco-siemens`, grade 400×286, 5 operações: **47 ms**; `fresa-fixacao`, grade 400×284, 5 operações: **55 ms**. Desprezível perto da simulação. Com 20 operações, estimativa linear: cerca de 0,2 s.

## Casos de canto de fora
- BOLTED_FLANGE real (STEP): o Linux deste ambiente não abre o STEP; a prova do item 2 da ficha foi com flange sintético equivalente. O arquivo real fica para o teste da 03b no .exe.
- Peça com rebaixo ou reentrância (topo por coluna); voxel; cilindro deitado; bruto STEP.
- Contorno Ø94 literal da ficha: no teste usei Ø88 porque sem banco de ferramentas a simulação usa a fresa padrão Ø10 e a parede do corte fica em r≈49 (no .exe, com o banco, a fresa real entra no cálculo).

## Testes alterados
Nenhum.

## O que o Igor confere
Nada no .exe nesta fatia. Leia só esta Volta e o resultado dos testes; a 03b traz a tela.
