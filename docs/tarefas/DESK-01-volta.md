## DESK-01: relatório (CAM e Code desktop: fresa com STEP, bruto e fixação, janelas de bruto e ferramentas)

Ramo `ajuste-DESK-01`, base `dev` em `78f77dc`. Executor: Claude (chat), em sprint, por ordem do Igor (18/11, noite), depois do teste no PC dele (teste automático do PR #194). Lista dos problemas: Projeto, `claude/problemas-teste-2026-11-18.md`.

**Commits:** A (fresa com STEP) · B (bruto e fixação do CAM ao Code) · testes do B · C (janelas do Code desktop) · D (achados do teste no PC do Igor, 19/11).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 2012 (22 novos em `Desk01StepPartTests`, `Desk01HandoffTests` e `MachineExecutorTests`). Build Release do App e da web sem erro.
- Teste no PC do Igor (teste automático do PR, commit antes do D), pelo Claude: fresa com STEP (altura 26, bruto 104 × 104 × 28 em volta da flange, zero no centro, furação com Z fundo −26, `(BRUTO 104 X 103.995 X 28)`), torno (pendência do bruto, `(BRUTO D30 X 125)`), janelas novas do Code. Os achados viraram o D.

### Os 8 G-codes de referência e os testes que mudaram (autorizado pelo Igor em 18/11: "os G-codes ganham a linha bruto")
- **Só a linha `(BRUTO ...)` entra** (nenhum movimento muda): `fresa-3mais2`, `fresa-bloco-siemens`, `fresa-fixacao`, `fresa-texto-cilindro`, `fresa-zero-g55-canto` e os exemplos de torno `canal-de-face` e `sangramento` (Fanuc e Siemens). `fresa-sem-bruto`, `fresa-zero-centro-base` e `torno-eixo` não mudam (sem bruto completo).
- Testes que comparavam "o bruto não muda o programa" passam a comparar sem a linha do bruto: `CamJobStockShapeTests.Bruto_vazio_ou_incompleto_gera_o_mesmo_programa_e_salva`, `CamJobTests.Job_gera_o_mesmo_programa_que_o_fluxo_direto`, `CamHandoffTests.Montar_o_pacote_nao_muda_o_programa`, `CamJobZeroTests.Linha_do_zero_no_cabecalho_so_quando_escolhido`, `LatheT1PrepTests.Bruto_do_trabalho_nao_muda_o_programa_nem_as_operacoes_antigas`.
- `LatheT1PrepTests.Trabalho_novo_nasce_sem_programa_g5x_e_refrigeracao_e_nao_gera`: o bruto vazio do torno virou pendência.
- `CamHandoffCodeTests.Comentario_BRUTO_vence_o_do_CAM_e_avisa_se_diferente`: comentário com as mesmas medidas do CAM deixa entrar o bruto do CAM (posição exata).
- `M3DPartTests` (2): o Z fundo do bolsão e da furação pelo 3D vem preenchido do modelo.
- `CamPaletteTests`: a faixa Arquivo ganhou "Novo".
- `CamJobShapeTests.Pendencia_da_peca_cita_a_forma`: o texto cita o STEP.
- `MachineExecutorTests.G53EG04NaoGeramMovimento`: depois do G53 com Z o movimento em X não é desenhado até o próximo Z (D1).

### A. Fresa com STEP
1. **A peça STEP vale sozinha**: a caixa do STEP é a peça para o bruto por sobra, o zero e as pendências; os círculos e contornos que o 3D cria para as operações não mudam mais o bruto (antes virava um cubinho 13 × 13 em volta do furo).
2. **Bruto que não envolve a peça vira pendência** (com as caixas da peça e do bruto no texto), e o bruto STEP igual ao arquivo da peça também ("sem sobremetal"). Vale na árvore e no rodapé.
3. **Z fundo do modelo**: bolsão (Z da face) e furação (fundo do furo, quando todos os furos têm o mesmo fundo) nascem preenchidos; o aviso da operação diz "contorno e Z fundo lidos da malha: confira".
4. **Altura da peça** vem do STEP ao abrir, quando vazia; o 3D liga sozinho e enquadra a peça.
5. **Novo** na faixa Arquivo (fresa e torno): com trabalho aberto, o primeiro clique avisa e o segundo (até 6 s) descarta.
6. Abrir um trabalho de torno agora traz também a face do zero (antes ficava a do trabalho anterior).

### B. Bruto e fixação do CAM ao Code
1. **O CAM escreve o bruto no programa**, logo depois do zero: `(BRUTO 100 X 80 X 20)`, `(BRUTO D50 X 30)`, `(BRUTO D50 X D30 X 30)`, no torno `(BRUTO D30 X 125)`. Cilindro deitado e STEP não viram comentário (vão só no pacote).
2. **Bruto do torno incompleto** (Ø, comprimento ou Z da face) é pendência e trava Gerar e Enviar.
3. **Placa e contraponto do CAM no Code**: o pacote leva Ø e comprimento preso, balanço e contraponto (`fixacaoTorno`); o Code monta a placa com o comprimento preso e desenha o contraponto na face. Só quando o setup não tem fixação.

### C. Code desktop
1. **Janela do bruto** igual ao setup da web: com comentário, "Do programa" ou "Outro bruto"; sem comentário, "Automático" ou "Informado". Fresa: bloco, redondo, tubo (em pé ou deitados) e sextavado; torno: barra redonda, tubo, quadrada/retangular e sextavada com a face em Z. Campo vazio não salva.
2. **O BRUTO do programa manda também no desktop**, na posição do zero; o mesmo bruto já no setup (o do CAM) fica com a posição exata; "Outro bruto" mostra aviso fixo na barra de status.
3. **Janela de ferramentas**: lista as ferramentas do programa com a origem da medida (deste programa, CAM, banco, comentário ou "sem medida"); "Medida deste programa" grava tipo, Ø, raio de canto ou da ponta e largura do bedame no setup. O antigo "Diâmetro padrão" ficou embaixo, como "Ø só para desenhar ferramenta sem medida".

### D. Achados do teste no PC (19/11)
1. **G28/G30/G53 com Z:** o Z vai a uma altura de máquina que o programa não diz. Até o próximo bloco com Z, os movimentos em X/Y não são desenhados (nem contam como rápido no material), e o bloco que devolve o Z também não. Antes a ferramenta ficava em Z0 e o primeiro rápido do CAM (`G91 G28 Z0` → `G0 X Y`) passava por dentro do bruto com sobra em cima. Girar o eixo rotativo no lugar continua desenhado.
2. **Enviar ao Code vale por cima do setup salvo com o mesmo nome:** o bruto do CAM e, no torno, a placa e o contraponto entram (as ferramentas do setup ficam). Antes, um TORNO.nc antigo segurava o bruto e a placa velhos.
3. **Torno lê só o banco do torno** na janela de ferramentas e na folha de setup (mostra o raio da ponta, não um Ø). Antes o T1 da fresa (Ø20) aparecia no torno.
4. "Entre pontas" marca o contraponto (dá para desmarcar). O item "Peça: Sólida" diz que é dos contornos do DXF (a peça STEP já aparece sólida).

### E (teste do Igor: contraponto não aparecia)
- O contraponto do CAM era desenhado no eixo do torno sem passar para a cena: ficava em pé na ponta da barra, escondido atrás do suporte. Agora fica deitado no eixo, saindo da face. Teste novo `Desk01TailstockSceneTests` (falha sem a correção).

### Ficou para depois
- O aviso de G28/G30 continua na lista (a contagem de avisos não mudou).
- Furação pelo 3D de furo passante vai até a base exata (sem passar a ponta): conferir o Z fundo.
- Ferramenta encostada na face no começo da simulação do torno.
- O zero do CAM num canto é o da peça; o comentário BRUTO não leva posição, e a web posiciona pelo zero (no torno, a face em Z0). O desktop usa o pacote do CAM, que tem a posição exata.

### Teste do Igor (teste automático do PR)
1. Fresa: Novo → STEP da peça → bruto por sobra 2 mm → clicar num furo → Furação: bruto em volta da flange, Z fundo preenchido.
2. Fresa: bruto STEP deslocado da peça → pendência "Bruto não envolve a peça".
3. Torno: bruto sem comprimento → pendência; completo → o G-code tem `(BRUTO D.. X ..)`; Enviar ao Code → placa com o comprimento preso e o contraponto.
4. Code: Bruto (janela nova) e Configuração da máquina → Ferramentas.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dz8ujacpQtkJpoFA8D
