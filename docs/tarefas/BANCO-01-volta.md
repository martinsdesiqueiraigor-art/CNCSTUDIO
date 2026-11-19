## BANCO-01: relatório (banco de ferramentas funcional, sem o armário)

Ramo `ajuste-BANCO-01`, saído do `ajuste-DESK-01` (PR #195): **depende do #195** (mesclar o #195 antes). Executor: Claude (chat), por ordem do Igor (19/11): "resolver o conflito de banco de ferramentas de um modo funcional, sem o armário". O T continua sendo a posição no magazine; nada aqui atrapalha o armário depois.

**Testes:** `dotnet test tests/CncStudio.Core.Tests` em Release, 2014 aprovados (2 novos em `Banco01Tests`). Build Release do App sem erro. O Igor testa (teste automático do PR).

### O que mudou
1. **Cada programa lê só o banco da sua máquina.** O Ø do simulador da fresa vem só do banco ativo da fresa (antes um T só do torno dava Ø a uma fresa); torno lê só o banco do torno na janela de ferramentas e na folha (já no #195).
2. **CAM: conflito entre o trabalho e o banco com um clique.** No aviso "T1 no banco é diferente do trabalho", os botões **Usar a do banco** (a operação copia T, Ø, nome, rotação e avanços do banco) e **Atualizar o banco com esta** (o banco passa a ter o Ø e o nome da operação, "a conferir"; tipo, arestas e o resto do cadastro ficam). T fora do banco continua com "Adicionar ao banco ativo".
3. **Code: conflito do Ø do CAM com o banco com um clique.** O aviso "T1: o CAM usou Ø 9 e o banco tem Ø 20" ganhou **Atualizar o banco** (só os T que já estão no banco, "a conferir"). O programa continua valendo pelo CAM, como antes.
4. **Operação nova vazia não trava mais o Salvar** (continua travando Gerar e Enviar). O arquivo guarda a lista dos campos vazios da operação (`empty`) e sai na **versão 5** só quando há campo vazio; sem campo vazio, o arquivo é o de antes. Valor inválido (ex.: T 1,5) continua travando o Salvar.
5. Ferramenta sem Ø no banco já aparecia "sem Ø" na escolha (conferido; nada mudou).

### Testes existentes que mudaram (regra nova do item 4)
- `CamJobMissingFieldTests`: `Campo_vazio_deixa_Incompleta_bloqueia_gerar_e_salvar_...` (Salvar não trava), `Operacao_desligada_com_campo_vazio_gera_e_salva` (era "... mas não salva"), `Campos_vazios_nao_vao_para_o_arquivo_...` (agora vão, na versão 5).
- Versão futura nos testes de leitura passou de 5 para 6: `CamJobTests`, `CamJobProgramTests`, `CamJobZeroTests`, `CamJobStockShapeTests`.
- Os 8 G-codes de referência: sem mudança neste PR.

### Teste do Igor
1. CAM fresa: operação com T do banco e Ø diferente → "Atualizar o banco com esta" → o aviso some; "Usar a do banco" → a operação fica igual ao banco.
2. Code: programa do CAM com Ø diferente do banco → "Atualizar o banco" no aviso.
3. CAM: operação nova sem preencher → Salvar funciona; abrir de novo → os campos continuam vazios; Gerar continua travado.
4. Torno: T1 no banco de fresa e no de torno → o torno mostra só a do torno.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01B7m9Dz8ujacpQtkJpoFA8D
