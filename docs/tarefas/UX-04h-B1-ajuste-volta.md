```
ID: UX-04h (B1, ajuste)
Base conferida (origin/dev): a44fdf9 (Merge #168). Não é o hash da ficha (e738a5b é da origin/main, Merge #170), mas o conteúdo é idêntico (git diff origin/main origin/dev vazio). Ramo ajuste-ux04h-avisos-migracao em pasta nova C:\Projetos\CncStudio-ajuste-b1 (worktree); nenhuma outra pasta mudou de ramo.
O que foi feito (até 5 linhas):
1. Coluna da esquerda do banco: o pé (faixas de T + avisos) passou de StackPanel para DockPanel alinhado embaixo, ocupando só o que sobra abaixo da lista de bancos.
2. "Entendi" preso embaixo; a caixa vermelha fica num ScrollViewer (rolagem vertical própria) que encolhe para caber. Com poucos avisos nada muda de lugar.
3. Título "Não entrou no banco:" → "Avisos da migração:". Textos de cada aviso iguais.
4. ESTADO.md: itens do #170 movidos para "Na main" e base atualizada.
Arquivos alterados: src/CncStudio.App/Modules/ToolBankView.cs, docs/ESTADO.md, docs/tarefas/UX-04h-B1-ajuste-volta.md, docs/tarefas/UX-04h-B1-ajuste/01-14-avisos.png e 02-3-avisos.png.
Linhas de código alteradas (aprox.): ~30 (ToolBankView.cs, +26 −10).
Testes: rodados (dotnet test, Release, local) / 1805 passaram, 0 falharam. Teste automático novo: não coube (o projeto de testes é só do Core; a tela foi conferida pelo roteiro headless abaixo).
8 G-codes de referência idênticos? sim · CamJobGoldenTests 8/8 antes de mudar e de novo na suíte inteira depois.
ESTADO.md: linha de base (origin/dev a44fdf9 = origin/main e738a5b, #170); "Na main" ganhou 2 linhas (UX-04e, UX-04f, UX-04g, CAM-13 pelo #170; UX-04h PR A e B1, com o B2 pendente); saíram de "Em ramo" os 5 itens movidos; linha deste ajuste em "Em ramo"; linha "PRs abertos" sem os PRs que já entraram. Arquivo com 87 linhas.
Número do PR e workflows que rodaram: #171 (rascunho, para o dev). Push disparou Prévia (previa.yml) e Automatic Dependency Submission (NuGet); ci.yml é só da main.
Dados do Igor restaurados? sim · cópia em C:\Projetos\backup\ajuste-b1-2026-11-14\config.json antes (SHA-256 08bb565e…0833, igual ao original); no fim config.json restaurado com o mesmo SHA-256 e pasta ferramentas apagada (não existia antes).
Como conferi a tela: roteiro headless (Avalonia.Headless, fora do repositório) que monta o config.json do cenário, abre a MainWindow real em 1280×720, vai a CAM → Ferramentas, mede as posições e tira a foto.
- 14 avisos (14 linhas Kind "Xyz", foto 01): caixa em y 299–674 com rolagem (conteúdo 489 px em 375 px visíveis); "Entendi" em y 680–710, dentro da janela; "+ Novo banco", faixas de T e os 2 bancos visíveis, sem sobreposição; "Entendi" dispensa a lista.
- 3 avisos (tabela do teste do B1, foto 02): sem rolagem; posições iguais às da foto 06 do teste (faixas de T em y 374, caixa a conferir e caixa vermelha no mesmo lugar, "Entendi" em y 680); só o título mudou. Os nomes das ferramentas na lista diferem da foto 06 porque montei a tabela sem as descrições.
Não conferido: .exe à mão (só headless); outras resoluções além de 1280×720; caso com muitos bancos na lista (a lista de bancos não rola e, se crescer muito, aperta a caixa de avisos: comportamento de antes, fora deste ajuste); resultado da Prévia no GitHub (estava rodando ao escrever).
Achados fora do escopo (sem corrigir): no ESTADO.md, CAM-09, CAM-11, UX-03a, CAM-12 e UX-03b seguem em "Em ramo", mas já estão na main desde o #158; PLAT-08 (#169) e o CI só na main (#168) entraram pelo #170 e não aparecem no ESTADO.md.
O que o Igor deve olhar na prévia: CAM → Ferramentas com uma tabela antiga de muitas linhas (ou o .exe da Prévia): a caixa vermelha rola e o "Entendi" aparece embaixo; título "Avisos da migração:".
Próximo passo sugerido (1): mesclar este ajuste no dev e seguir para o UX-04h B2 (o Trabalho do CAM passa a ler o banco).
```
