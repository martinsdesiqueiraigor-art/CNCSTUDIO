## UI-C: relatório (Code desktop na proposta v2 — sprint C1 a C4)

Ramo `ajuste-UI-C`, saído do `ajuste-UI-B` (PR #198, que contém #195, #196 e #197): **depende de #195 a #198** (mesclar nessa ordem antes). Executor: Claude (chat), pela ficha UI-C (proposta v2 aprovada pelo Igor em 19/11, 06:09; execução autorizada às 06:16).

**Testes:** `dotnet test tests/CncStudio.Core.Tests` em Release, uma vez por fatia: 2015 aprovados nas quatro; teste mestre (`CamJobGoldenTests`, 8 G-codes) aprovado. Solução inteira em Release sem aviso nem erro. Fotos sem tela em 1280×720 conferidas em cada fatia. Ferramenta de fotos do CI (`tools/CncStudio.Screenshots`) rodada: as 7 fotos saem e a conferência do mouse no 3D passa (gira, move e aproxima). **Nenhum teste alterado. Core intocado.**

### O que mudou, por fatia
1. **C1 faixa com abas.** Arquivo (Novo, Abrir, Recentes, Salvar, Salvar como; Exemplos), Editar (Desfazer, Refazer; Procurar, Substituir, Ir para linha; Funções NC; fonte menor/maior e Explicar), Simular (Ferramentas, Bruto; Estatísticas, Só o gráfico), Saída (Folha de setup, Comparar). Botões com ícone e nome em grupos. São os mesmos botões de antes (mesmos nomes no código, mesmas ações e atalhos). Máquina, Controle, tema e licenças à direita, sempre à vista.
2. **C2 painel do comando.** Sobre o gráfico, como na web: linha, posição absoluta e falta por eixo (Ø no torno; A/B/C quando há eixo girado), ferramenta, rápido ou corte, rotação (rpm ou m/min), avanço, G ativos e zero de peça. Os dados vêm do `ControlPanel` do Core, o mesmo da web. A posição é a mesma do X/Z da linha de baixo; a falta é o movimento atual. Recolhe e abre com a seta; a escolha fica nas preferências (`CodePanelOpen` no config.json).
3. **C3 barra de simulação.** Botões maiores e arredondados, Executar em laranja, velocidade com − e + dos lados do seletor de modo e velocidade (que continua igual).
4. **C4 barra de módulos, erros e vistas.** Barra de módulos com 64 px e ícones de linha de uma cor (laranja no módulo aberto) no lugar das ilustrações; erros e avisos numa linha com as contagens e "▸ abrir" (a lista de antes, 140 px, com a divisória); Iso, Topo, Frente, Lado e Ajustar com nome; Bruto e Ferramentas saem da barra do gráfico e ficam na aba Simular da faixa.

### Decisões do Claude
- **Barra de módulos com 64 px**, não 52: com 52 o nome "Otimizador" quebrava no meio.
- **Ícones da faixa do Code:** os do app (uma cor), não os de duas cores do CAM; trocar o desenho de cada ícone fica para depois, se o Igor quiser.
- **Estatísticas:** continua a aba ao lado de Gráfico e ganhou o botão na faixa (Simular); "Ampliar" é o "Só o gráfico" (o mesmo de recolher o editor, F4).
- **Linha do painel:** mostra a linha do último movimento executado (regra do `ControlPanel`, igual à web); a linha de baixo mostra a do movimento atual.
- Fora da lista da ficha: `Settings/AppSettings.cs` ganhou só `CodePanelOpen` (preferência do app, nunca do programa).

### Teste do Igor (teste automático do PR)
1. Code: passar pelas abas Arquivo, Editar, Simular e Saída; cada botão faz o que fazia antes (Novo, Abrir, Recentes, Salvar, Exemplos, Procurar, Funções NC, fonte, Explicar, Ferramentas, Bruto, Folha de setup, Comparar). Ctrl+O, Ctrl+S, Ctrl+F continuam.
2. Simular um programa de fresa e um de torno: o painel acompanha (posição igual à da linha de baixo, falta, ferramenta, G ativos, zero). Recolher o painel, fechar e abrir o app: continua recolhido.
3. Barra de simulação: Executar (laranja), − e + mudam a velocidade, a barra de progresso e os passos continuam.
4. Barra de módulos: trocar de módulo (Code, CAM, Assist…): o aberto fica em laranja. Erros e avisos: "▸ abrir" mostra a lista; clicar num aviso leva à linha.
5. Tema claro e CMYK: nada ilegível. Receber um programa do CAM: o aviso do pacote continua aparecendo em cima do gráfico.
