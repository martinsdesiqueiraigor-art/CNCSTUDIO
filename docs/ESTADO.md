# Estado atual

Atualizado em 20/11/2026 (FECHO-02, versão 1.2.0). Vale para o `dev` depois do fecho e para a `main` quando o `dev` entrar nela (só com ordem do Igor).
Histórico: `docs/historico/ESTADO-ate-2026-11-14.md` (o resumo anterior) e `docs/historico/ESTADO-ate-2026-11-02.md`. Ler só se a tarefa pedir.
Notas das versões: `docs/versoes/1.1.0.md` e `docs/versoes/1.2.0.md`.

## O que existe
- **Code (web e desktop):** editor, simulação 2D/3D, comparação, painel de controle, Macro e Otimizador (web e desktop), Academy só na web (conteúdo em rascunho). No desktop, faixa com abas, painel do comando sobre o gráfico e barra de simulação como na web.
- **Plataforma web:** tela inicial, conta única com teste de 30 dias e travas por módulo, domínio `casillas.com.br`. O merge na `main` publica a web na hora.
- **CAM desktop, interface:** faixa com abas (Preparação, Operações, Código NC) e seletor Início, Fresa, Torno, Ferramentas; menu "Operações"; árvore com estado e tempo, nó "Geometria" por operação da fresa; painel da operação em abas (fresa e torno); console de avisos (Erro, Aviso, Conferir, Info); cubo de vistas; biblioteca de ferramentas recolhível; linha do tempo; tema escuro.
- **CAM desktop, fresa:** Preparação (Máquina, Peça pronta em DXF ou STEP, Bruto por sobra, cilindro, tubo ou STEP, Programa nº, G5x, Zero de 9 pontos); banco de ferramentas ativo (sem armário); 4º eixo e 3+2 na mesa dupla; pacote `.casillas-cam` para o Code. Operações: Faceamento, Contorno, Bolsão (entradas e estratégias, ilhas), Furação, Texto, Chanfro (fresa em V).
- **STEP no CAM:** peça STEP no Trabalho; faces e arestas viram Faceamento, Contorno, Bolsão com ilhas e Furação; o Assist lê o STEP (contorno pela silhueta); bruto STEP do mesmo arquivo alinhado com a peça. Gerar com bruto STEP continua travado.
- **CAM desktop, torno:** Preparação (Máquina, Programa nº, G5x, refrigeração, bruto), operação nova vazia, faceamento, torneamento externo e interno, furação, rosca, canal radial, canal de face, sangramento, fixação (castanha dura e mole, contraponto, luneta), Z0 na face pronta ou oposta, pacote `.casillas-cam` do torno.
- **Assist (só desktop):** bibliotecas, orçamento, proposta, plano 2D.
- **STEP:** OpenCASCADE 7.9.3 (`CncStudio.Step`, só desktop).

## Arquivos
- `.casillas-job` da fresa: versões 1 a 7. Versão 4 com peça STEP ou lista de furos; 5 com campos vazios; 6 com geometria vinda do STEP; 7 só com Chanfro. Programa antigo recusa versão maior com aviso.
- `.casillas-job` do torno: versão 1; chaves novas só quando usadas.
- Teste mestre: `CamJobGoldenTests`, 8 programas de referência (ganharam só a linha `(BRUTO ...)` no DESK-01, autorizado).

## Limites que valem hoje
- Nada do CAM rodou em máquina real. G-code novo: Chanfro, canal de face, sangramento, Z da face oposta.
- O CAM não acompanha o material: cada operação é calculada sem saber o que as anteriores removeram (sem bruto atualizado, sem material restante, sem comparação peça × bruto). A simulação de remoção existe só no Code, sobre o G-code pronto.
- Fixação, queda da peça e colisão não são verificadas; simular e rodar a primeira vez no ar.
- Contorno lido da malha do 3D é poligonal (arcos viram trechos retos). Chanfro com ponta plana sai menor que o digitado.
- O nível das mensagens do console é reconhecido por palavra-chave; mensagem nova de tela entra como Erro.
- Interface vista em tela virtual (1280 e 150%, fresa e torno) e no .exe só em parte; 125% e a biblioteca com T e Ø preenchidos não foram conferidos.
- STEP muito grande, IGES e peça real do Igor: não testados. `CncStudio.Step.Tests` fora da solução e do CI.
- O `.exe` e o instalador oficiais não estão publicados. Mínimo de tela: 1280×720.
- Siemens: o simulador trata G58/G59 como zeros 5 e 6 (`MachineExecutor.cs:630`).

## Fora desta versão
2ª fixação do torno; armário de ferramentas, D2, D3 e importar da web; operações novas de fresa (ranhura, macho, mandrilhar, contorno aberto, gravação, rosca fresada); bruto atualizado a cada operação; Gerar com bruto STEP; FreeCAD (estudo parado).

## Para vender o `.exe` (pendente)
Itens "a confirmar" do inventário de licenças (texto do aviso do OCCT, DLLs do OcctNet, textos de zlib, libpng, bzip2, brotli, FreeType, Visual C++, MicroCom, Tmds.DBus) e uma peça real em STEP testada.

## Ponteiros
- Fichas e relatórios: `docs/tarefas/` (modelo `docs/tarefas/_MODELO.md`; relatório de cada missão em `docs/tarefas/<ID>-volta.md`).
- Planos do torno: `docs/tarefas/TORNO-01-plano.md`. Relatórios da interface: `UI-PRO-P1a-volta.md`, `UI-PRO-P1b-volta.md`, `UI-PRO-P2-volta.md`, `UI-PRO-sprint-volta.md`, `FECHO-02-volta.md`.
- Testes: vale o resultado do CI.
