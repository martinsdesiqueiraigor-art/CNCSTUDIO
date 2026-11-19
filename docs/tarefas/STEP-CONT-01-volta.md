## STEP-CONT-01: contorno do Assist pela silhueta do STEP (relatório)

Ramo `ajuste-STEP-CONT-01`, **base `ajuste-STEP-OPS`** (PR #200 ainda em rascunho, fora do `dev`). Por isso o PR abre contra `ajuste-STEP-OPS`; depois do merge do #200, trocar a base para `dev`. Executor: Claude (chat), por ordem do Igor (19/11, "Autorizado e resolva as duas pendências"). Nada foi testado no .exe depois destas mudanças.

**Origem:** achado no teste do PR #200 no .exe (BOLTED_FLANGE, 19/11): o flange tem chanfro de ~3 mm; a face de cima é Ø94, a base Ø100. O Assist lia a peça como 94 × 93,98 e planejava o contorno em Ø94 (cortaria ~3 mm do flange). O contorno feito à mão pela aresta deu Ø100.

### O que mudou
1. **Contorno pela silhueta** (`StepPlanSource`): o laço externo vem do maior laço de fora entre **todas** as faces (antes só as voltadas para cima). Faces verticais têm laço sem área no plano e não pesam. Peça sem chanfro dá o mesmo contorno de antes (teste). A nota "Lido do STEP: peça A × B" passa a mostrar a silhueta.
2. **Pendência 1, rótulo "digitado":** profundidades lidas do STEP apareciam como "digitado" na revisão do Assist (o evento de texto chegava depois de preencher) e a marca da operação dizia "digitado: profundidade". Agora valor igual ao lido continua "do modelo STEP" e só entra "digitado: profundidade" se o usuário mudar. Alturas Z seguem como digitadas. `PlanReview.Review` ganhou o parâmetro opcional `modelDepthKeys` (sem ele, nada muda).
3. **Pendência 2, contorno duplicado:** cada operação do 3D guarda seus contornos (necessário: trocar a geometria de uma não pode mexer na outra), então duas operações da mesma face deixavam dois contornos iguais em "Peça pronta + bruto", e o Assist por DXF contava e planejava em cima deles. Agora o Assist por DXF lê só os contornos que não pertencem a operação do 3D (`JobView.PlanContourMap`), e ao aplicar o plano os índices voltam para os do trabalho (`CamView.ApplyPlan`). **Não** juntei os contornos duplicados na árvore: continuam existindo, por causa da regra acima.

### Testes
- `dotnet build -c Release`: sem aviso. `dotnet test tests/CncStudio.Core.Tests` em Release: 2037 aprovados (3 novos em `StepPlanSourceTests`: silhueta com chanfro, peça sem chanfro igual, profundidade do STEP sem marca de digitada). O teste da silhueta falha sem a correção (conferido). Os 8 G-codes (`CamJobGoldenTests`) passam sem mudança. Nenhum teste existente foi alterado.
- A parte de App (pendências 1 e 2) só compilou; precisa do teste do Igor.

### Roteiro do Igor no .exe (BOLTED_FLANGE)
1. Assist, fonte "Peça STEP do Trabalho": a tela diz Ø100 × 100 e o contorno da peça sugerido é Ø100. Aplicar: os 6 furos Ø9 e o Bolsão 1 saem como antes.
2. Na revisão, as profundidades mostram "do modelo STEP" (não "digitado"). Mudar uma para outro valor: vira "digitado". Depois de aplicar, a operação diz "digitado: alturas Z" (e "profundidade" só nas que você mudou).
3. Criar um Faceamento e um Bolsão da mesma face; ir ao Assist com fonte "Desenho (DXF)": diz "Nenhum desenho" (ou só os contornos que não vêm do 3D), não "2 contorno(s)".
4. Reselecionar: numa operação por face ou aresta, "Trocar seleção" e escolher outra; geometria e 3D mudam e a operação segue válida (não foi testado ainda).

### Riscos
- Silhueta = maior laço de qualquer face: numa peça com aba mais larga que a base (cogumelo) o contorno acompanha a aba.
- Uma operação do 3D já criada num trabalho salvo antes continua dona dos seus contornos; nada migra.
