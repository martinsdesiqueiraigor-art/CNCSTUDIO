# UX-04h PR D1 — Volta (14/11/2026)

```
ID: UX-04h (PR D1, torno no banco ativo)
O que foi feito (até 5 linhas):
  Banco ativo do torno (ActiveLatheBankId no config.json; sem escolha ou banco apagado: o Primary, igual a hoje). O Code lê esse banco
  no Ø do torno, no desenho da ferramenta do torno e na folha de setup. Trabalho do torno: seletor "Banco ativo" à direita da faixa e
  escolha só com as ativas (copia T, nome e rpm se cadastrado). O Enviar ao Code do torno não grava mais na tabela antiga.
  Migração única (marca própria LatheToolsMigrated) pela regra da ficha, com a opção (a) do Igor para T repetido.
Arquivos alterados/criados:
  Core: Cam/ToolBankLatheMigration.cs (novo), Cam/ToolBankLookup.cs, Cam/ToolBankPick.cs (ActiveLatheBank, LatheLabel, ApplyLathe,
  nomes dos tipos de torno), Cam/ToolBankImport.cs (conversão de uma linha separada, a mesma do B1). Testes: ToolBankLatheMigrationTests.cs (novo).
  App: Settings/AppSettings.cs, MainWindow.ToolBank.cs, MainWindow.Modules.cs, MainWindow.axaml.cs, Modules/CamView.axaml.cs,
  Modules/LatheJobView.axaml(.cs), Modules/LatheJobView.Props.cs. Docs: docs/ESTADO.md, esta Volta, fotos em docs/tarefas/UX-04h-D1/.
Testes: rodados (dotnet test, Release, local), todos verdes / CamJobGoldenTests 8 de 8 antes de mudar código (base 3b98343) e
  depois / web compila (Release), build sem aviso. Testes novos (19): T livre; T igual com a mesma ferramenta; T igual com outra
  (vai ao T livre, com aviso); mesmo nome e outro tipo; banco cheio; T fora da faixa; linhas de fresa fora; T repetido na tabela;
  segunda passada não duplica; grava no banco ativo; segunda abertura não repete; sem linha de torno; sem banco de torno (cria
  "Ferramentas torno"); falha no meio (temporário não grava: sem marca, arquivo anterior inteiro, tenta de novo e grava); banco só
  leitura; banco ativo do torno no Ø e no desenho; fresa por cima no mesmo T; escolher copia T, nome e rpm; texto da escolha.
T repetido e ferramenta sem T (conferência da seção 2):
  O banco não aceita nenhum dos dois: T é único no banco (Validate recusa e o arquivo com T repetido não abre) e o T é obrigatório
  (1 a 99 no torno). Parei e perguntei; o Igor escolheu a opção (a): a ferramenta diferente entra no próximo T livre, "a conferir",
  com aviso "Posição T5 repetida: "Rosca 60" (rosca-externa) veio da tabela antiga como T5, que no banco "Ferramentas torno" já é
  "Desbaste CNMG" (desbaste-externo). Entrou como T7, a conferir." O formato do banco não mudou.
Regra da migração, como ficou:
  Só as linhas de tipo de torno (fresa, broca e sem tipo já foram ao banco de fresa no B1). Vai ao banco ativo do torno; sem banco
  de torno, cria o "Ferramentas torno". Todas entram "a conferir". A mesma ferramenta = mesmo nome (sem diferença de maiúsculas e
  espaços nas pontas) e mesmo tipo. Se ela já está no banco em outra posição, não entra de novo (isso evita duplicar se a marca não
  gravar). T repetido na própria tabela: a última linha fica com o T, a outra vai ao livre. Banco cheio ou T acima de 99: não entra,
  com aviso, e continua na tabela antiga. Avisos e um resumo ("2 ferramentas entraram, a conferir (T2, T9)") vão ao painel
  "Avisos da migração". Banco gravado por arquivo temporário trocado no fim; a marca só é gravada se o banco gravou.
Fotos 1280×720 (arquivos em docs/tarefas/UX-04h-D1/):
  01-seletor-banco-ativo, 02-escolha-do-banco (lista aberta: T7 inativa fora, "a conferir" marcado), 03-escolhido-T3 (T, nome e
  rpm 800 copiados), 04-outro-banco-ativo. Roteiro headless fora do repositório: trilho de 76 px + CamView, bancos só na memória.
Decisões tomadas: (só as autorizadas)
  Opção (a) do Igor. Propostas (Igor ajusta): rótulo e lugar do seletor iguais aos da fresa; o texto da escolha do torno não mostra
  o Ø (a operação de torno não usa) e mostra o inserto nos bedames; trocar o banco ativo do torno refaz a análise do Code na hora.
Riscos ou contradições encontradas:
  - Ferramenta que já estava no banco e teve o nome editado pelo Igor conta como "outra": entra de novo no T livre, com aviso.
  - Duas ferramentas físicas com o mesmo nome e tipo em posições diferentes contam como a mesma (regra da ficha).
  - Mesmo T na fresa e no torno: a fresa continua valendo por cima no Ø do Code (não mudei, seção 4 da ficha).
  - Em 1280 a faixa do torno rola: o fim do grupo Rosca fica depois da rolagem (o mesmo efeito do seletor na fresa, B2).
  - Trocar o banco ativo do torno não avisa quando a ferramenta da operação não está no novo banco (o aviso do B2 não veio, seção 4).
Não conferido: .exe; abrir com o config.json e os bancos reais do Igor; abrir e fechar o App de verdade; Prévia; máquina.
  Nada foi testado em máquina.
O que o Igor deve testar na prévia:
  Abrir com o config.json de hoje e ver as ferramentas de torno no banco, "a conferir", com os avisos no painel; escolher no Trabalho
  do torno (só ativas; T, nome e rpm); trocar o banco ativo e ver o Ø e o desenho no Code; programa de torno antigo simula igual.
Próximo passo sugerido (1): D2 (tirar a tabela antiga do config.json), conforme as Decisões 1 e 2 do plano.
```
