# CAM-STOCK-01: bruto por operação no Core (Volta)
Ramo: `ajuste-CAM-STOCK-01` (base `origin/dev` em `64d45e5`). Tamanho M. Só Core: uma classe nova e um arquivo de teste novo. Nenhum motor, G-code, formato de arquivo, App ou teste existente mudou.

## O que entrou (uma fatia, um commit)
`src/CncStudio.Core/Cam/CamStockTimeline.cs`: `CamStockTimeline.Compute(job, machine, bank, maxCellsPerSide, previous, ct)` devolve um estado por operação: `Ready`, `Disabled` ou `NoState`, com o motivo. Cada estado traz também o volume tirado, o volume que sobrou, o tempo, o percurso e o instantâneo do material. `ShowAfter(i)` põe no material o bruto depois da operação `i` (−1 = bruto inteiro), para a tela desenhar com o `SimulationScene` (CAM-STOCK-02).
- **Bruto:** montado de `CamJobStock.Check` e levado ao referencial do programa pelo zero, não pelo comentário `(BRUTO …)`. Bloco e cilindro ou tubo de pé vão na grade de alturas; cilindro ou tubo deitado vai em voxels com a forma.
- **Sem estado (com motivo, sem caixa automática):** sem bruto, bruto incompleto, bruto STEP (entra na F4) ou zero incompleto.
- **Motor:** voxels se alguma operação ligada gira a peça (face girada, texto no cilindro) ou se o bruto redondo está deitado; senão, grade de alturas. As resoluções são as mesmas do Code.
- **Por operação:** `BuildOperations` → `CamJobZero.Translate` → `CamPost.Generate` só dela → `ProgramAnalyzer.Analyze`, com o eixo rotativo do perfil da máquina (`RotaryPartFrame.From`). O simulador é o mesmo do Code, sobre o bruto compartilhado. Logo depois de criar o simulador, `Restore` do instantâneo anterior, porque o construtor zera o bruto. O Programa nº e o G5x desse texto são internos (1 e 54): não mudam a geometria e não aparecem em lugar nenhum.
- **Operação com erro ou campo vazio:** fica `NoState`, e as seguintes também ("Operação N anterior sem estado."). Operação desligada passa o bruto igual.
- **Ferramenta:** perfil do pacote do CAM (`CamHandoff.Create` com o banco → `PackageProfiles`). Sem banco, topo reto com o Ø.
- **Recalcular só o que mudou:** com `previous`, os estados se repetem enquanto o bruto inicial e o texto do programa de cada operação forem iguais.

## Testes
16 novos em `CamStockTimelineTests`:
- volume fecha (soma do tirado = inicial − final);
- **consistência**: o bruto depois da última operação é igual, célula a célula, ao do programa inteiro simulado de uma vez na mesma grade (`fresa-bloco-siemens`, `fresa-zero-g55-canto`, `fresa-fixacao`: 0 células diferentes);
- operação incompleta para o cálculo sem pular;
- desligada passa igual;
- sem bruto, bruto incompleto, STEP e sem zero ficam sem estado;
- 3+2 e texto no cilindro usam voxels;
- mudar só a 3ª reaproveita a 1ª e a 2ª, e nada mudado reaproveita tudo;
- cilindro de pé sem material fora do círculo (volume a menos de 2% do exato);
- cilindro deitado usa voxels;
- medição de tempo.

Core: **2103 verdes** (2087 + 16), rodada uma vez no portão, com o teste mestre `CamJobGoldenTests` (8 G-codes intactos). Build do Core em Release sem avisos. Nenhum teste existente alterado.

## Medido (Linux do Claude, Release)
- Grade de alturas 400×286: 20 operações, 2484 movimentos, **cerca de 1,7 a 2,1 s** no total. Instantâneo de **670 KB** por operação.
- Voxels (3+2, resolução do PC, 320×229×92, voxel 0,219 mm): 5 operações, 127 movimentos, **1,2 s**. Instantâneo de **822 KB**.
- Um programa como o do teste do Igor (3515 movimentos) deve ficar em poucos segundos na grade. Por isso a tela precisa calcular em segundo plano e mostrar "calculando".

## Decisões técnicas tomadas na execução
- A classe ficou em `Cam/` (depende do CAM); os motores ficam em `Simulation/`, intocados.
- O cancelamento (`ct`) é conferido entre operações e passado ao analisador.
- Operação desligada não quebra o reaproveitamento.

## Sem teste
- Tela (é a CAM-STOCK-02).
- Banco ativo com fresa esférica ou toroidal no perfil (o caminho é o mesmo do pacote do Code).
- Peça grande real em voxels (a de 3+2 tem só 127 movimentos).
- Nada em máquina.

## Só com ordem do Igor (não feito)
Merge no `dev`.
