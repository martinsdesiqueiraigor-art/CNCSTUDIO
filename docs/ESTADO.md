# Estado atual

Atualizado em 02/11/2026 · base: `origin/main` em `fb1c10c` (merge do PR #132).
Histórico completo em docs/historico/. Ler só se a tarefa pedir.
Fonte deste resumo: `docs/historico/ESTADO-ate-2026-11-02.md` (citado como H:linha) e `git log`.

## Na main
- **Code (web e desktop):** simulação 2D/3D, comparação, painel de controle, tema CMYK, teclado do editor e balão do código na web, faixa Renumerar/Formatar/Desfazer na web (H:5-38, H:406).
- **Plataforma web:** tela inicial com cartões, Configurações, Novidades (lista vazia), conta única com teste de 30 dias e travas por módulo (H:22, H:394, H:444).
- **Macro:** geradores no Core, tela web renovada e módulo nativo no desktop (H:15, H:24, H:50, H:402).
- **Otimizador:** Core, tela web com aviso de economia de linhas, módulo nativo no desktop (H:21, H:48, H:400).
- **Academy (só web):** leitor v1 e v2, tela nova; módulo 1 em v1, módulo 2 em v2, conteúdo em rascunho (H:52, H:415, H:428-435).
- **CAM desktop:** Trabalho fresa e torno (árvore, propriedades, barra de pendências, Bruto, Origem, Máquina, Fixação, desfazer, 3D), DXF com unidade, bolsão com ilhas, torno (rosca, canal, rebaixo), 4º eixo, 3+2 na mesa dupla (só Core e Trabalho), rotativo a zero no fim, aba Fresa antiga removida (H:58-206, H:331, H:436-489).
- **CAM 3D desktop:** STL, varredura e desbaste por níveis com Z digitado (H:261-269, H:342-345).
- **CAM web:** CAM rápido; trava de licença “soft”, livre enquanto o produto `cam` estiver inativo no servidor (H:139, H:238-241).
- **Assist (só desktop):** bibliotecas, orçamento, proposta HTML, plano 2D com travas (plano obsoleto, ferramenta nova, profundidade, peça solta, alturas Z, rpm do fuso, material obrigatório), etapas e marca de origem por operação (H:208-236, H:286-329, H:451, H:484-489).
- **Web, Setup:** “minhas ferramentas” guardadas no navegador; desktop ainda sem isso (H:491-493).

## Em ramo, sem merge na main (git, 02/11)
- `fase-cam-step-estudo`, `fase-cam-step-malha`, `fase-cam-step-correcao`, `fase-cam-step-orientacao` (STEP / Modelo 3D).
- `fase-cam3d-selecao`: juntar com a main exige resolver `ModelZ` e campos Z (H:346).
- `ajuste-web-simulacao`, `ajuste-cam3d-protecao`, `ajuste-docs-missao-gpt-acad02`, além de ramos antigos de 22/10.
- PRs abertos: nenhum listado por `gh pr list` em 02/11.

## Aguarda o Igor
- Conferir na prévia/celular o que ficou “não conferido”: telas web do Code, Macro, Otimizador, Loja e Academy dentro do app (H:395-409, H:419); vista de frente para o eixo e peça girando na web (H:146, H:171).
- Conferir no .exe da prévia: Trabalho 3D, DXF em polegadas e clique, STL real (H:256, H:264, H:334); telas do Assist e do Trabalho marcadas “tela não conferida” (H:453, H:468, H:486, H:489).
- Decidir/ativar: produto `cam` no Supabase (H:240); publicação do texto de termos (H:273); origem/direitos da foto da Loja (H:399); STEP real de peça para o teste de viabilidade (H:283).
- ACAD-02: entregar a ficha dos módulos 2 a 4 ao GPT (H:40).

## Limites que valem hoje
- Nada do CAM rodou em máquina real: rosca, 4º eixo, 3+2, rotativo a zero, F com rotativo no Fanuc 21i, CYCLE95/FALX no Siemens (H:95, H:101, H:151, H:177, H:471, H:474).
- Fixação e colisão nunca são verificadas; simular e rodar a primeira vez no ar (H:311, H:370).
- Simulação web em 3 eixos com voxel aproximado; tempo no celular não medido (H:163, H:166).
- Termos não revisados por advogado; licença OCCT do STEP a confirmar com profissional (H:273, H:282).
- Conteúdo técnico da Academy e itens iniciais do Assist são rascunho (H:210, H:435).

## Próximos passos já registrados
- CAM desktop, na ordem de 28/10: desbaste (feito), DXF e seleção de contornos, estudo do STEP, seleção de faces e arestas no 3D; depois o Assist (H:281-283).
- Pendências registradas: menu lateral pouco legível no tema claro; botão 3D do torno sobre fundo escuro (H:427).
- Não feito e citado: aviso ao Code quando o Trabalho muda (H:468); pontes no contorno (H:314); bedame interno e barras MVQNR (H:13); perfil real da rosca no editor (H:12); minhas ferramentas no desktop (H:493).

## Testes
- Contagem: ver o resultado do CI na main.

## Ponteiros
- Fichas: `docs/tarefas/` (modelo em `docs/tarefas/MODELO.md`); ex.: `docs/tarefas/CAM-01-plano.md`, `docs/tarefas/PLAT-06-desktop-plataforma.md`, `docs/tarefas/ACAD-02-modulos-2-a-4.md`.
- Histórico: `docs/historico/ESTADO-ate-2026-11-02.md`.

## Divergências a resolver (não decididas aqui)
- H:5, H:9, H:14: “Na main (21/10)” e “Próximo: CODE-09, depois CODE-08 DNC” desatualizados; CODE-08 DNC não aparece de novo.
- H:10 a H:493: contagens de testes diferentes ao longo do arquivo (473 a 1332), fora de ordem cronológica (H:480 1307, H:483 1308, H:474 1245).
- H:22 e H:23: parágrafo do PLAT-03 duplicado.
- H:8 cita o Supabase “Casillas Code”; H:22 cita o projeto “Casillas”. Qual vale para a conta mestre?
- H:52 “Loja nativa no desktop” × H:54 “Loja nativa removida”.
- H:20 (8 cartões, CAM “em breve”) × H:140 e H:394 (CAM ativo, 9 cartões).
- H:284: “PR #85 aguarda conferir e o OK do merge”, mas o git mostra o #85 mesclado.
- H:342: ramo citado `ajuste-cam3d-desbaste-protecao`; no git o remoto é `ajuste-cam3d-protecao`, não mesclado, embora `SurfaceRoughing`/`ModelZ` estejam na main.
- H:371 e H:413 (teste instável, correção não feita) × H:423 (corrigido).
- H:427 e H:440 listam como pendentes itens feitos depois (H:437, H:441, H:443, H:477).
- STEP: ramos `fase-cam-step-*` existem no git, mas o arquivo não registra o estado deles.
- H:118 e H:182 apontam planos “no Projeto” (fora deste repositório).
- ffmpeg/FreeImage: não aparecem no arquivo de histórico; se descritos como usados, é em outro documento.
