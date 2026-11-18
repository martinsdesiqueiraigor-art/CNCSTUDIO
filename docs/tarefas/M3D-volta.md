## M3D: relatório (Modelo 3D no Trabalho)

Ramo `ajuste-M3D`, base `origin/dev` em `a63084a`. Executor: Claude (chat), em sprint, por ordem do Igor (18/11). Plano: `claude/plano-M3D-modelo-3d-no-trabalho-2026-11-18.md` (Projeto).
Conteúdo técnico em rascunho. Nada rodado no .exe nem em máquina; o STEP não pôde ser lido aqui (o leitor usa as DLLs do OpenCASCADE do Windows).

**Commits:** M3D-1 e M3D-2 juntos (dividem o mesmo arquivo do Core, `JobPart.cs`) · M3D-3.

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, **1952** (eram 1941). Build Release do Core e do App sem aviso (Linux).
- Teste mestre (8 G-codes): verde; nenhum G-code de referência mudou.
- Não executados: `CncStudio.Step.Tests` (STEP não foi tocado); nada no .exe.
- Testes alterados (autorizados pelo plano M3D aprovado pelo Igor em 18/11, que sobe o arquivo para a versão 4): o "arquivo de versão mais nova" passa de 4 para 5 em `CamJobStockShapeTests.Leitor_recusa_versao_maior_que_a_atual`, `CamJobTests.Versao_mais_nova_ou_dado_estranho_sao_recusados`, `CamJobProgramTests.Leitor_ate_v2_exige_os_dois_campos_e_v3_aceita_sem_eles` e `CamJobZeroTests.Leitor_recusa_versao_maior_e_abre_v3_com_e_sem_zero`.
- Teste novo: `M3DPartTests` (11).

### M3D-1 Peça STEP no Trabalho
- Preparação → Peça pronta: "Desenho DXF" ou "STEP da peça" (uma fonte só). Com operações nos contornos de uma fonte, trocar pede trabalho novo.
- O trabalho guarda caminho, caixa e volume da peça já girada, os giros (X/Y ±90°, Virar) e "topo em Z 0". Nunca lê o STEP ao abrir; arquivo sumido = aviso + "Localizar de novo" (tem de ser a mesma peça, mesma caixa, se já há operações).
- Girar só antes de criar operações (girar depois mudaria o lugar delas).
- "Usar a altura do STEP" copia a altura da caixa para o campo Altura da peça, à vista.
- A peça aparece no 3D do Trabalho (opaca, mesmo com o bruto transparente); o chip "Peça: Oculta" esconde.
- Arquivo: versão 4 só com peça STEP ou lista de furos; sem isso, sai igual (versões 1 a 3). App antigo recusa a versão 4 com aviso.

### M3D-2 Face vira operação
- No 3D do Trabalho, clicar numa face abre o menu (mesma regra do Modelo 3D): fundo plano para cima → Bolsão; furo vertical → Furação (Ctrl soma furos).
- Bolsão: a borda da face vira contorno do trabalho; Furação: cada furo vira um círculo, e uma operação fura só os escolhidos (lista de contornos, chave `contours` na operação).
- A operação nasce com T, Ø, rotação, avanços e o **Z do fundo vazios** (Incompleta até digitar). Z do topo = topo lido da malha; Z seguro e de aproximação com as mesmas folgas da operação nova da faixa, contadas a partir desse topo.
- Rótulos sem acento (vão como comentário no G-code): `BOLSAO 3D`, `FUROS 3D`.
- O G-code sai pelo mesmo gerador do Trabalho (bolsão e furação de sempre).
- Risco: contorno lido da malha é poligonal (arcos viram G1 curtos); a mensagem ao criar a operação avisa.

### M3D-3 Aba Modelo 3D (reduzido)
- Só um aviso na seção de furação e bolsão apontando para o Trabalho. Os botões e campos ficam: o menu da face da aba depende deles e eu não consigo testar a tela daqui. Tirar fica para o fecho da versão.

### Como validar no .exe (Trabalho fresa)
1. Trabalho novo → Preparação → Peça pronta → STEP da peça → `Bolted_Flange.step`. Ver o resumo; girar se precisar; "Usar a altura do STEP".
2. Ligar o 3D: ver a peça. Salvar, fechar, abrir: peça e giros voltam; com o 3D, "Mostrar a peça no 3D". Mover o STEP de pasta → aviso e "Localizar de novo".
3. Clicar no fundo de um rebaixo → Bolsão: operação na árvore, Incompleta; digitar T, Ø, rotação, avanços e Z do fundo; gerar.
4. Clicar num furo, Ctrl+clique em mais três → Furação: uma operação; preencher e conferir os 4 furos no código.
5. Abrir um trabalho antigo (DXF): mesmo programa de antes.
