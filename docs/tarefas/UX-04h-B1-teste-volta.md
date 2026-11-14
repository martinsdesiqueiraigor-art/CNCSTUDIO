```
ID: UX-04h (B1, teste)
Commit testado (origin/main): e738a5b (Merge #170), igual ao da ficha.
Pasta de trabalho usada: C:\Projetos\CncStudio-teste-b1 (worktree novo, --detach na origin/main; nenhuma outra pasta mudou de ramo).
Dados reais: não havia banco em disco (%APPDATA%\CncStudio\ferramentas não existia); config.json sem ToolBanksMigrated. O App não aceita pasta de dados alternativa (caminho fixo em AppSettings), então: cópia em C:\Projetos\backup\teste-b1-2026-11-14\config.json (conferida por SHA-256), teste nos dados reais com um config.json de teste e restauração no fim.
Como testou: build Release local da main + roteiro headless (Avalonia.Headless, fora do repositório, no scratchpad) que abre a MainWindow real em 1280×720, vai a CAM → Ferramentas, clica nos botões e lê o estado da tela e os arquivos em disco. Fotos headless 1280×720. NÃO usei o .exe à mão. Tabela de teste: T1 Ø20, T10 Ø5, T3 Ø10 fresa de topo, T7 bedame inserto 3, T5 tipo "Xyz", T150 torno, T1 repetida.
Itens 1 a 14:
1. passou · roteiro: 2 bancos (fresa 4, torno 1); T3 Ø10 e bedame T7 (inserto 3) "a conferir"; lista "Não entrou no banco" com 3 avisos até "Entendi" (some e não volta). Foto 06. Com a tabela real do Igor: fresa T1 Ø20 e T10 Ø5 (as duas Incompletas, sem tipo), torno vazio, sem avisos.
2. passou · roteiro: a lista Tools do config.json é idêntica antes e depois de migrar, editar e fechar o App (2 aberturas). Só ToolBanksMigrated e ToolBankNotes mudam.
3. passou · roteiro: na 2ª abertura a T10 migrada (excluída na 1ª) não voltou.
4. passou · roteiro: criei T20 (Fresa de topo, Ø8), editei para "8,5" com vírgula, Aplicar grava (arquivo .casillas-ferramentas tem 8.5); na 2ª abertura a T20 continua com Ø 8,5. Foto 02.
5. passou · roteiro: T20 duplicada foi para T21 "Fresa topo teste (cópia)".
6. passou · roteiro: excluí T21 e T10 com a confirmação "Sim, excluir Tn".
7. passou · roteiro: T21 inativa grava active=false, linha com opacidade 0,5, aparece só na aba "Inativas (1)"; nova ferramenta com T21 recusada ("T21 já existe neste banco."); reativar grava. Foto 03.
8. passou · roteiro: T20 repetido recusado com "✕ T20 já existe neste banco." na barra. Foto 05.
9. passou · roteiro: Completa (T20), Incompleta (T1, T5, T30) e "a conferir" (T3) na lista; T30 salva com tudo vazio (não bloqueia); células vazias mostram "ausente". Foto 04.
10. passou, em parte · chamada direta do Core (ToolBankStore.Export + ToolBankImport.Import, o mesmo código dos botões), sem o seletor de arquivo (o headless não abre seletor): volta como "Ferramentas fresa (cópia)", id ferramentas-fresa-2; o arquivo original não mudou. Botões Exportar/Importar no .exe: não conferido.
11. passou · roteiro: "T3" e "3" → T3; "topo" → T20; "8,5" → T20; "10" → T3 (Ø). Abas: Todas, Fresa de topo, Sem tipo, Inativas; cada aba filtra certo.
12. passou, em parte · roteiro: Ø que o simulador usa (AppSettings.DiameterOf, que lê o banco) é igual ao da tabela antiga em todos os T; Ø 0 cai no padrão (6), como antes. Folha de setup: só leitura do código (lê o mesmo ToolBankLookup.Tools); não abri a folha.
13. passou, em parte · teste automático: CamJobGoldenTests (8 trabalhos de fresa + torno antigo, abrindo .casillas-job antigos) passaram. Abrir um .casillas-job antigo no .exe: não conferido.
14. passou com 1 defeito · foto e medida: em 1280×720 nada passa da borda (maior x de botão 1192 de 1204), sem barra de rolagem extra; mas lista longa do que não migrou corta (defeito 1).
Defeitos achados (passo a passo, sem corrigir):
1. Lista do que não migrou sem rolagem: config.json com 14 linhas de tipo desconhecido (Kind "Xyz", T1 a T14), sem pasta ferramentas → abrir CAM → Ferramentas em 1280×720: a caixa vermelha passa da tela e o botão "Entendi" fica fora, sem como dispensar. Com 8 avisos ainda cabe. Foto 08.
2. Texto: avisos de T que ENTROU no banco aparecem sob "Não entrou no banco:" ("T5: tipo "Xyz" desconhecido; ficou sem tipo." e "T1 aparece mais de uma vez; ficou a última linha."). Sugestão de título: "Avisos da migração". Foto 06.
Testes automáticos: rodados no build local da main / 1805 passaram, 0 falharam (dotnet test, Release).
Workflow disparado pelo push: Prévia (previa.yml, ramo ajuste-*). CI (ci.yml) não, ele é só da main.
Dados do Igor restaurados? sim · config.json igual à cópia (SHA-256 08bb565e…0833, 3002 bytes); pasta ferramentas apagada (não existia antes).
Não conferido: .exe à mão; seletor de arquivo do Exportar/Importar; folha de setup aberta na tela; .casillas-job antigo aberto no .exe; o Trabalho do CAM (B2, ainda lê a tabela antiga).
O que o Igor deve olhar nas fotos: 01/04 lista com os 3 estados (mesma cena); 02 painel da ferramenta (desenho, abas, Ativa/Inativa); 03 inativa esmaecida na aba Inativas; 05 mensagem de T repetido; 06 caixa "Não entrou" e o título (defeito 2); 07 o botão Ferramentas no topo do CAM, ao lado de Fresa e Torno; 08 defeito da lista longa.
Próximo passo sugerido (1): corrigir os defeitos 1 e 2 num ajuste pequeno (rolagem na coluna da esquerda + título "Avisos da migração") antes do B2.
```
