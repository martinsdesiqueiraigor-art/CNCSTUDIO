# CAM-STOCK-00: auditoria do motor de remoção para o bruto atualizado no CAM (Volta)
Ramo: `doc-CAM-STOCK-00` (base `origin/dev` em `64d45e5`; a ficha citava `592315b`, e o que entrou no meio foi só o #209 FECHO-02, que não mexe no Core). Tamanho M, só leitura. Único arquivo alterado: este relatório.

## Resposta curta
**Dá para reaproveitar os motores do Core sem alterá-los** (`VoxelStock`, `VoxelSimulator`, `HeightMap`, `MaterialSimulator`). O que falta é **uma camada fina e nova no Core**, que chamo aqui de `CamStockTimeline`. Ela monta o bruto direto do trabalho, simula **operação por operação** sobre o estado da anterior e guarda um instantâneo por operação. A tela só mostra esses instantâneos.

Atenção a um ponto: **na fresa de 3 eixos, o Code não usa voxel, usa a grade de alturas** (`HeightMap`). O voxel só entra com eixo rotativo ou com cilindro deitado (`SimulationFactory.cs:77-83`, `:143-156`). A camada nova escolhe o motor pela mesma regra.

## As 6 perguntas
**1. Trajetória e ferramenta.**
- **Entrada:** os simuladores recebem `Trajectory.Move`, que vem do G-code em texto analisado (`ProgramAnalyzer.Analyze`, `ProgramAnalyzer.cs:45-48`; `VoxelSimulator.cs:32-37`). Não recebem os `ToolMove` do CAM.
- **Ferramenta:** é um perfil de revolução: topo reto, esférica, toroidal com raio de canto ou cone pelo ângulo da ponta (`ToolProfile.cs:4-17`, `:35-65`).
- **Volume varrido: não é exato, é por carimbos.** O arco vira pontos com tolerância de ¼ de voxel (`VoxelSimulator.cs:214`). A ferramenta é carimbada a cada meio voxel ao longo do trecho e a cada 1° no eixo rotativo (`:226-265`). Cada carimbo é um cilindro de 25 mm com o perfil na ponta (`VoxelStock.cs:203-287`; `CutLength = 25` em `VoxelSimulator.cs:18`). Na resolução da grade, isso equivale ao volume varrido.
- **Rápido:** no voxel, o rápido não corta e não acusa colisão (`VoxelSimulator.cs:93`, `:179`). Na grade de alturas, o rápido que toca material vira colisão.

**2. Armazenamento e custo.**
- **Como guarda:** 1 bit por voxel em `ulong[]` (`VoxelStock.cs:16`, `:36-42`). Os blocos de 16³ guardam só uma versão, para a malha refazer só o que mudou (`:14`, `:35`, `:270-284`).
- **Limite:** no PC, a fábrica usa 320 voxels por lado e 16 milhões no total; no celular, 160 e 2,5 milhões (`SimulationFactory.cs:89-90`). Os valores de 256 e 12 milhões são só o padrão do método (`VoxelStock.cs:47`). Grade grande demais dá exceção (`:37-40`).
- **Exemplo BOLTED_FLANGE:** a peça tem 100×100×26 (`Desk01StepPartTests.cs:10`). Com um bruto de 110×110×30 (estimativa), o voxel fica com 0,34 mm de lado: são 320×320×88, cerca de 9 milhões de voxels, **cerca de 1,1 MB por instantâneo**. Na grade de alturas (400 células), o instantâneo dá cerca de 1 MB (`float` e `ushort` por célula, `HeightMap.cs:148-164`).
- **Instantâneo:** `TakeSnapshot` e `Restore` são públicos (`VoxelStock.cs:191-197`; `HeightMap.cs:161-164`) e **bastam para um estado por operação**. Com 20 operações, são cerca de 22 MB. O `Restore` marca tudo como alterado, então a malha é refeita inteira ao trocar de operação.
- **Tempo:** não foi medido (é só leitura). Estimativa: com Ø10, cada carimbo vertical confere uns 65 mil voxels, e voxel já vazio sai na hora (`VoxelStock.cs:236`). Um programa como o do teste (3515 movimentos) deve levar segundos. A medição fica na F1.

**3. Operações no CAM.**
- `CamJobBuilder.BuildOperations` já gera **cada operação separada** (`CamJob.cs:329-339`). O resultado é um `CamOperation` com uma lista de `ToolMove` (`ToolPath.cs:117`).
- O `Build` inteiro não serve para isso, porque devolve programa vazio se **qualquer** operação falhar ou faltar o Programa nº, o G5x ou o zero (`CamJob.cs:358-372`).
- Caminho sem mexer no motor, por operação:
  1. deslocar a operação pelo zero (`CamJobZero.Translate`, `JobZero.cs:200`);
  2. gerar o G-code só dela com `CamPost.Generate([op], …)`, como o `CamQuickJson.cs:190` já faz;
  3. analisar esse texto;
  4. simular sobre o mesmo bruto.
- **Cuidado:** o construtor do simulador zera o bruto (`VoxelSimulator.cs:39`, `:98`; `MaterialSimulator.cs:53`, `:128`). A camada faz `Restore(instantâneo anterior)` logo depois de criar o simulador. Funciona com a API de hoje.

**4. Bruto no simulador.**
- **Bloco, cilindro e tubo:** entram como caixa ou forma (`VoxelStock(…, shape)`, `VoxelStock.cs:21`, `:131-139`; `ApplyRoundShape`, `SimulationFactory.cs:55-71`).
- **Hoje o 3D do CAM pega o bruto só pelo comentário `(BRUTO …)`**, sem setup (`JobSceneView.cs:106-115`). Esse comentário não leva posição, não existe para cilindro deitado e não existe para STEP (`CamHandoff.cs:91-100`); sem ele, o simulador cria uma caixa em volta do corte. A camada nova deve montar o bruto de `CamJobStock.Check` com a posição real (`JobStockShape.cs:290-344`).
- **STEP:** não existe malha → grade no Core. Na `Cam3D` só há a altura por coluna (`DropCutter.cs:55`) e o teste de malha fechada (`TriangleMesh.cs:84`).
  - Para voxel, é preciso preencher cada coluna X/Y entre os cruzamentos de um raio com a malha, o que exige malha fechada.
  - Para a grade de alturas, basta o topo por coluna. O fundido com reentrância some, mas para 3 eixos tanto faz.
- **Por que "Gerar travado com STEP":** a trava só vale quando o bruto STEP **é o mesmo arquivo da peça**, porque aí não há sobremetal (`JobStockShape.cs:474-477`). Com outro STEP, Gerar funciona: o programa sai igual e só o simulador do Code não desenha o bruto (`JobView.axaml.cs:136-140`). Não é preciso calcular Bloco e sobra.

**5. Acoplamento.**
- `Simulation/` é Core puro: só usa namespaces do próprio Core, sem Avalonia (conferido nos `using`).
- A única lógica de "simular o trabalho" que fica no App é o `JobSceneView.Simulate` (`JobSceneView.cs:106-135`). Ela gera o programa inteiro, roda até o fim e não guarda estado por operação.
- Do lado do CAM, nada precisa sair do `GraphicsPanel`. A camada nova fica no Core e o App só a chama em segundo plano, como já faz (`JobSceneView.cs:71`).

**6. Riscos.**
- **Desempenho:** o tempo não está medido. A solução é calcular em segundo plano e recalcular **a partir da operação alterada** (os instantâneos permitem).
- **Precisão:** o resultado é indicativo, limitado ao voxel (0,34 mm no exemplo) ou à célula (0,275 mm). O carimbo tem 25 mm de altura: num bolsão mais fundo que isso, a parede acima da ferramenta pode aparecer com material.
- **Consistência:** operação incompleta não gera resultado (`CamJob.cs:390-393`). A camada para nela, e as operações seguintes ficam "sem estado" (sem pular para não mentir).
- **Motor misto:** basta uma operação com face girada ou texto no cilindro para o trabalho todo ir para voxel.
- **Programa por operação:** o post exige Programa nº e G5x, que só mudam o texto, não a geometria. A camada usa valores internos que nunca aparecem nem são salvos. Sem zero, não há estado.
- **Torno:** tem motor próprio (`TurningStock`) e fica fora desta trilha; seria uma fatia à parte.
- **Arquivos antigos:** **sem risco**. O bruto é derivado e nada é gravado, então o `.casillas-job` continua na versão 7 (`CamJob.cs:144`).

## Recomendação
**Extrair uma camada, sem mudar os motores:** `CamStockTimeline` no Core, com testes. Ela:
1. monta o bruto de `CamJobStock.Check`, já no referencial do programa;
2. escolhe entre grade de alturas e voxel com a regra do Code;
3. para cada operação ligada, gera o programa só dela, analisa e simula sobre o instantâneo anterior;
4. guarda um instantâneo e o estado por operação (pronta, sem estado, erro);
5. recalcula a partir da operação alterada.

A tela só lê os instantâneos.

## Tamanho das fatias
| Fatia | Tamanho | O que mexe no Core |
|---|---|---|
| F1 · bruto por operação (visual, fresa) | **M** | Só a classe nova `CamStockTimeline` e seus testes. Nenhum motor, G-code ou arquivo muda. App: selecionar na árvore mostra o instantâneo. |
| F2 · peça × bruto (excesso e gouge) | **G** | Converter a peça (malha) para a mesma grade (voxel por coluna, ou `DropCutter` na grade de alturas) e comparar em cores por distância. Geometria nova e risco de falso alarme. |
| F3 · trajetória ciente do bruto | **G** | Os geradores (`Operations`) passam a ler o bruto. Muda G-code e precisa de autorização para os 8 de referência. |
| F4 · bruto STEP na simulação | **M** | Converter a malha fechada em grade e levar o bruto STEP ao Code (hoje vai só o caminho do arquivo). Gerar já funciona com outro STEP. |
| F5 · bruto entre fixações | **G** | Depois da F3. Exige mais de uma fixação na fresa, que hoje está fora. |

## Testes
Nenhum rodado nem alterado (auditoria só de leitura). Nenhum `.cs` mudou.

## Sem conferir
- Tempo real de simulação (estimado, não medido).
- O bruto do BOLTED_FLANGE usado no exemplo é suposto (110×110×30).
