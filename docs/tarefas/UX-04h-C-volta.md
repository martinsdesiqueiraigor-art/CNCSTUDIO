# Volta UX-04h (PR C), 14/11/2026

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).

```
ID: UX-04h (PR C)
O que foi feito (até 5 linhas):
  O pacote .casillas-cam (versão 1) leva tipo, raio de canto, ângulo da ponta, comprimento de corte e suporte quando o T do trabalho
  está no banco ativo sem conflito (Ø e nome, a mesma comparação do aviso do B2), só os cadastrados. O Code lê o banco ativo da fresa
  no Ø da análise e do simulador (sem escolha: o Primary, igual a antes) e o aviso 3 compara com ele. PARADO: o desenho do simulador
  com os campos do pacote (o SetupTool não guarda "ausente"; ver Pendências). G-code e .casillas-job iguais.
Arquivos alterados/criados:
  Core: Cam/CamHandoff.cs (Create recebe o banco; FromBank; HolderText), Cam/ToolBankPick.cs (SameTool, SameDiameter, SameName; o
  Conflict usa os mesmos), Cam/ToolBankLookup.cs (Tools/Diameters com activeMillId), Cam/CamHandoffCode.cs (BankDiameterOf; texto do aviso 3).
  App: Modules/JobView.axaml.cs (envio passa o banco ativo), Settings/AppSettings.cs (DiameterOf e ToMachineSettings com o
  ActiveMillBankId), MainWindow.CamHandoff.cs (aviso 3 com o banco ativo).
  Testes: CamHandoffBankTests.cs (novo), ToolBankActiveCodeTests.cs (novo), CamHandoffCodeTests.cs (aviso 3).
  Docs: docs/ESTADO.md, esta Volta, docs/tarefas/UX-04h-C/ (2 fotos).
Testes: rodados (dotnet test, Release, local) / todos passaram, 0 falharam (teste mestre 8 de 8 em todos os commits) / web compila
  (dotnet build src/CncStudio.Web -c Release) / torno e testes antigos sem mudança, salvo o texto do aviso 3 no teste do CAM-13.
  Não rodados: teste automático da tela (o App não tem projeto de testes; conferido por foto headless).
Versão do pacote: leitor antigo ignora os campos? (versao 1 mantida ou parou):
  versao 1 mantida. Os cinco campos já existiam no leitor v1 (tipo e suporte como texto, os outros como número) e eram sempre null;
  o leitor do CAM-13 os lê sem aviso. Chave desconhecida é ignorada (o leitor só pede as chaves que conhece): teste com "chaveNova"
  na raiz e na ferramenta, sem aviso. Por isso o suporte vai como TEXTO ("Ø 32 × 50 mm"): número no lugar de texto quebraria o leitor antigo.
O que o Code lia antes (tabela ou Primary) e onde passou a ler o banco ativo:
  Antes (desde o B1): ToolBankLookup.Tools/Diameters = banco principal de torno + por cima o Primary da fresa (o migrado "Ferramentas
  fresa"; sem ele, o primeiro por nome), em AppSettings.DiameterOf (Ø efetivo do desenho) e AppSettings.ToMachineSettings (Ø da
  análise, G41/G42 e simulação); o aviso 3 usava o ToolDiameterOf desse ToMachineSettings.
  Agora: os dois usam ToolBankPick.ActiveMillBank(Banks, ActiveMillBankId) no lugar do Primary da fresa (torno igual). Banco apagado,
  de torno ou sem escolha: o Primary. O simulador por dentro não foi tocado.
  Ainda no Primary (não alimenta o simulador, fora do escopo): a folha de setup (MainWindow.axaml.cs:830, ToolBankLookup.Tools sem o ativo).
Setup montado: como ficou ausente × preenchido, e o SetupTool:
  PARADO, como a ficha manda. O SetupTool (MachiningSetup.cs:373) só tem campos double com padrão: Kind = FlatEndMill, Diameter 10,
  TipAngle 118, CuttingLength 25, HolderDiameter 35, HolderLength 45. Não guarda "ausente" sem mudar MachiningSetup/SetupJson. Nenhum
  SetupTool é criado a partir do pacote; o setup montado continua só com o bruto (como no CAM-13 PR 2), material vazio.
  O Ø do CAM continua valendo pelo ApplyDiameters. O desenho segue genérico (topo reto com o Ø efetivo).
Aviso 3: texto que ficou e o que compara:
  "T{n}: o CAM usou Ø {x} mm e o banco tem Ø {y} mm. Neste programa vale o do CAM; o banco não foi alterado."
  Compara o Ø do CAM com o Ø do banco ativo da fresa (CamHandoffCode.BankDiameterOf). T fora do banco ou sem Ø: não avisa.
  Inativa conta. Só o Ø (o nome não entra no aviso 3, como antes).
O que o envio faz com T que não existe no banco:
  Nada no banco nem na tabela antiga (o B2 tirou a gravação). O T vai no pacote com T, Ø, nome, rpm e avanços do trabalho e os
  cinco campos null; no Code o Ø do CAM vale para o programa, sem aviso 3 (não há com o que comparar).
Proposta: como o Core recebe o banco; o que aparece ao usuário; nome do banco no pacote:
  Banco: parâmetro opcional ToolBank? em CamHandoff.Create (a casca resolve com ToolBankPick.ActiveMillBank; o Core não lê arquivo).
  Feito assim.
  Ao usuário: a etiqueta "do CAM" que já existe nas Estatísticas (foto 02). Quando o desenho entrar, proponho a mesma etiqueta,
  sem estilo novo. Nada mais foi acrescentado à tela.
  Nome do banco no pacote: proponho SIM, chave opcional "banco" (texto) na raiz, só informativa (diz de onde vieram os cinco campos
  quando o banco muda depois). O leitor v1 ignora chave desconhecida, então não exige versão nova. Não implementei (aguarda o Igor).
Fotos 1280×720 (arquivos em docs/tarefas/):
  UX-04h-C/UX-04h-C-01-aviso3-banco-ativo.png (banco ativo "Banco oficina": T2 Ø 8 × CAM Ø 6, aviso na faixa acima do gráfico)
  UX-04h-C/UX-04h-C-02-estatisticas-ferramentas.png (T1 e T2 com "do CAM").
  Roteiro headless fora do repositório (MainWindow real em 1280×720). Ele grava config e bancos de cenário no %APPDATA%: fiz cópia
  antes, restaurei no fim e conferi os hashes (idênticos). Sem foto do simulador com campos preenchidos: essa parte parou.
Decisões tomadas: (só as autorizadas)
  Pendência (b) do B2 aplicada como decidida (banco ativo em quem alimenta o simulador). Texto do aviso 3 com "o banco" (a ficha
  autoriza quando o texto dizia "tabela"). Suporte em texto "Ø d × l mm" / "Ø d mm" / "comprimento l mm" (o campo do pacote é texto;
  ver Riscos). Tipo no pacote = código estável do banco ("topo", "toroidal", "broca"...).
Pendências e dúvidas: (A definir)
  1. Desenho com os campos do pacote (critério 1 da seção 7): opções, para o Igor escolher:
     (a) sem SetupTool: um perfil de corte montado do pacote (esférica → ponta esférica; toroidal com raio → toroidal; broca com ângulo →
         cone; o que falta segue topo reto) entregue pelo MillProfiles. Mexe só em quem alimenta o simulador. Comprimento de corte e
         suporte continuam genéricos, porque o desenho (SceneMeshes.MillTool) só os recebe por um SetupTool. Muda a remoção de material
         desses T (é o objetivo).
     (b) SetupTool com campos que aceitam ausente: muda MachiningSetup e SetupJson (proibido nesta ficha).
     (c) comprimento de corte e suporte desenhados sem SetupTool: mexe no SceneMeshes/SimulationScene (simulador por dentro).
  2. Folha de setup ainda lê o Primary (não alimenta o simulador): passar para o banco ativo?
  3. Nome do banco no pacote (proposta acima).
Riscos ou contradições encontradas:
  - Base: o #172 (B2) não está no dev; este ramo parte dele. Se o B2 mudar, este ramo precisa ser refeito por cima.
  - "suporte" é texto no pacote e o banco tem Ø e comprimento numéricos: o Code não consegue usar o texto para desenhar sem
    interpretá-lo. Para desenhar o suporte, o melhor seria chaves numéricas novas e opcionais (o leitor v1 ignora), decisão do Igor.
  - Um comentário antigo no teste do CAM-13 diz que o T novo "vai entrar na tabela no envio"; desde o B2 isso não acontece (não mudei o teste).
Não conferido: .exe; Prévia do GitHub; outras resoluções além de 1280×720; abrir/fechar o App de verdade trocando o banco ativo;
  bancos e programas reais do Igor.
O que o Igor deve testar na prévia:
  1. CAM: escolher T do banco ativo, Enviar ao Code, salvar o programa e abrir o PECA.nc.casillas-cam: os cinco campos preenchidos
     só para T sem conflito (o desenho ainda é genérico).
  2. Mesmo T com Ø diferente no banco ativo: aviso 3 com "o banco tem Ø ...", vale o Ø do CAM, nada preenchido.
  3. Trocar o banco ativo no CAM e abrir um programa no Code: o Ø sem setup vem do banco ativo; sem escolha, igual a antes.
  4. Programa e .casillas-cam antigos abrem e simulam como hoje.
Próximo passo sugerido (1): o Igor escolher a opção do desenho (pendência 1); a (a) cabe neste ramo em ~150 linhas.
```
