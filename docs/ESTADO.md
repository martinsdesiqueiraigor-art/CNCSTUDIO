# Estado atual

Atualizado no fim de cada tarefa (3–6 linhas). A conversa nova começa por aqui.

- **Na `main` (21/10):** Fase 1 (simulação 3D), CODE-06 interface enxuta (PR #5) e CODE-07 comparação na web/celular, animação do percurso e desktop em paridade (PRs #6 e #7). Web oficial publicada direto da `main`.
- **Interface:** barra fina de ícones, seletor 2D | 2D + 3D | 3D, menu **Ver**, faixa de baixo numa linha com marcas de troca de ferramenta, editor recolhível. Web: `ComparePanel` (lado a lado, destaque das palavras); simulação começa no início; no play a ferramenta percorre o passo e os retornos dos ciclos (Core: `StepAnimation` na web, `LinePacing` no desktop).
- **Mouse do 3D no desktop:** só chega ao `GlSceneView` por `ICustomHitTest`. Testado numa placa real (CODE-05) e por teste sem tela.
- **Conta mestre:** licença vitalícia (20 aparelhos, o máximo do banco) criada no Supabase "Casillas Code" para o e-mail do dono. Mexer no Supabase continua só com autorização dele.
- **Próximo:** CODE-09 ferramentas de torno (ficha `docs/tarefas/CODE-09-ferramentas-torno.md`), depois CODE-08 DNC. Pendências: corte gradual dentro de movimento longo; apagar ramos antigos (CODE-04, do Igor).
- **Prévia:** push em ramo `fase-*`/`ajuste-*` publica `https://<ramo>.casillas-code.pages.dev` e o .exe de teste em Actions → Artifacts. **Testes:** 473 passando.

- CODE-09 na `main` (21/10, PR #8): tipos de ferramenta de torno (desbaste, barra interna, bedame, rosca) com desenho e corte próprios; editor só na web. Entrega 2 (PR #15, 22/10): tipo e largura do inserto na tabela de ferramentas do desktop. Falta: perfil real da rosca.
- CODE-13 na `main` (22/10, PR #14): bedame externo (cabeça fina, alargamento a 45°, cabo 25×25, garra e parafuso), PCLNR com garra em cunha e fresa de topo com 4 canais helicoidais (só visuais). Falta: bedame interno e barras (referências MVQNR), inclinação de 10° da cabeça, ver no app com GPU. **Testes:** 509.
- Também na `main` (22/10): CODE-10 (6 programas de torno), MACRO-02 (macros O9010 furação em círculo e O9011 bolsão, rascunho, validadas no interpretador; PR #13) e CODE-11 (referência visual de ferramentas). Próximo: bedame interno e barras (MVQNR), Macro 01 (geradores), Academy piloto.
- MACRO-01 entregas 1 e 2 (ramo `fase-2-macro-geradores`, 22/10): 8 geradores no Core (furação em círculo, grade, bolsões retangular e circular, rasgo, faceamento, furo e rosca helicoidais; macros O9010–O9017), saída programa pronto ou G65 + macro, tela na web (menu Arquivos → Geradores). Todos têm **Z inicial** (topo do bruto, campo W; camadas e alturas seguras partem dele; furação usa W + folga). Falta: teste do Igor no celular, tela no desktop, mais geradores. Ficha `docs/tarefas/MACRO-01-geradores.md`.
- ACAD-01 (ramo `fase-3-academy`, 24/10): estrutura técnica da Academy na web, menu Arquivos → Academy (cursos). Core: `Academy/` (leitor de aulas JSON, validador da ficha, correção de quiz, progresso no aparelho, ponte `AcademyJson`); tela `wwwroot/academy-ui.js` (trilhas Comum/Fresa/Torno × 6 níveis, aula, quiz, teste final, abrir programa no editor/3D). Conteúdo vem de `content/academy/` e `samples/academy/` (embutido); o teste `EmbeddedContentInTheRepositoryIsValid` barra conteúdo fora da ficha. Módulo 1 (rascunho do GPT, 2ª versão aprovada pelo validador) já incluído. Falta: módulos 2–7, tela no desktop. **Testes:** 583.
- ACAD-01 teste visual (ramo `fase-3-academy-visual`, sobre o PR #19, 24/10): a aula mostra o programa linha a linha com explicação de cada comando (Core: `ProgramWalkthrough`/`LineExplainer`, texto gerado, sem custo de conteúdo) e um mini 3D em aramado (bruto, eixos, percurso, ferramenta, girar com o dedo, reproduzir). A 1ª versão (PR #19) fica como volta se não der certo. **Testes:** 587.
- ACAD-01 visual v2 (24/10, PR #20): aula em cartões curtos com pausa rápida; diagramas interativos (eixos X/Y/Z, absoluto × incremental, zero máquina × zero peça/G54); exercício digitado no editor e conferido pelo Core (`ExerciseChecker`: posição final, texto presente, sem erro; a solução do autor tem de passar); espaço para vídeo do YouTube (campo `video`, sem vídeo ainda); mini 3D e leitura linha a linha. Campos novos no JSON da aula: `diagramas`, `video`, `exercicio.editor`. **Testes:** 593.
- ACAD-01 painel (24/10, PR #20): abas inferiores Início (continuar, anel de progresso, dias seguidos), Trilhas (linha do tempo dos níveis, com cor e barra) e Conquistas (selos de módulo, estatísticas, notas dos testes finais); quiz recolhe as outras alternativas depois de responder (Trocar resposta; Corrigir mostra todas com cor); gráfico dos eixos fixo; mini 3D da aula só gira com o botão Girar; botão do canal do YouTube enquanto a aula não tem vídeo. Fora do escopo por decisão do Igor: mapas visuais.
- Plataforma (fase-4-plataforma-inicio): tela inicial "Plataforma Casillas" (cartões Code, Academy, Macro ativos; CAM, Biblioteca CNC, Ferramentas "em breve") e barra inferior fixa (Início, Code, Academy, Macro) em `platform-ui.js`; links `#academy`/`#macro`/`#code`; só na web por enquanto (desktop depois). Academy: botões Voltar ao módulo/Próxima aula no fim da aula e do teste final. Atualização 24/10 (fase-5-otimizador, PR #22): tela inicial com 8 cartões em ícones estilo app (Academy, CAM, Code, Biblioteca, Macro, Otimizador, Calculadoras, Loja), sem rolagem; Calculadoras abre o app Casillas em outra aba; Loja e CAM/Biblioteca "em breve"; barra inferior Início/Code/Academy/Macro/Otimizador; aviso de teste; menu do Code enxuto.
- OTIM-01 (fase-5-otimizador): núcleo do GCodeOptimizer portado para `Core/Optimizer` (130 testes, programas sintéticos), tela web (analisar, propostas, abrir no editor/3D/comparar, revisão e cópia anonimizada), cartão "Casillas Otimizador" e barra inferior. Só simulador. Ver `docs/tarefas/OTIM-01-otimizador.md`.
- PLAT-03 (fase-6-conta-unica): web usa a conta única (Supabase "Casillas", funções `platform_*_v1`): teste de 30 dias para todos os módulos, 2 aparelhos, ativar código na tela Conta; Macro e Otimizador travam com "Comprar licença" no fim do teste; Code trava a simulação (reproduzir/passo a passo), salvar e exportar. Falta: Academy (módulo 1 aberto, demais travados), painel de ativação novo, desktop. Auth do projeto novo: conferir Site URL e URLs de redirecionamento (casillas-code.pages.dev e *.casillas-code.pages.dev). Atualização (fase-7-academy-painel): Academy trava módulos ≠ 1 sem teste/licença (módulo 1 sempre aberto); painel de ativação (`admin/index.html`) reescrito para o projeto "Casillas": gerar códigos (módulo/pacote, assinatura ou vitalícia), clientes, licenças, revogar, liberar aparelho; funções `platform_admin_*_v1`.
- PLAT-03 (fase-6-conta-unica): web usa a conta única (Supabase "Casillas", funções `platform_*_v1`): teste de 30 dias para todos os módulos, 2 aparelhos, ativar código na tela Conta; Macro e Otimizador travam com "Comprar licença" no fim do teste; Code trava a simulação (reproduzir/passo a passo), salvar e exportar. Falta: Academy (módulo 1 aberto, demais travados), painel de ativação novo, desktop. Auth do projeto novo: conferir Site URL e URLs de redirecionamento (casillas-code.pages.dev e *.casillas-code.pages.dev).
- MACRO-03 (fase-8-macro-texto, 24/10): gerador **Texto (gravação)**:  [arco: raio K e lado M, texto segue um círculo] fonte de traço único própria (A–Z, 0–9, `- _ = + / . , : ! ( )`; acentos viram a letra base), altura, largura %, espaço entre letras, ângulo, alinhamento (esq./centro/dir.), Z inicial, passes em Z, avanço de descida e de corte. Só programa pronto (sem G65). Core: `StrokeFont`, `TextEngravingGenerator`; geradores ganharam campos de texto (`TextParameters`, `SupportsMacro`) e a tela web mostra o campo de texto. **Testes:** 740.
- UI do Code (ramo `ajuste-code-ui`, 25/10): (1) menus Arquivos, Funções e movimentos rápidos escondem o 3D enquanto abertos (o 3D é elemento do navegador e cortava o menu); (2) botão "N avisos/erros" abre cartão com tipo, linha, mensagem, linha do programa e "como corrigir" (Core: `DiagnosticAdvice`), com Anterior/Próximo; (3) cabeçalho em uma linha, versão e licença sob o nome, botões mais justos no celular em pé. **Testes:** 750. Falta ver na prévia (celular em pé e deitado).
- UI do Code, etapa 2 (ramo `ajuste-tela-inicial`, 25/10): tela inicial com logo maior e cartões menores. Em pé: 3 colunas, última linha centralizada, sem descrição. Deitado: logo e título à esquerda, 4×2 cartões (Loja não fica sozinha). Só `platform-ui.js`. **Testes:** 750. Falta ver na prévia.
- UI do Code, etapa 1 (ramo `ajuste-code-formatar`, 25/10): menu Funções da web ganha "Formatar o programa": Espaçar código, Juntar código e Letras maiúsculas (Core: `NcTextTools`, já testado; comentários e textos não mudam; uma ação no Desfazer). **Testes:** 750. Falta ver na prévia (celular). Próximas: 2 tela inicial, 3 barra de simulação flutuante, 4 teclado do editor, 5 cores, 6 tema ciano.

- **Painel de controle (ramo `ajuste-painel-controle`)**: cartão estilo máquina na simulação (2D e 3D): posição absoluta, falta percorrer por eixo, ferramenta, avanço/rotação, G ativos e zero de peça (G54–G59). Sem coordenada máquina. Arrasta, recolhe/expande; liga/desliga em Ver. Core: `Panel/ControlPanel`, `MachineModal` por movimento.
- UI do Code, etapa 4 (ramo `ajuste-teclado-editor`, 25/10): (1) teclado próprio do editor no celular (`EditorKeyboard`, `MainView.Keyboard.cs`): botão "Teclado" no editor; letras do G-code (G M N O X Y Z...), página 123 com números e símbolos, setas, espaço, Apagar (repete), Enter, Desfazer e esconder; não abre o teclado do Android e o "Editar" nativo continua. (2) balão do código: tocar num G/M/endereço (ou digitar) mostra a explicação do Core (`GCodeHelp`); fecha no X ou em 9 s. (3) correção: o canvas do Avalonia ocupava a tela toda e a barra inferior cobria o rodapé do app (status e informações do ponto); `#out` agora é relativo e cortado (`platform-ui.js`). Conferido no app publicado em navegador de teste: abrir, digitar G83 e ver o balão. **Testes:** 750. Falta ver na prévia, no celular.
- Etapa 4, ajuste (25/10, a pedido do Igor): botão "Balão: sim/não" liga e desliga o balão do código (fica guardado em `cc.balloon`); letras mais usadas do teclado com cor (G azul, M laranja, X Y Z verde, F S T amarelo, I J K R roxo).
- UI do Code, etapa 3 (ramo `ajuste-barra-simulacao`, 25/10): no 3D sozinho, os controles da simulação (início, voltar, play, passo, fim, progresso, velocidade) saem do rodapé e flutuam dentro do 3D, numa barra HTML (`scene3d.js`: `setSimBar`/`onSimBar`/`setSimBarVisible`; C#: `Scene3DView.SimBarCommand`, `MainView.SimCommandAsync`, mesmos comandos e trava de licença do rodapé). Fica em baixo, à direita, em cima ou à esquerda: arrastar a alça ou tocar nela troca (guarda em `cc.simbar.edge`); vertical compacta no celular deitado. Sem as marcas de troca de ferramenta no 3D (continuam no 2D+3D e no 2D). Com 2D ao lado vale o rodapé. **Testes:** 750. Falta ver na prévia.

- **Tema CMYK (ramo `ajuste-tema-cmyk`)**: opção em Ver → "Tema CMYK" (ciano de destaque, magenta na marca, fundo azul-petróleo). Padrão continua o visual laranja. Cor dos elementos HTML via `--cc-accent`.

- **CMYK em todos os módulos HTML** (ramo `ajuste-tema-cmyk-modulos`): o tema agora também recolore Início, Academy, Macro, Otimizador, Ferramentas e rodapé (sobreposição de cores em `web.js`). Cada cartão da Início mantém a cor própria do produto.

- **Atalho de tema no rodapé** (ramo `ajuste-tema-atalho`): botão "Tema" no rodapé alterna padrão ↔ CMYK em qualquer módulo; sincroniza com a opção do menu Ver.

- **ACAD-02** (`docs/tarefas/ACAD-02-modulos-2-a-4.md`): ficha dos módulos 2 a 4 da Academy pronta para o GPT (um PR por módulo). Aguarda o Igor entregar ao GPT.

- **PLAT-06** (`docs/tarefas/PLAT-06-desktop-plataforma.md`): plano da plataforma no desktop (barra de módulos, Início, Tema, Otimizador, Macro, conta, Academy). CAM-01 atualizado: desktop primeiro. Nada codado.
- **CAM-01** (`docs/tarefas/CAM-01-plano.md`): plano do Casillas CAM 2,5D escrito (6 entregas, 5 decisões do Igor). Nada codado.
- **Casillas Loja (teste)** (ramo `ajuste-loja-teste`): módulo abre pelo cartão/#loja; catálogo em `wwwroot/loja/catalogo.json` (1 item: apalpador 3D, specs A definir, preço sob consulta); pedido pelo WhatsApp. Sem pagamento, sem preço no repositório.

- **Desktop, etapa 1 do PLAT-06** (ramo `ajuste-desktop-barra-modulos`): barra lateral de módulos (Início, Code, Otimizador, Macro, CAM, Academy; os 4 últimos "em breve"), tela Início e botão Tema CMYK (salvo nas configurações). O Code não mudou: o Início só o cobre.

- **Desktop, etapa 2** (`ajuste-desktop-otimizador`): módulo Otimizador nativo (analisar, propostas, abrir proposta/revisão no editor, copiar anonimizada), usando `Core/Optimizer`.

- **Desktop, etapa 3** (`ajuste-desktop-macro`): módulo Macro nativo (9 geradores, programa pronto ou G65 + macro, abrir no editor para simular), usando `Core/Generators`.

- **Desktop, etapas 5 e 6**: Academy fica só na web (decisão do Igor, 26/10). Loja nativa no desktop (catálogo `loja/catalogo.json` compartilhado com a web, pedido pelo WhatsApp). Falta a etapa 4 (conta e licença).

- **Desktop voltado ao operador** (`ajuste-desktop-links-web`): Academy, Loja e Calculadoras viram botões que abrem a web (endereços em `Modules/PlatformModules.cs`); Loja nativa removida. Desktop fica com Code, Otimizador, Macro e CAM.

- **Desktop visual** (`ajuste-desktop-visual`): Início no estilo da web (logo com máscara, cartões com ilustração e cor por produto; as imagens são as mesmas da web, ligadas por `AvaloniaResource Link`), barra lateral com ícones coloridos e destaque na cor do produto, marca "Casillas Code" no cabeçalho, CMYK completo (editor, código, abas e painéis).

## CAM-01 entrega 1 — geometria 2D e offset (ajuste-cam-geometria)
- `Core/Cam`: `Point2`, `Segment2` (reta/arco), `Contour` (área com sinal, sentido, perímetro, retângulo/círculo/polígono, `Reversed`).
- `Contour.OffsetOutside(d)` / `OffsetLeft(d)`: desloca por fora/dentro, com arco nos cantos externos e encontro nos internos; devolve erro se não cabe ou se cruza.
- 13 testes novos (`ContourOffsetTests`); total 769 passando. Próximo: entrega 2 (importar DXF).

## CAM-01 entrega 2 — importar DXF (ajuste-cam-dxf)
- `Core/Cam/DxfImporter.Parse(texto)`: LINE, ARC, CIRCLE, LWPOLYLINE e POLYLINE (com bulge); junta retas/arcos soltos em contornos fechados (pontas a 0,001 mm, inverte o que precisar).
- Devolve contornos fechados, cadeias abertas e avisos (SPLINE/ELLIPSE/texto ignorados). Só DXF em texto.
- 11 testes novos; total 780 passando. Próximo: entrega 3 (operações: faceamento, contorno, bolsão, furação).

## CAM-01 entrega 3 — operações (ajuste-cam-operacoes)
- `Core/Cam`: `CamOperations.Facing`, `.Contour` (fora/dentro/sobre a linha, concordante ou convencional, sobremetal, entrada/saída em arco, compensação G41/G42 ou percurso já deslocado), `.Pocket` (anéis concêntricos, rampa helicoidal, acabamento), `.Drilling` (G81/G82/G83/G73, ordem do furo mais próximo, furos a partir de círculos do DXF).
- Saída neutra (`ToolMove`, `DrillCycle`); o G-code sai na entrega 4 (pós Fanuc/Siemens), onde cada programa passa também pelo `MachineExecutor`.
- Limites: bolsão sem ilhas; contorno por dentro desce na vertical (avisa). 22 testes novos; total 801 passando.

## CAM-01 entrega 4 — pós-processador Fanuc e Siemens (ajuste-cam-pos)
- `Core/Cam/CamPost.Generate(operações, PostOptions)`: cabeçalho, troca de ferramenta (T/M6, G43 H no Fanuc, D no Siemens), S/M3, refrigerante, G54, movimentos com avanço modal, arcos e hélices (I/J), G41/G42 (Fanuc `D`, Siemens sem D), furação G81/G82/G83/G73 (Fanuc) e CYCLE81/82/83 com MCALL (Siemens), fim com M30.
- Cada programa passa pelo `MachineExecutor`: o percurso simulado é igual ao gerado (movimento a movimento) e a compensação G41 desloca para o lado certo.
- 17 testes novos; total 818 passando. `DrillCycle` ganhou `SafeZ`. Próximo: entrega 5 (tela do CAM no desktop).

## CAM-01 entrega 5 — tela do CAM no desktop (ajuste-cam-tela)
- Módulo **CAM** ativo na barra lateral: abrir DXF ou desenhar retângulo/círculo, lista de contornos, ferramenta e alturas, operação (contorno fora/dentro/sobre a linha, bolsão, faceamento, furação pelos círculos), lista de operações, comando Fanuc/Siemens, prévia 2D com o percurso, "Gerar programa" e "Abrir no editor (simular no Code)".
- Testado em Xvfb: retângulo + círculo → contorno por fora → programa abre no Code com 0 erros (2 avisos de G28, que o simulador não desenha).
- Core: `Contour.BoundsXY`, `ToolpathView.CuttingPolylines` (+3 testes; total 821). Só desktop; na web o CAM continua "em breve".
- Falta: testar com um DXF real de máquina, ilhas no bolsão, banco de ferramentas, torno e 3D (entrega 6).

## CAM-01 entrega 6a — banco de ferramentas (ajuste-cam-ferramentas)
- Banco = a **mesma tabela de ferramentas do Code** (`AppSettings.Tools`, em config.json): `ToolEntry` ganhou rotação e avanços. Uma fonte só: o simulador do Code usa o diâmetro cadastrado.
- Tela do CAM: escolher ferramenta do banco (preenche T, Ø, rpm, avanços e nome), salvar no banco, excluir. "Abrir no editor" cadastra no banco as ferramentas que faltam.
- Pós: nome da ferramenta no comentário da troca; mesma ferramenta com rotação diferente emite novo S; `CamPost.Check` avisa T repetido com diâmetros diferentes (e a tela avisa se o diâmetro difere do banco).
- 4 testes novos; total 825. Próximo: 6b torno.

## CAM-01 entrega 6b — torno (ajuste-cam-torno)
- `Core/Cam/Lathe.cs`: `LatheProfile` (trechos em Z × raio, orientado para a placa), `LatheOperations.Turning` (externo/interno, desbaste e/ou acabamento, perfil tipo I; recusa rebaixo, bruto menor que o perfil), `.Facing`, `.Drilling` (bicadas em movimentos simples).
- `Core/Cam/LathePost.cs`: Fanuc (G71/G70, G96+G50, G99, T0101) e Siemens (CYCLE95 com contornos depois do M30, G96 LIMS, G95, DIAMON). Só acabamento sai como movimentos simples.
- Conferido pelo simulador: o percurso nunca entra no perfil (externo) nem sai do furo (interno), o acabamento passa pelo perfil (inclusive arco) e chega ao fundo/centro. 17 testes novos; total 842.
- Tela: no módulo CAM, aba **Torno** (perfil por DXF ou pontos "Z Ø", ferramenta, operações, prévia do programa simulado, gerar, abrir no editor).
- Limites: sem rosca, canal, G72/G73 nem perfil com rebaixo (tipo II); sem integração com o banco de ferramentas do torno; sobremetal radial do Siemens (FALX) precisa ser conferido em controle real.

## CAM-01 · entrega 6c — rosca no torno
- `LatheOperations.Threading` (externa/interna, passo ISO, altura 0,6134·P / 0,5413·P, passes √ + acabamento) e `LatheThread` no Core.
- Pós: Fanuc `G76` (dois blocos, P/Q em mícrons, `G97` giro fixo) e Siemens `CYCLE97`; volta ao `G96` na operação seguinte.
- Tela do torno: "Rosca externa" e "Rosca interna". Testes simulam os dois dialetos (raiz, crista, Z de entrada/saída).
- Limites: só rosca reta, 1 entrada, flanco 60°; não testado em máquina real.

## CAM-01 · entrega 6d — canal no torno
- `LatheOperations.Grooving` (canal radial externo/interno, retangular): mergulhos com quebra-cavaco, passo lateral de 80% da ferramenta, acabamento das duas paredes e do fundo com sobremetal.
- Movimentos simples (Fanuc e Siemens iguais; o simulador não executa CYCLE93, por isso não usamos). Z da ferramenta = aresta de maior Z.
- Tela: "Canal externo" e "Canal interno". Testes simulam e conferem fundo, paredes e cobertura.
- Limites: só canal reto (sem chanfro/raio, sem canal de face).

## CAM-01 · entrega 6e — perfil com rebaixo (mergulho)
- `Turning` aceita perfil cujo raio vai e volta (canal/rebaixo); Z segue obrigatoriamente rumo à placa. Antes recusava.
- Fanuc: G71 tipo II (1º bloco do perfil com X e Z); Siemens: CYCLE95 (o simulador mergulha no rebaixo nos dois).
- Com rebaixo o sobremetal em Z vai a zero (senão corta a parede do rebaixo a mais) e aparece aviso. Teste simula e confere que nada entra na peça.
- Limites: sem checar ângulo de folga da pastilha; para canal retangular simples use "Canal" (6d).

## CAM-02 · etapa 1 — perfil de máquina 4/5 eixos
- Core: `MachineProfile` (comando, até 2 eixos rotativos A/B/C na mesa ou no cabeçote, limites, pivô, sentido, modo de saída calculado ou função da máquina), `Validate`/`Warnings`, `Describe`, modelos prontos e JSON (`MachineProfileJson`). 10 testes.
- Desktop: aba "Perfil 4/5 eixos" no CAM (lista, novo a partir de modelo, duplicar, excluir, salvar, "usar este perfil"); perfis em `config.json` (`MachineProfiles`, `ActiveMachineProfile`).
- Ainda **não gera programa**: só guarda a máquina. Próximo: 4º eixo no modo calculado (Fanuc 21i, sem G68.2/TCP, 5 eixos simultâneos). Plano: `CAM-02-4-e-5-eixos.md` no Projeto.

## CAM-02 etapa 2 — 4º eixo no modo calculado (Core)
- `ToolMove` ganhou palavra rotativa opcional (`RotaryLetter`/`RotaryAngle`); `CamPost` escreve A/B/C em Fanuc e Siemens (só quando muda).
- `RotaryIndexing.Place`: leva operação 2,5D (face para cima) para a face girada do eixo A ou B na mesa: translada pelo pivô, sobe à altura segura, gira, corta, recua. Respeita curso e sentido invertido; recusa cabeçote, modo nativo e furação em ciclo fixo.
- Sem tela ainda (próximo passo). Nada testado em máquina: rodar no ar e conferir o sentido de A/B. 874 testes.
