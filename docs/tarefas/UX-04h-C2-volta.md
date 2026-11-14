# Volta UX-04h (PR C, parte 2), 14/11/2026

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).

```
ID: UX-04h (PR C, parte 2)
O que foi feito (até 5 linhas):
  O simulador do Code corta e desenha com o tipo, o raio de canto e o ângulo da ponta que vieram no pacote, sem SetupTool
  (esférica, toroidal com raio, broca com ângulo; o resto topo reto, igual a antes). A folha de setup lê o banco ativo da fresa.
  O pacote leva o nome do banco na chave opcional "banco" (versão 1 mantida). G-code e .casillas-job iguais.
  Mesmo ramo (fase-ux04h-pacote-banco), mesmo PR #173 (rascunho, base dev). Sem merge.
Arquivos alterados/criados:
  Core: Cam/CamHandoff.cs (BankName), Cam/CamHandoffJson.cs (chave "banco"), Cam/CamHandoffCode.cs (ProfileOf, PackageProfiles),
  Simulation/SimulationFactory.cs (MillProfiles com o parâmetro opcional fromPackage).
  App: MainWindow.axaml.cs (ProfileOf do simulador com o pacote; folha de setup com o ActiveMillBankId),
  MainWindow.CamHandoff.cs (CamPackageProfiles, refeito só quando o pacote da aba muda).
  Testes: CamHandoffProfileTests.cs (novo), CamHandoffBankTests.cs (chave "banco"), ToolBankActiveCodeTests.cs (folha de setup).
  Docs: docs/ESTADO.md, esta Volta, docs/tarefas/UX-04h-C2/ (4 fotos).
  Código: ~75 linhas (sem testes); testes ~185. Abaixo do limite de ~300.
Testes: rodados (dotnet test, local) / 1862 passaram, 0 falharam; teste mestre (CamJobGoldenTests) 8 de 8 em todos os commits /
  build Release sem aviso; web compila (dotnet build src/CncStudio.Web -c Release, 0 avisos); torno e testes antigos sem mudança,
  salvo 2 testes do C ajustados para a chave "banco" (ver Decisões).
  Não rodados: teste automático da tela (o App não tem projeto de testes; conferido por foto headless).
Perfil de corte do pacote: como ficou e onde entra (MillProfiles):
  CamHandoffCode.ProfileOf(ferramenta do pacote, Ø efetivo): "esferica" → ponta esférica; "toroidal" com raio de canto → toroidal;
  "broca" com ângulo da ponta → cone. Tipo ausente, outro tipo, campo que falta ou valor fora da faixa (raio < 0 ou > Ø/2; ângulo
  fora de 0–180°): null → topo reto com o Ø efetivo (o genérico de hoje). Nenhum Ø10, 118° ou 25/35/45 escondido; nada deduzido.
  SimulationFactory.MillProfiles(setup, Ø, fromPackage): setup do programa (se o T está nele) → pacote → topo reto. Sem pacote, o
  mesmo de antes (o parâmetro é opcional; a web e o 3D do CAM não passam nada). O desenho (SceneMeshes) já usa esse mesmo perfil:
  não foi tocado. Comprimento de corte e suporte seguem genéricos; o texto do suporte não é interpretado.
  Ferramenta de cada T = a mesma que dá o Ø (CamHandoffCode.Diameters: a primeira com Ø).
  Testes: os três tipos; 14 casos de campo faltando/outro tipo/fora da faixa → topo reto; mesma remoção de material com a ferramenta
  sem os campos (mapa de alturas idêntico); com os campos, muda só a da ferramenta que os tem; setup vale por cima; nenhum SetupTool
  criado (setup serializado igual).
Folha de setup: de onde lia e para onde passou:
  Antes: ToolBankLookup.Tools(Banks) = torno principal + fresa Primary (MainWindow.axaml.cs:832).
  Agora: ToolBankLookup.Tools(Banks, ActiveMillBankId), o banco ativo da fresa, como o Ø do simulador. Sem escolha ou banco
  apagado: o Primary, igual a antes. O Ø da folha continua vindo da análise (setup → CAM → banco).
Nome do banco no pacote: ficou assim:
  Chave "banco" (texto) na raiz, depois de "ferramentas", com ToolBank.Name do banco ativo usado no envio. versao 1. Sem banco
  (null), a chave não é gravada (nem como null). Pacote antigo sem a chave: BankName null, sem aviso, grava o mesmo texto de volta.
  Chave desconhecida junto: ignorada sem aviso. .casillas-job e G-code iguais (teste).
Relatório (seção 4): fontes de ferramenta que o Code ainda lê:
  1. Tabela antiga do config.json (AppSettings.Tools, AppSettings.cs:96):
     a) Trabalho do torno (MainWindow.Modules.cs:77 → CamView.ToolsSource → LatheJobView.axaml.cs:664/682 e LatheJobView.Props.cs:442):
        lê e GRAVA (SaveTools) a ferramenta do torno. Para unificar: PR D (banco de torno ativo, como o B2 fez na fresa).
     b) Assist, plano 2D (MainWindow.Modules.cs:161, PlanTools): T, Ø, "é broca" e descrição para as travas do plano (ferramenta
        nova, broca). Para unificar: montar os PlanTool do banco ativo da fresa (Type == Drill no lugar do Kind antigo).
     c) Migração única (MainWindow.ToolBank.cs:13, AppSettings.LegacyToolRows, AppSettings.cs:118): lê uma vez para criar os bancos.
        Fica como está (é a ponte).
  2. Primary (o banco "principal", ToolBankLookup.Primary, ToolBankLookup.cs:11):
     a) Fresa: só como reserva quando não há banco ativo ou ele foi apagado (ToolBankPick.cs:28). Já é o comportamento desejado.
     b) Torno: ToolBankLookup.Tools (ToolBankLookup.cs:28) usa o Primary de torno para o Ø do torno no simulador, e
        LatheSetupTools (ToolBankLookup.cs:47) → AppSettings.WithToolKinds (AppSettings.cs:170) → Graphics.Setup
        (MainWindow.axaml.cs:707) para o desenho do torno (tipo, Ø e inserto, com 10 e 3 de reserva). Para unificar: banco ativo
        de torno (ActiveLatheBankId), no PR D.
  3. Padrão da máquina (DefaultToolDiameter = 10, AppSettings.cs:90, usado em :295 e :305): Ø quando o T não está no banco.
     Não é banco, é reserva; fica, mas vale dizer ao usuário de onde veio.
  4. Comentário da troca ("FRESA D10", ProgramAnalyzer.cs:55): Ø quando nem setup, nem CAM, nem banco têm. Fica (é do programa).
  5. Setup do programa (MachiningSetup.Tools, por programa): vale por cima do banco no Ø e no perfil. É outra fonte de ferramenta
     com padrões escondidos (Ø10, 118°, 25/35/45). Para "um banco só": o setup passaria a guardar só o T e ler o resto do banco
     ativo (muda MachiningSetup/SetupJson; decisão do Igor).
  6. 3D do Trabalho do CAM (JobSceneView.cs:115): simula com o Ø da ferramenta da operação (cópia no .casillas-job), topo reto.
     Não lê banco. Para unificar: passar o perfil do banco ativo (o mesmo PackageProfiles, a partir do banco).
  7. Biblioteca do Assist (AssistLibrary): só materiais, morsas e castanhas; não tem ferramenta. Nada a mudar.
  8. Web ("minhas ferramentas" no navegador): fora do desktop; o desktop só a importa para o banco (ToolBankImport).
  Nada foi mudado por causa deste relatório.
Proposta: etiqueta "do CAM" no desenho; tipos extras mapeáveis:
  Etiqueta: a mesma "do CAM" das Estatísticas (Statistics.FromCam = ToolFromCam), na linha de leitura abaixo do 3D, logo depois de
  "Ferr. T41 Ø10", quando o T vem do pacote e o setup não o redefine. Mesmo texto, mesmo estilo; nada novo. Não implementado.
  Tipos extras: broca de centro com ângulo da ponta → cone (o banco já tem o campo TipAngle); facear com raio de canto → toroidal
  (o banco já tem CornerRadius). Macho, alargador e mandrilhadora: topo reto com o Ø (não há campo de ponta que baste; o macho
  pediria um chanfro de entrada, campo novo). Não implementado.
Fotos 1280×720 (arquivos em docs/tarefas/):
  UX-04h-C2/UX-04h-C2-01-esferica.png (T41 esférica Ø10: canal com fundo redondo, saída redonda na face)
  UX-04h-C2/UX-04h-C2-02-toroidal.png (T42 toroidal Ø10 R2: fundo reto com cantos arredondados)
  UX-04h-C2/UX-04h-C2-03-broca.png (T43 broca Ø10 118°: furos com fundo em cone)
  UX-04h-C2/UX-04h-C2-04-sem-campos.png (T44 sem campos: topo reto, como antes)
  MainWindow real, headless, programa com o pacote ao lado (bruto 60×40×20 do pacote). Roteiro fora do repositório. O App grava o
  config.json ao fechar: fiz cópia do %APPDATA%\CncStudio antes, restaurei e conferi os hashes (idênticos). T41–T44 para não
  cruzar com o banco real (sem aviso 3 nas fotos).
Decisões tomadas: (só as autorizadas)
  As da seção 2 da ficha. Detalhes de execução: o setup do programa vale por cima do pacote (mesma ordem do Ø); valor fora da faixa
  conta como ausente (topo reto). Banco vazio (existe, sem ferramentas) grava a chave "banco": "sem banco" = nenhum banco ativo.
  Por isso o teste do C "banco vazio dá o pacote de hoje" passou a dizer "igual, mais a chave banco".
Pendências e dúvidas: (A definir)
  1. Banco ativo existe mas nenhum T veio dele: gravar "banco" mesmo assim (feito) ou só quando algum T trouxe campos?
  2. Etiqueta "do CAM" no desenho e tipos extras: propostas acima, aguardam o Igor.
  3. Relatório: ordem da unificação (PR D torno; Assist; 3D do CAM; setup do programa).
Riscos ou contradições encontradas:
  - Na foto, a ponta desenhada (esfera, cone) é discreta no zoom padrão; o que se vê bem é a remoção de material. A ponta desenhada
    sai do mesmo perfil (testado no Core), mas não conferi de perto na tela.
  - A vista de lado/frente não anima no headless; as fotos ficaram na isométrica.
  - Se o Igor editar à mão o tipo no .casillas-cam, um código errado vira topo reto sem aviso (nada suposto, mas silencioso).
Não conferido: .exe; Prévia do GitHub; outras resoluções além de 1280×720; abrir/fechar o App de verdade trocando o banco ativo;
  folha de setup aberta no navegador (só o teste do Core); bancos e programas reais do Igor.
O que o Igor deve testar na prévia:
  1. No banco ativo, cadastrar T com tipo esférica, toroidal (com raio) e broca (com ângulo); usar no Trabalho fresa; Enviar ao Code:
     o 3D mostra a canaleta redonda / cantos arredondados / fundo em cone. T sem esses campos: igual a antes.
  2. Salvar o programa e abrir o PECA.nc.casillas-cam: chave "banco" com o nome do banco ativo.
  3. Trocar o banco ativo e abrir a folha de setup: nomes das ferramentas desse banco.
  4. Conferir que o G-code gerado não mudou.
Próximo passo sugerido (1): PR D (torno no banco ativo), que fecha os itens 1a e 2b do relatório.
```
