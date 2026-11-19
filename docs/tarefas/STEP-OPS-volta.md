## STEP-OPS: operações a partir do STEP (relatório)

Ramo `ajuste-STEP-OPS`, base `dev`. Executor: Claude (chat), em sprint, por ordem do Igor (19/11, "Aprovo pode continuar"). **Nada foi testado na máquina do Igor nem no .exe**: só compilação e testes do Core.

**Commits:** S0 Core (geometria guardada, arquivo v6) · S0 App (seleção → faixa, seção Geometria, faces acesas) · S1 faceamento · S2 contorno · S3 bolsão com ilhas · S4 Assist lê STEP (só Core) · ajuste de 4 testes (abaixo).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 2032 (novos: 13 em `StepOpsGeometryTests`, 5 em `StepPlanSourceTests`). `dotnet build -c Release` sem aviso nem erro. Os 8 G-codes (`CamJobGoldenTests`) passam sem mudança.
- **Testes existentes que mudaram (autorizado pelo Igor, 19/11):** 4 testes de "arquivo mais novo é recusado" (`CamJobTests`, `CamJobProgramTests`, `CamJobZeroTests`, `CamJobStockShapeTests`) usavam a versão 6 como exemplo de versão futura; como o arquivo passou a v6, o exemplo virou 7 (uma linha em cada; no `CamJobStockShapeTests` também o número na mensagem esperada). O sentido não mudou. Nenhum teste de G-code mudou.
- Não rodei `tests/CncStudio.Step.Tests` (nada em `src/CncStudio.Step` mudou) nem a peça `Bolted_Flange.step` (usei malhas montadas nos testes: bloco com bolsão e pino, arruela).

### O que entrou
- **Geometria da operação (arquivo v6):** a operação feita do 3D guarda a "impressão" das faces/arestas (tipo, centro, direção, tamanho), não o número da malha; ao reler o STEP ela acha de novo. Trabalho sem geometria continua salvando como antes (v4/v5).
- **Faixa + seleção no 3D:** com STEP lido, clicar faces (Ctrl soma) ou arestas e usar Faceamento / Contorno / Bolsão / Furação da faixa; o botão apagado diz o porquê. Clicar na operação na árvore acende as faces dela.
- **Seção "Geometria" no painel:** lista, ✕, Trocar seleção, + Adicionar, laços (Todos/Só o de fora no bolsão; Externo/Interno no contorno), "Pegar o Z do fundo do modelo". Trocar mantém T, Ø, avanços e alturas digitados.
- **S1 Faceamento:** contorno de fora da face; Z fundo = Z da face (digitável).
- **S2 Contorno:** pela face (laço de fora ou de dentro) ou por arestas (vizinhas se juntam num laço). Aresta aberta só "Sobre a linha". Z fundo nasce vazio.
- **S3 Bolsão com ilhas:** laços de dentro da face vão para o motor de ilhas existente; teste mostra a ilha respeitada no G-code e o filtro "Só o de fora" mudando o resultado.
- **S4 Assist:** `StepPlanSource` lê contorno externo, furos verticais (Ø e profundidade da malha) e rebaixos fechados com ilhas; `PlanInput.Depths` (opcional) usa a profundidade real. Sem o mapa o plano do DXF é idêntico (teste). Furo inclinado, curvas, faces para baixo e degraus na borda ficam de fora com aviso.

### Decisões minhas (revise)
- O menu da face do Trabalho passa a usar a regra da geometria para Faceamento e Bolsão com ilhas. O menu do Core (aba Modelo 3D) não mudou, porque `FaceMenuTests` testa as mensagens antigas.
- Aresta escolhida não muda de cor no 3D (a cena só pinta triângulos); a contagem aparece no aviso.

### Não foi feito (fica para outra ficha)
- **3D como tela principal do Trabalho** ao abrir STEP, e esvaziar a aba Modelo 3D (S0 do quadro).
- **"Reselecionar" como estado da operação:** hoje, se o STEP mudou e a face não é achada, a seção Geometria avisa e o contorno guardado continua; a operação **não** vira Incompleta.
- **Tela do Assist** "Peça STEP do Trabalho": não há imagem aprovada, então só Core + testes (como a ficha manda). O item 4 do "Pronto quando" não é testável ainda.
- Raio/arco: arcos da malha viram trechos retos (G1 curtos), como já era no M3D.

### O que o Igor testa no .exe
1. Trabalho novo → `Bolted_Flange.step`: clicar duas faces (Ctrl) → Bolsão; clicar a operação na árvore (faces acendem); Geometria → tirar/adicionar face; salvar, fechar, abrir.
2. Topo plano → Faceamento; digitar T, Ø, rotação; gerar G-code.
3. Duas ou mais arestas vizinhas (Ctrl) → Contorno; conferir fora/dentro e o aviso de aresta aberta.
4. Fundo de rebaixo com pino ou furo → Bolsão com ilhas; simular.
5. Abrir um trabalho DXF antigo e um STEP do M3D: mesmo programa de antes.

### Riscos
- Arquivo v6 não abre em programa antigo (recusa com aviso de versão mais nova): só vale para trabalho salvo com geometria do 3D.
- Impressão da face tem tolerância (centro 0,05 mm, tamanho 2%): peça muito alterada pode "perder" a face.
- O Assist com STEP não tem tela; nada disso aparece para o usuário ainda.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9DvfcaCHrqMX8PGTAMX
