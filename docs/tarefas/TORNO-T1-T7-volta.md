## TORNO-T1-T7: relatório (torno completo para teste: T1 a T5 e T7; T6 fora)

Ramo `ajuste-TORNO-T1-T7`, base `origin/dev` em `f28d593`.
- T1 a T3: Claude Code (Fable), 15/11.
- S0, T4, T5 e T7: Claude (chat), 18/11, em **sprint** pedido pelo Igor: fatias seguidas sem teste no .exe entre elas; o Igor testa tudo no fim. **T6 (2ª fixação) saiu por decisão do Igor (18/11).**
Conteúdo técnico em rascunho. Canal de face, sangramento e o Z deslocado da face oposta são G-code novo, **nunca rodado em máquina**.

**Commits (um por fatia):** T1 Preparação · T2 Canal de face · T3 Sangramento · S0 ajustes do #185 · T4 Fixação · T5 Z0 · T7 Pacote do torno.

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, **1941** (eram 1889 no `dev`). Build Release do Core e do App sem aviso (Linux).
- Teste mestre (8 G-codes): verde depois de cada fatia; nenhum G-code de referência mudou.
- Não executados: `CncStudio.Step.Tests` (STEP não foi tocado); nada no .exe; nenhuma foto de tela.
- Testes alterados:
  - `LathePaletteMissingTests` (3 testes da faixa, T1): autorizados pelo Igor em 15/11.
  - `LathePaletteMissingTests.Campos_vazios_nao_vao_para_o_arquivo` trocado por `Campos_vazios_vao_para_o_arquivo_e_trabalho_sem_eles_regrava_igual` (S0): autorizado pelo Igor em 18/11.
- Testes novos: `LatheT1PrepTests`, `LatheFaceGrooveTests`, `LathePartingTests`, `LatheExamplesTests`, `LatheT4FixationTests`, `LatheT5ZeroTests`, `LatheT7HandoffTests`.

### T1 Preparação (15/11)
Cartão Máquina; Programa nº, G5x e Refrigeração vazios no trabalho novo, travam Gerar e Enviar; operação nova nasce vazia; bruto do trabalho (Ø, comprimento, Z da face); Salvar não trava por campo vazio. Arquivo continua versão 1; chaves novas só quando têm valor.

### T2 Canal de face e T3 Sangramento (15/11)
G0/G1 iguais nos dois controles. Canal de face: X do programa = **aresta de maior Ø** do bedame (conferido no teste; mantido). Sangramento: avanço reduzido no Ø digitado, Ø final 0 com aviso de peça solta, limite de rotação acima do fuso não gera, nada gera depois dele.

### S0 Ajustes do #185 (18/11)
- "Rotação fixa (0 = G96)" nasce **vazia** na operação nova (antes nascia 0 = G96, um valor suposto). O usuário digita 0 ou a rotação. Com ferramenta copiada da operação anterior, vem dela.
- Tirado o parâmetro `keepEmptyFields`: o arquivo sempre guarda a lista de campos vazios; operação sem campo vazio não ganha a chave (arquivo antigo regrava igual).

### T4 Fixação (18/11)
Tipos novos "Castanha dura" e "Castanha mole" (arquivo: `castanha-dura`, `castanha-mole`); caixas "Contraponto" e "Luneta". Tudo informativo. Nomes antigos (`castanhas`, `placa`, `ponto`) ficam; `tailstock`/`steadyRest` só são gravados quando marcados.

### T5 Z0 (18/11)
- Cartão Zero com escolha: "Z0 do desenho do perfil" (padrão; arquivo antigo), "Face pronta" (início do perfil, maior Z) ou "Face oposta" (fim do perfil, menor Z). Chave `zeroFace` só quando escolhida.
- Os Z digitados nas operações continuam no desenho do perfil; **só o programa sai deslocado** (movimentos, Z seguro, contorno do G71/CYCLE95 e rosca), com o comentário `Z0 NA FACE PRONTA`/`OPOSTA` depois do G5x. Teste confere linha a linha que só os Z mudam.
- Face escolhida sem perfil trava Gerar ("Z0 na face escolhida precisa do perfil da peça").
- Risco: a face oposta é o fim do perfil desenhado; se o perfil não cobre a peça inteira, o Z0 cai no fim do perfil, não na face real.

### T7 Pacote do torno (18/11)
- Enviar ao Code do torno vai com o pacote `.casillas-cam` (versão 1, máquina `torno`): Programa nº, G5x, fixação em texto, barra no referencial do programa (segue o Z0 do T5) e as ferramentas das operações ligadas.
- Barra só vai com Ø, comprimento e Z da face do bruto preenchidos; senão o Code avisa e usa o bruto automático.
- Ferramenta de torno vai **sem Ø** (não muda a tabela no Code); tipo e raio de canto do banco ativo do torno quando o T está nele.
- O zero não vai como ponto (é da fresa): X0 no centro e o Z0 já estão no programa e na barra.
- Mudou de passagem: programa Siemens do torno vai ao Code com `.mpf` (antes `.nc`), como na fresa.

### Como validar no .exe (Trabalho torno)
1. Trabalho novo → Preparação: cartão Máquina; Programa nº, G5x e Refrigeração tracejados; Gerar travado; Salvar funciona. Digitar os três.
2. Bruto Ø 50 × 120, face em Z 2. Acrescentar Faceamento: Ø do bruto preenchido, **Rotação fixa vazia**; digitar e ver gerar.
3. Fixação: escolher "Castanha mole", marcar Contraponto e Luneta; salvar, fechar e abrir.
4. Zero: "Face oposta" → no Código NC, Z deslocados e a linha `Z0 NA FACE OPOSTA`; voltar a "Z0 do desenho do perfil" → programa igual ao de antes.
5. Canal de face e sangramento: abrir `exemplos/torno/*.casillas-job`, ver a prévia e o aviso de peça solta.
6. Enviar ao Code (com bruto preenchido): a barra aparece no simulador com as medidas do bruto; editar o texto → aviso de programa diferente.
7. Abrir `torno-eixo.casillas-job` antigo: mesmo programa de antes.
