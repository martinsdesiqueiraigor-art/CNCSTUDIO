# STEP-BRUTO-01: bruto "Importar STEP" alinhado com a peça
Nível G · Base `dev` em `07e78fa` · Ramo `ajuste-STEP-BRUTO-01`

## O que mudou
- Quando o bruto "Importar STEP" é o **mesmo arquivo** da peça STEP do Trabalho (e o posicionamento é "como está no arquivo"), o bruto passa a seguir a colocação da peça: mesmos giros e topo em Z0 com o canto mínimo de X e Y na origem. Os dois ficam sobrepostos no 3D e na caixa que a tela mostra.
- Outro arquivo, deslocamento digitado ou trabalho sem peça STEP: igual a antes (nada é alinhado sozinho).
- Gerar continua travado para bruto STEP, com o mesmo aviso. Sem chave nova no `.casillas-job`; trabalho antigo abre e gera igual.

## Causa
A peça STEP é colocada com `CamJobPart.Place` (giros, canto mínimo em X e Y na origem, topo em Z0); o bruto STEP era mostrado como está no arquivo, sem esse passo.

## Código
- `Cam/JobStockShape.cs`: `StepStockSummary.Placed`, `CamJobStock.SameFileAsPart`, `PlaceStepMesh(shape, mesh, part)`; `CheckStep` recebe a peça.
- `Cam3D/TriangleMesh.cs`: `Turn(turn, ponto)` extraído de `Turned` (mesma conta, sem duplicar).
- `App/Modules/JobView.Prep.cs`: o 3D usa a sobrecarga com a peça.

## Testes
Novos em `CamJobStepStockAlignTests` (6): mesmo arquivo cai na caixa da peça (sem e com giro), malha igual à da peça, arquivo diferente e sem peça ficam como estavam, deslocamento digitado vale sobre o arquivo, volume não muda. Nenhum teste existente mudou. Core: 2045 verdes; os 8 G-codes de referência intactos; build Release sem avisos.

## Não testado
- No .exe (o roteiro abaixo é do Igor ou do Claude no PC).
- O desvio em X e Y visto no #201 (X50, Y49,997) bate com o canto mínimo na origem da peça; confirmar no .exe que sumiu.

## Roteiro (.exe)
1. Trabalho novo, abrir Bolted_Flange.step, bruto "Importar STEP" com o mesmo arquivo: bruto e peça sobrepostos em Topo, Frente e Lado.
2. Trocar o STEP da peça ou girar: o bruto acompanha.
3. Salvar, fechar, abrir: continua alinhado.
4. Bruto de outro STEP: aparece como no arquivo, como antes.
5. Trabalho antigo (DXF e STEP) abre e gera igual.
