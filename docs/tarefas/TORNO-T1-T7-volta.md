## TORNO-T1-T7: relatório (parcial: T1, T2 e T3 prontos; T4 a T7 não começados)

Ramo `ajuste-TORNO-T1-T7`, base `origin/dev` em `f28d593`. Modelo: Fable (ordem do Igor, teste de desempenho).
**Onde parou e por quê:** depois do T3, em commit limpo. O limite de uso da sessão chegou a 80% (Fable da semana em 86%); pela regra da ficha, parei e abri o PR com o que está pronto. T4, T5, T6 e T7 não têm nenhuma linha de código.
Conteúdo técnico em rascunho. Canal de face e sangramento são G-code novo, **nunca rodado em máquina**.

**Feito:** T1 Preparação, T2 Canal de face, T3 Sangramento (um commit cada).
**Arquivos:** Core `Cam/LatheJob.cs`, `LatheJobJson.cs`, `CamPalette.cs` (itens do torno), novos `LatheJobProgram.cs`, `LatheFaceGroove.cs`, `LatheParting.cs`. App `Modules/LatheJobView.axaml`, `.axaml.cs`, `.Props.cs`, novo `LatheJobView.Prep.cs`, `CamView.axaml.cs` (só a ligação do cartão Máquina do torno). Testes novos: `LatheT1PrepTests`, `LatheFaceGrooveTests`, `LathePartingTests`, `LatheExamplesTests`. Exemplos: `exemplos/torno/` (4 trabalhos e os 4 programas).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1921 (eram 1889 na base). Build Release sem aviso.
- Reprovados: nenhum.
- Não executados: `CncStudio.Step.Tests` (nada do STEP foi tocado); nenhuma foto de tela; nada no .exe.
- Teste mestre (8 G-codes): verde depois de cada fatia.
- Testes alterados (autorizado pelo Igor na conversa de 15/11, "ajustar só esses"), todos em `LathePaletteMissingTests.cs`:
  - `Grupos_do_torno_na_ordem_e_em_breve_apagados`: a lista "em breve" ficou `nova-fixacao` e `zero` (saíram `canal-face` e `sangrar`).
  - `Faixa_do_torno_tem_cada_operacao_do_Core_uma_vez`: conta as 8 antigas mais `LatheJobInfo.AddedKinds` (2 novas).
  - `Botao_cria_a_mesma_operacao_da_lista_antiga_e_o_icone_volta`: o botão cria a operação vazia (`NewBlank`).
  - Ainda **não** alterados: `LatheJobTests` e a versão 1 em `LatheFixationTests` (seriam do T6).

### T1 Preparação
- Feito: cartão Máquina na Preparação (escolher, criar, editar; mesmos perfis da fresa). Programa nº, G5x e Refrigeração no cartão da Fixação, vazios no trabalho novo, com tracejado e motivo; vazio trava Gerar e Enviar. Operação nova nasce vazia. Bruto do trabalho digitado (Ø, comprimento, Z da face). Salvar não trava mais por campo vazio.
- Arquivo: versão 1. `programNumber`, `workOffset`, `coolant` e `stock` só são gravados quando têm valor; cada operação ganha `"missing": [...]` com os campos vazios. Trabalho antigo abre e regrava idêntico.
- Decisões que tomei e o Igor pode vetar: (1) terceiro campo no bruto, "Z da face do bruto"; (2) "Rotação fixa (0 = G96)" nasce 0, não vazia; (3) no Core, `new LatheJob()` e `NewOperation` continuam com os valores antigos e a tela usa `LatheJob.Blank()` e `NewBlank` (para não mexer em mais testes).
- **Pendência para decidir:** o teste antigo `Campos_vazios_nao_vao_para_o_arquivo` contradiz "Salvar com campo vazio" e não estava na lista autorizada. Deixei-o verde com um parâmetro: `LatheJobJson.ToJson(job)` continua sem a lista; a tela chama `ToJson(job, keepEmptyFields: true)`. Sugiro autorizar a troca do teste e tirar o parâmetro.
- Risco: um app antigo que abrir arquivo novo sem `programNumber` mostra erro de campo ausente (a versão continua 1 até o T6).
- Corrigi de passagem: os campos do painel gravavam com os valores da hora em que o painel foi montado (digitar um campo podia desfazer o anterior); agora usam o valor atual da operação.

### T2 Canal de face
- Feito: operação nova, botão ativo, G0/G1 igual nos dois controles: mergulhos em Z com quebra-cavaco do Ø externo para o interno (passo de 80% da largura), acabamento das paredes e do fundo. Campos da ficha, todos digitados; faixa de Ø do bedame obrigatória. A largura vem do inserto do bedame ao escolher a ferramenta no banco (só leitura do banco).
- Convenções a conferir: X do programa = aresta de **maior Ø** do bedame; a faixa de Ø do bedame é conferida contra o Ø externo do canal; o avanço reduzido vale nos cortes cujo destino tem Ø menor ou igual ao digitado (o arraste final pelo fundo sai todo no reduzido).
- Não conferido: tela no .exe; o aviso da convenção de X aparece na tela, não como comentário no programa.

### T3 Sangramento
- Feito: operação nova, G1 radial no Z do corte até o Ø final, troca para o avanço reduzido exatamente no Ø digitado, mergulho e recuo. Ø final 0 mostra o aviso fixo de peça solta (vermelho no painel e na lista de avisos). Avanço reduzido e limite de rotação obrigatórios; com perfil de máquina, limite acima do fuso **não gera** (nas outras operações segue só aviso). Operação ligada depois do sangramento não gera; a faixa não deixa acrescentar, e Subir/Descer/Duplicar não deixam passar dele.
- Não conferido: tela no .exe; a queda da peça não é simulada.

### T4 a T7
Não feitos. O botão "Nova fixação" e "Zero" continuam "em breve". O plano aprovado segue em `C:\Users\Usuario\.claude\plans\tender-spinning-matsumoto.md` (fora do repositório).

**Limitações e pendências:** fatias com mais de ~800 linhas: nenhuma (T1 ~900 contando testes e a cópia do cartão Máquina; aviso). `docs/ESTADO.md` não foi atualizado (fica para o fecho da missão).

**Como validar (itens 1 a 4 e 9 da ficha):** no .exe da Prévia, Trabalho torno.
1. Trabalho novo → Preparação: cartão Máquina; Programa nº, G5x e Refrigeração tracejados; Gerar travado; Salvar funciona. Digitar os três e Gerar.
2. Digitar o Bruto; acrescentar Faceamento: nasce com o Ø do bruto e o resto vazio.
3. Canal de face: campo vazio mostra "Falta ..."; abrir `exemplos/torno/canal-de-face-fanuc.casillas-job`, ver a prévia e Enviar ao Code; repetir com o `-siemens`.
4. Abrir `exemplos/torno/sangramento-fanuc.casillas-job`: aviso de peça solta; tentar acrescentar operação (bloqueia); simular no Code.
5. Abrir `torno-eixo.casillas-job` antigo: mesmo programa de antes.
