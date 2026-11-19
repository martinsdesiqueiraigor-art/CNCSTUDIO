## UI-A: relatório (interface nova, base do CAM — sprint UI-01, UI-02, UI-03, UI-06)

Ramo `ajuste-UI-A`, saído do `ajuste-BANCO-01` (PR #196, que contém o #195): **depende do #195 e do #196** (mesclar os dois antes). Executor: Claude (chat), pela ficha UI-A aprovada pelo Igor (19/11). Referência visual: imagens aprovadas em 19/11.

**Testes:** `dotnet test tests/CncStudio.Core.Tests` em Release, uma vez por fatia: 2015 aprovados nas quatro; teste mestre (`CamJobGoldenTests`, 8 G-codes) aprovado. Solução inteira em Release sem aviso nem erro. Fotos sem tela do CAM em 1280×720 conferidas em cada fatia. **Nenhum teste alterado. Core intocado** (nenhum arquivo em `src/CncStudio.Core`).

### O que mudou, por fatia
1. **UI-01 identidade.** As cores do CAM fresa e torno ficam num lugar só, `Modules/CamColors.cs` (grafite, laranja = ação e item ativo, azul = seleção, amarelo = pendência, verde = pronto). Botões, caixas e listas do Fluent usam essa paleta no CAM. Seleção na árvore e nos cartões da Preparação passou de laranja para azul. Fonte Inter embutida (pacote `Avalonia.Fonts.Inter`, licença OFL nos avisos de terceiros). Cores de estado do tema escuro (`UiKit.Status`) acompanham a paleta.
2. **UI-02 faixa.** Fresa e torno cabem em 1280×720 sem rolar: grupo Arquivo em grade 2×2, botões "em breve" estreitos só com o ícone (nome na dica), botões mais baixos (62 px), banco ativo mais estreito. Grupos do Core na mesma ordem.
3. **UI-03 propriedades.** Os títulos de seção do painel (Ferramenta, Condição de corte, Alturas…) abrem e fecham com seta; a escolha vale para todas as operações enquanto o app está aberto. Seção com campo pendente (tracejado) fica sempre aberta. Abrir trabalho passou por `OpenText` (mesma leitura de antes).
4. **UI-06 tela inicial.** O CAM abre na tela Início: trabalho de fresa, trabalho de torno, abrir trabalho (fresa ou torno pelo formato do arquivo), abrir desenho DXF, recentes (nome, caminho, tipo, data, "arquivo não encontrado"), perfis de máquina e banco. Botão **Início** no topo, ao lado de Fresa e Torno.

### Decisões do Claude (diferenças da ficha)
- **Cores em `CamColors.cs`, não no `App.axaml`:** quase todo o CAM é montado em código; um arquivo C# serve código e XAML (`x:Static`).
- **JetBrains Mono não entrou:** números e código seguem com a fonte mono do sistema (Cascadia/Consolas), como antes. Evita mais um arquivo de fonte.
- **Abas:** ficaram Preparação · Operações · Código NC, com Início · Fresa · Torno · Ferramentas à direita. Fresa e torno são trabalhos diferentes, não abas do mesmo; Simular continua sendo "Enviar ao Code".
- **Gerar e Enviar** ficam na barra de baixo, junto da linha de pendências: na faixa não cabiam em 1280×720.
- **Tempo por operação na árvore:** não entrou (o Core não calcula; vai com a linha do tempo, UI-B).
- **Barra de módulos mais fina:** não entrou (fica na janela principal, com o Code, UI-B).
- **Recentes:** o CAM passou a abrir e salvar trabalhos pelo próprio seletor (mesmo título e filtros) para saber o caminho. A lista fica em `cam-recentes.json`, ao lado do `config.json`, só com caminho, tipo e data (nada do trabalho). Arquivos novos fora da lista da ficha: `CamRecent.cs`, `CamHomeView.cs`; mexidos fora da lista: `App.axaml.cs` (registro da Inter), `CncStudio.App.csproj` (pacote) e `THIRD-PARTY-NOTICES.md`.
- `UiKit.Status` é usado por outras telas: no tema escuro, as etiquetas de estado delas também ganham as cores novas (mesmo significado).

### Teste do Igor (teste automático do PR)
1. Abrir o CAM: aparece a tela Início. Clicar em Trabalho de fresa, voltar em Início, Trabalho de torno.
2. Abrir trabalho (um de fresa e um de torno): cada um abre na tela certa. Salvar um trabalho. Voltar em Início: os dois aparecem nos Recentes; clicar num deles abre.
3. Fresa e torno com a janela em 1280×720: a faixa inteira aparece sem barra de rolagem; passar o mouse num botão apagado mostra o nome e "em breve".
4. Escolher uma operação: fechar a seção Ferramenta; escolher outra operação: continua fechada. Apagar um campo obrigatório de uma seção fechada: a seção abre sozinha.
5. Um trabalho antigo abre, salva e gera o mesmo G-code de antes.

### Pendências
- Bloco B (UI-B): bruto e fixação do torno com desenho, linha do tempo das operações (com o tempo por operação), Code e barra de módulos.
