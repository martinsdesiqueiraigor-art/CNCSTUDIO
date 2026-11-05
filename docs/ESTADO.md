# Estado atual

Atualizado em 05/11/2026 · base: `origin/main` em `5add2ce` (merge do PR #143, STEP).
Histórico completo em docs/historico/. Ler só se a tarefa pedir.
Fonte deste resumo: `docs/historico/ESTADO-ate-2026-11-02.md` (citado como H:linha) e `git log`.

## Na main (datas = merges no git log)
- **Code (web e desktop):** simulação 2D/3D, comparação, painel de controle, tema CMYK, teclado do editor e balão do código na web, faixa Renumerar/Formatar/Desfazer na web (H:5-38, H:406). Merges de 18/10 a 30/10 (#105).
- **Plataforma web:** tela inicial com cartões, Configurações, Novidades (lista vazia), conta única com teste de 30 dias e travas por módulo (H:22, H:394, H:444). Merges de 24/10 a 02/11 (#117).
- **Macro:** geradores no Core, tela web renovada e módulo nativo no desktop (H:15, H:24, H:50, H:402). Merges de 24/10 a 30/10 (#106).
- **Otimizador:** Core, tela web com aviso de economia de linhas, módulo nativo no desktop (H:21, H:48, H:400). Merges de 24/10 a 30/10 (#107).
- **Academy (só web):** leitor v1 e v2, tela nova; módulo 1 em v1, módulo 2 em v2, conteúdo em rascunho (H:52, H:415, H:428-435). Merges de 24/10 a 02/11 (#109, #110, #116, #118).
- **CAM desktop:** Trabalho fresa e torno (árvore, propriedades, barra de pendências, Bruto, Origem, Máquina, Fixação, desfazer, 3D), DXF com unidade, bolsão com ilhas, torno (rosca, canal, rebaixo), 4º eixo, 3+2 na mesa dupla (só Core e Trabalho), rotativo a zero no fim, aba Fresa antiga removida (H:58-206, H:331, H:436-489). Merges de 26/10 a 02/11 (#119, #121-#126, #128).
- **CAM 3D desktop:** STL, varredura e desbaste por níveis com Z digitado (H:261-269, H:342-345). Merges de 28/10 a 30/10 (#81, #82, #94).
- **STEP no desktop (CAM-08, #143, 05/11):** leitura de STEP pelo OpenCASCADE 7.9.3 (projeto `CncStudio.Step`), orientação da peça (X/Y ±90°, Virar, Zerar), só os sólidos quando há sólido (superfícies auxiliares ignoradas, com aviso; só malha facetada, com aviso), 33 DLLs nativas fora (.exe ~61,7 MB), avisos do OCCT em “Licenças de terceiros”. Lote NIST: 32 de 33 fechadas.
- **CAM web:** CAM rápido; trava de licença “soft”, livre enquanto o produto `cam` estiver inativo no servidor (H:139, H:238-241). Merges de 27/10 a 28/10 (#73).
- **Assist (só desktop):** bibliotecas, orçamento, proposta HTML, plano 2D com travas (plano obsoleto, ferramenta nova, profundidade, peça solta, alturas Z, rpm do fuso, material obrigatório), etapas e marca de origem por operação (H:208-236, H:286-329, H:451, H:484-489). Merges de 28/10 a 02/11 (#120, #127, #129, #131).
- **Web, Setup:** “minhas ferramentas” guardadas no navegador; desktop ainda sem isso (H:491-493). Merge em 02/11 (#132).
- **Documentação (03/11):** `ESTADO.md` enxuto com histórico em `docs/historico/` (#133); `CLAUDE.md` com `git fetch` no início e roteamento por tarefa (#134).

## Em ramo, sem merge na main (git, 05/11)
- **CAM-09 seleção 3D:** `fase-cam3d-selecao` (parte da malha do STEP antigo) ainda precisa se juntar à main, agora com o STEP nela; esperar conflito em `ModelView.cs` e apagar o `ModelZ` duplicado em `ModelReview.cs` (H:346). Há também `fase-cam-step-estudo` (só estudo).
- Ramos do STEP já mesclados (`fase-cam-step-malha`, `-correcao`, `-orientacao`, `ajuste-step-enxugar-dlls`, `ajuste-step-solidos`, `fase-cam-step-integracao`): podem ser apagados com cópia de segurança.
- `ajuste-web-simulacao`, `ajuste-cam3d-protecao`, `ajuste-docs-missao-gpt-acad02`, além de ramos antigos de 22/10.
- PRs abertos: nenhum, fora o desta atualização (conferido em 05/11).

## Fluxo de ramos (05/11)
`main` protegida (regra `proteger-main`: build + testes, sem force push/exclusão). Ramo `dev` = integração e teste do Igor; tarefas entram no `dev` por PR e o `dev` vai à `main` por PR do Igor.

## Aguarda o Igor
- Conferir na prévia/celular o que ficou “não conferido”: telas web do Code, Macro, Otimizador, Loja e Academy dentro do app (H:395-409, H:419); vista de frente para o eixo e peça girando na web (H:146, H:171).
- Conferir no .exe da prévia: Trabalho 3D, DXF em polegadas e clique, STL real (H:256, H:264, H:334); telas do Assist e do Trabalho marcadas “tela não conferida” (H:453, H:468, H:486, H:489).
- Decidir/ativar: produto `cam` no Supabase (H:240); publicação do texto de termos (H:273); origem/direitos da foto da Loja (H:399); uma peça real sua em STEP, quando puder (o teste só com o NIST foi aceito em 05/11; H:283).
- Revisar o módulo 2 da Academy (já na main, #109) e marcar `revisado`.
- Autorizar: CODE-14 (avisos do simulador); Handoff 21 (módulo 1 em v2); PLAT-07 (conta e licença no desktop).
- Guardar por escrito a resposta do advogado; pedir que ele confira o texto do aviso do OpenCASCADE e os itens “a confirmar” (ver Licenças). Ver a janela ⓘ e o aviso de superfícies do STEP no .exe da prévia a 100%, 125% e 150%.

## Limites que valem hoje
- Nada do CAM rodou em máquina real: rosca, 4º eixo, 3+2, rotativo a zero, F com rotativo no Fanuc 21i, CYCLE95/FALX no Siemens (H:95, H:101, H:151, H:177, H:471, H:474).
- Fixação e colisão nunca são verificadas; simular e rodar a primeira vez no ar (H:311, H:370).
- Simulação web em 3 eixos com voxel aproximado; tempo no celular não medido (H:163, H:166).
- Termos de uso não revisados por advogado (H:273). Licença OCCT do STEP: liberada pelo advogado em 05/11, segundo o Igor; texto do aviso e itens “a confirmar” em Licenças (H:282).
- Conteúdo técnico da Academy e itens iniciais do Assist são rascunho (H:210, H:435).
- O .exe e o instalador oficiais **não** estão publicados.
- O merge na main publica na hora a web (casillas.com.br) e o painel.
- Mínimo de tela do desktop: 1280×720 lógicos.
- STEP: o lote NIST (33 arquivos) abre sem erro e 3 referências batem até 0,01 mm. Não testados: STEP muito grande, IGES, STL pelo OCCT, cores e PMI, peça real do Igor. `CncStudio.Step.Tests` (8 testes) está fora da solução e do CI.

## Licenças (LIC-01)
- Inventário feito em `C:\Projetos\inventario\` (fora do repositório).
- 33 das 69 DLLs nativas do OpenCASCADE não são usadas (FFmpeg, FreeImage, OpenVR, TBB, jemalloc e 23 TK*); nenhum arquivo do repositório cita ffmpeg/FreeImage.
- 05/11: o advogado liberou o uso das licenças (repassado pelo Igor). Condições: o aviso na tela basta; as DLLs podem ficar no `.exe` único; .NET sob MIT.
- Feitos: Handoff 18 (33 DLLs fora, #140) e tela/arquivo de avisos de terceiros (LIC-02, #139); seções do OCCT 7.9.3 e das DLLs do OcctNet na janela de licenças (#143, na main). A confirmar antes de vender o `.exe`: texto do aviso do OCCT (advogado); DLLs do OcctNet compiladas do 7.9.3 sem modificação?; OcctNet.Wrapper (copyright, repositório, MIT); textos de zlib, libpng, bzip2, brotli (versão e licença), FreeType (ano do crédito) e Visual C++; textos do MicroCom e Tmds.DBus; versão do patch do .NET. Nada é publicado.

## Próximos passos já registrados
- CAM desktop, na ordem de 28/10: desbaste (feito), DXF e seleção de contornos, leitura do STEP (feita, #143), seleção de faces e arestas no 3D; depois o Assist (H:281-283).
- Pendências registradas: menu lateral pouco legível no tema claro; botão 3D do torno sobre fundo escuro (H:427).
- Não feito e citado: aviso ao Code quando o Trabalho muda (H:468); pontes no contorno (H:314); bedame interno e barras MVQNR (H:13); perfil real da rosca no editor (H:12); minhas ferramentas no desktop (H:493).

## Testes
- Contagem: ver o resultado do CI na main.

## Ponteiros
- Fichas: `docs/tarefas/` (modelo em `docs/tarefas/MODELO.md`); ex.: `docs/tarefas/CAM-01-plano.md`, `docs/tarefas/PLAT-06-desktop-plataforma.md`, `docs/tarefas/ACAD-02-modulos-2-a-4.md`.
- Histórico: `docs/historico/ESTADO-ate-2026-11-02.md`.

## Decisões do Igor (04/11)
- CODE-08 DNC: mais adiante, fora do “Próximo”.
- Loja: a nativa foi removida do desktop; a Loja abre a web.
- Tela inicial: 9 cartões (a web confirma no código; o desktop não foi verificado).
- Fixação (#119): testada no .exe pelo Igor.
- Supabase da conta mestre: o Igor acredita já estar resolvido; o nome do projeto (“Casillas Code” ou “Casillas”) não foi conferido. Não mexer em Supabase sem ordem.

## Divergências do histórico, fechadas em 04/11
- Contagem de testes: vale o resultado do CI; os números do histórico não são fonte.
- PR #85 está mesclado. Teste instável corrigido (#111, H:423). `ajuste-cam3d-desbaste-protecao` entrou pelo #94; `ajuste-cam3d-protecao` segue fora da main.
- H:427 e H:440: já feitos o campo Bruto (H:437), o Assist relendo o desenho (H:443), a rotação acima do fuso (H:441) e Origem/Máquina no torno (H:477). Seguem pendentes: menu lateral no tema claro e botão 3D do torno.
- “Na main (21/10)” e “Próximo: CODE-09, depois CODE-08” (H:5, H:9, H:14): desatualizados; ignorar.
- Parágrafo duplicado do PLAT-03 (H:22-23): só histórico.
- Planos “no Projeto” (H:118, H:182): ficam no Projeto “Casillas CNC — Plataforma” do claude.ai, fora do repositório.
- ffmpeg/FreeImage: nenhum arquivo do repositório os cita (grep em 04/11); a menção vem do relatório do inventário (LIC-01), fora do repositório.
- Ramos do STEP: estado registrado em “Em ramo”.
