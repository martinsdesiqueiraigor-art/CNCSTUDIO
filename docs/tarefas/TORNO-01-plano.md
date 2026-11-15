# TORNO-01: plano do torno no CAM

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).
Só o plano. Nenhum código de produto neste ramo. Base: `origin/dev` em `1cc92a6`.
Nada foi testado em máquina nem rodado: é leitura de código. O que não foi conferido está escrito "não conferido".
Fora do plano, por ordem do Igor (14/11): banco de ferramentas, armário e operações novas de fresa. Depois do torno vem o Modelo 3D no Trabalho (não planejado aqui).
Mudar G-code, `.casillas-job` ou ciclos é G e decisão do Igor: este plano só propõe.

## (a) O que existe hoje (conferido no código)

### Core: operações e o que geram
`LatheOpKind` tem 5 tipos: Turning, Facing, Drilling, Threading, Grooving (`Core/Cam/LatheJob.cs:6`). A operação é só parâmetros (`LatheOperationSpec`, `:18`); o programa nasce de `LatheJobBuilder.Build` (`:170`).

| Operação | Onde | Fanuc | Siemens |
|---|---|---|---|
| Torneamento externo e interno | `Lathe.cs:149` (`TurningParams` `:136`) | Desbaste G71 + acabamento G70 (`LathePost.cs:246`); rebaixo vira G71 tipo II | `CYCLE95` com os contornos depois do M30 (`:262`) |
| Só acabamento (sem desbaste) | mesma | perfil em G1/G2/G3 | igual |
| Faceamento | `Lathe.cs:242` | movimentos G0/G1 em passes de Z (`LathePost.cs:11`) | igual |
| Furação axial | `Lathe.cs:284` | movimentos G0/G1 com bicada (não G83) | igual |
| Rosca externa e interna | `Lathe.cs:323` | G76 em dois blocos (`LathePost.cs:154`) | `CYCLE97` (`:162`) |
| Canal externo e interno (radial, retangular) | `Lathe.cs:378` (`GroovingParams` `:510`) | movimentos G0/G1, mergulhos com quebra-cavaco | igual |

- Cabeçalho Fanuc `G21 G18 G40 G97 G99` (`LathePost.cs:48`); Siemens `G71 G18 G40 G90 G95 DIAMON` (`:54`). X é diâmetro, avanço em mm/rot.
- Rotação: fixa em G97, ou G96 com limite: `G50 S` no Fanuc (`:138`) e `LIMS=` no Siemens (`:143`). `LatheTool.Validate` exige rotação máxima maior que zero (`Lathe.cs:9`, a validação).
- Desbaste e acabamento do perfil são **duas caixas da mesma operação** (`Rough` e `Finish` em `TurningParams`), não fases separadas. Para usar duas ferramentas, duplica-se a operação.
- Canal radial não chega ao centro: o fundo precisa de Ø maior que zero (`Lathe.cs:378`, validação do fundo). Não há canal de face nem sangramento no Core: o enum não os tem.

### Tela do Trabalho do torno
`App/Modules/LatheJobView*`. A casca é a da fresa: abas Preparação, Operações e Código NC, faixa de ícones, árvore, painel.
- Árvore (`LatheJobView.axaml.cs:569-595`): Máquina, Material ("em breve"), Peça, Bruto (só leitura), Banco de ferramentas ("em breve"), Fixação 1 com o Zero e as operações dentro, Fixação 2 ("em breve").
- Preparação (`LatheJobView.axaml:90-111`, `LatheJobView.Props.cs:47-58`): cartões Peça (perfil por DXF ou pontos), Bruto (só leitura: lista o Ø digitado nas operações, `Props.cs:62`), Material (texto "em breve"), Fixação (`:194`) e Zero (só texto, `:95`). Não há cartão Máquina; a Máquina é só uma linha da árvore.
- Faixa (`Core/Cam/CamPalette.cs:156-188`): Arquivo, Fixação (Nova fixação e Zero, "em breve"), Torneamento, Canal e corte (Canal de face e Sangrar, "em breve", `:180-181`), Rosca.
- Escolha de ferramenta: seletor "Banco ativo" do torno (D1, `LatheJobView.axaml.cs:32`, `Props.cs:441`).

### Fixação
`LatheFixation` (`Core/Cam/LatheFixation.cs:7`): três tipos, "castanhas", "placa" e "ponto" (entre pontas = castanhas + contraponto), com Ø, comprimento preso e balanço opcionais. É só informativa: não entra no programa e nunca é verificada (`LatheJob.cs:80`; estado sempre "não verificada", `LatheFixation.cs:81`). Castanha dura e mole não se distinguem. Luneta não existe.

### Zero
Fixo no Core: X0 no centro e Z0 o do perfil (`LatheJobInfo.OriginTitle`, `LatheJob.cs:295`); o G5x vem de `WorkOffset` (`:72`). O cartão Zero é só texto (`Props.cs:95`). Não há escolha de Z0.

### "Nada suposto" no torno
- **Vale em parte.** A regra do CAM-12 existe: campo vazio marca a operação Incompleta, trava Gerar e Salvar (`MissingFields`, `LatheJob.cs:46`; `LatheJobInfo.MissingError`, `SaveBlockers` `:374`).
- **Não vale** em três pontos: (1) operação nova nasce com valores padrão (Ø40, passe 1,5, 200 m/min, 3000 rpm etc., `NewOperation`, `LatheJob.cs:515`), ao contrário da fresa; (2) `ProgramNumber` nasce 1002 e `WorkOffset` nasce 54 (`LatheJob.cs:70,72`) e **não há campo na tela** para digitar nenhum dos dois (só lidos do arquivo, `LatheJobView.axaml.cs:314-315`); (3) `Coolant` nasce ligado (`:74`) sem campo na tela.

### Enviar ao Code
O botão manda só o texto do programa e o nome `.nc` (`LatheJobView.axaml.cs:94-99`, `OpenInEditor`). O torno **não** usa `CamHandoff`: nenhum uso na tela (confirmado por busca). O pacote já tem o campo `Machine` com o valor "torno" reservado ("só a fresa é preenchida", `Core/Cam/CamHandoff.cs:136-147`).

### O que o Code simula do torno
O executor tem torno (`Core/Execution/MachineExecutor.Lathe.cs`): G96 com G50 (`:95`), G70/G71/G76 (`:260`, `:454`, `:534`) e `CYCLE95`/`CYCLE97` (`MachineExecutor.Drilling.cs:156`). A prévia da tela do torno simula o programa gerado com esse executor (`LatheJobView.axaml.cs:643`) e há vista 3D (`:519-527`). Sem bruto dado, a barra do torno é **adivinhada pela trajetória** (`SimulationFactory.cs:118`). Canal e sangramento, sendo G0/G1, o executor já lê. Peça solta, fixação e colisão não são simuladas (`ESTADO.md`, "Limites"). Pendência anotada: G58/G59 do Siemens vira zero 5 e 6 (`MachineExecutor.cs:630`).

### Testes que existem
`LatheTests`, `LatheJobTests`, `LatheGrooveCamTests`, `LatheThreadCamTests`, `LatheFixationTests`, `LathePaletteMissingTests`, `LatheJobSetupInfoTests`, `LatheToolTypesTests`, `ToolBankLatheMigrationTests`. O teste mestre cobre o torno: `torno-eixo.casillas-job` contra `torno-eixo.nc` (`CamJobGoldenTests.cs:47-52`). Não li o conteúdo dos testes: **não conferido** o que cada um cobre.

## (b) O que falta, item por item

| Item da lista do Igor | Situação | O que falta |
|---|---|---|
| Faceamento | existe | nada |
| Desbaste longitudinal | existe | nada |
| Acabamento interno e externo | existe | duas caixas na mesma operação; sem fase própria (não é falta) |
| Canal externo e interno | existe | nada |
| Canal de face | **não existe** | tudo (Core, tela, pós, teste); só o botão "em breve" |
| Rosca interna e externa | existe | nada |
| Furo | existe | nada |
| Sangramento | **não existe** | tudo; o canal radial não chega ao centro |
| Zero: X0 no centro | existe | fixo; sem erro |
| Zero: Z0 na face pronta ou na face oposta | em parte | Z0 é o do perfil; não há escolha por trabalho |
| Fixação: castanha dura e mole | em parte | só "castanhas" |
| Fixação: contraponto | em parte | só como "entre pontas" |
| Fixação: luneta | **não existe** | tipo novo |
| 2ª fixação (um programa por fixação, lado 2 parte do lado 1) | **não existe** | modelo, tela, programa por fixação, arquivo |
| Preparação no nível da fresa | em parte | ver (c) |
| CAM simples: Torno → Modelo 3D → fecho | decisão tomada | só ordem |

## (c) Preparação do torno no nível da fresa

| Fresa (já feito) | Torno hoje | O que falta |
|---|---|---|
| Cartão Máquina (`JobView.Prep.cs:122`) | só linha na árvore (`LatheJobView.axaml.cs:569`) | cartão com o mesmo editor de perfil de máquina |
| Peça pronta (`JobView.Prep.cs:410`) | cartão Peça (perfil) | nada relevante |
| Bruto (`JobView.Prep.cs:472`) | só leitura, derivado das operações | bruto do trabalho (Ø, comprimento): Decisão 2 |
| Zero (`JobView.Zero.cs`; 9 pontos, topo ou base) | texto | Z0 por trabalho: Decisão 3 |
| Programa nº e G5x digitados (`JobView.Props.cs:543`) | sem campo; 1002 e 54 de fábrica | dois campos digitados, vazio trava Gerar e Enviar: Decisão 1 |
| Operação nova com T, Ø, rotação e avanços vazios (B2) | nasce com padrão | Decisão 1 |

Fica fora: Material (continua "em breve") e Banco de ferramentas (não muda).

## (d) Canal de face e sangramento

Proposta. Gera G-code novo: é G. Os parâmetros seguem o canal radial já existente (`GroovingParams`), todos digitados, sem padrão escondido; campo vazio deixa a operação Incompleta (regra do CAM-12).

**Canal de face (axial).** A ferramenta mergulha em Z na face e o canal ocupa uma faixa de raio.
- Parâmetros: Ø externo e Ø interno do canal; Z da face; profundidade (Z do fundo); largura da ferramenta; mergulho por passe; avanço (mm/rot); sobremetal de acabamento; folga de aproximação; rotação (G96 com limite, ou fixa).
- Ferramenta: bedame. A largura vem da largura do inserto do banco (`BankTool`, só bedames, `ToolBank.cs:93`). O banco não ganha tipo novo (Decisão 4).
- O que gera: movimentos G0/G1, igual nos dois controles, como o canal radial: mergulhos em Z com quebra-cavaco, passes laterais em X, acabamento nas paredes e no fundo.
- Riscos: bedame de face tem a lâmina curva por faixa de diâmetro; um canal fora da faixa da ferramenta bate na parede (não conferido no código; **o plano pede a faixa de Ø da ferramenta como campo digitado, sujeito à confirmação do Igor**). Em G96 a rotação sobe ao diminuir o Ø: o limite G50/LIMS precisa estar digitado e dentro do fuso.

**Sangramento (corte).** Canal radial que vai até o centro (ou a um pino).
- Parâmetros: Z do corte (aresta de maior Z); Ø de início; Ø final (0 = até o centro); largura da ferramenta; avanço; avanço reduzido e Ø a partir do qual ele vale; mergulho/recuo para quebrar cavaco; rotação máxima (limite de G96); folga.
- O que gera: G1 radial até o Ø final, com o avanço reduzido no trecho final, recuo e afastamento. Igual nos dois controles.
- Riscos: (1) **rotação limite**: em G96 o fuso tende ao limite quando o Ø vai a zero; o limite digitado precisa caber na peça e no fuso. (2) **avanço reduzido no fim**: sem ele a ferramenta quebra no centro; é campo digitado, vazio trava. (3) **peça solta**: com Ø final 0 a peça cai; o aviso fixo na tela, a ordem (sangrar é a última operação da fixação) e a verificação "sem operação depois do sangramento" são propostas. O app não verifica fixação nem queda (limite já registrado); simular e rodar a 1ª vez no ar.
- O Code simula como G1 comum; **não simula a queda da peça** (não conferido além do que o executor lê).

## (e) 2ª fixação

Hoje o `LatheJob` tem **um** perfil, **uma** lista de operações, **uma** fixação, **um** `WorkOffset` e **um** `ProgramNumber` (`LatheJob.cs:64-80`). Um programa por trabalho.

Proposta de modelo: o trabalho passa a ter uma lista de fixações; cada uma com sua `Fixation`, `WorkOffset`, `ProgramNumber`, zero e lista de operações. O botão "Gerar G-code da fixação 1" (`LatheJobView.axaml.cs:103`) vira "da fixação N". O pós não muda: `LathePost.Generate` já recebe lista de operações e opções, então roda uma vez por fixação, e os G-codes das operações existentes saem iguais (golden intacto).

**Lado 1 vira bruto do lado 2.** Proposta: o perfil do lado 1 vira o bruto do lado 2, **mostrado na tela e editável antes de gerar** (nada escondido). Dois pontos não conferidos: como a parte do perfil que fica na placa (Z mais negativo) entra nessa conta; e se o perfil atual cobre sempre a peça inteira.

**Efeito no `.casillas-job`** (formato `casillas-job-torno`, versão 1, `LatheJobJson.cs:25`):
- Arquivo antigo (versão 1) abre igual: vira uma fixação (os campos de hoje).
- Subir a versão é decisão do Igor (Decisão 8). A versão antiga recusa arquivo mais novo com aviso (`LatheJobJson.cs:184-187`): bom para o caso "abrir um trabalho de 2 fixações no app velho".
- Sem subir a versão, o app velho abriria e **perderia a fixação 2 ao salvar**. Risco.

## (f) Pacote `.casillas-cam` do torno

Vale. Hoje, sem bruto dado, a barra do torno no Code é adivinhada pela trajetória (`SimulationFactory.cs:118`) e o Ø vem do banco só pelo T. O pacote leva bruto (`HandoffStock` já tem tipo Redondo e Tubo), zero, ferramentas do banco ativo do torno e a fixação, como na fresa.
- O campo `Machine = "torno"` já existe (`CamHandoff.cs:138`).
- Tamanho: médio. Core (criar o pacote do `LatheJob`), App (o botão passa a mandar o pacote) e Code (ler `Machine = "torno"`: **não conferido** o que `MainWindow.CamHandoff.cs` faz com ele).
- Risco: pacote errado muda o bruto da simulação. Mitigação: o aviso de programa diferente já existe (`CamHandoff.MismatchWarning`). `versao: 1` se mantém (acrescentar chaves opcionais); **não conferido** como o Code antigo reage a `machine = "torno"`.
- Com 2ª fixação: um pacote por programa, uma fixação cada.

## (g) Riscos já conhecidos: em qual fatia entram

| Risco | Decisão | Fatia |
|---|---|---|
| Faixa rola em 1280 (fim do grupo Rosca escondido) | causa **não conferida** (layout em `CamShell.BuildPalette`); corrigir antes de mexer na faixa | T0 |
| Trocar o banco ativo não avisa quando o T da operação não está no banco novo | reusar o aviso de T fora do banco que a fresa já tem (B2, `ToolBankPick.cs`) | T0 |
| Botão 3D do torno sobre fundo escuro | só visual; não conferido onde | T0 |
| Mesmo T na fresa e no torno vale o Ø da fresa (`ToolBankLookup.Tools`, `ToolBankLookup.cs:25`) | **fica fora**: separar por máquina muda o Ø de quem tem T repetido; só com ordem do Igor. O pacote do torno (T7) o contorna para quem o usar | fora |
| Bedame interno e barras MVQNR (H:13) | o bedame interno já existe no banco (`ToolBank.cs:55`); barras MVQNR **não conferido** | fora |
| Regra antiga do fuso no torno | a conferência é a mesma da fresa (`LatheJobInfo.SpindleWarning`, `LatheJob.cs:304`); **não conferido** o que é "antiga". Entra só a parte do limite de rotação do sangramento | T3 |

## (h) Decisões do Igor

1. **Valores de fábrica do torno** (Programa nº 1002, G5x 54, refrigeração ligada, operação nova com padrão).
   a) Programa nº e G5x digitados, sem padrão; refrigeração digitada; operação nova como hoje. **(recomendo: mínimo)**
   b) Tudo como a fresa: também a operação nova nasce vazia (vira PR a mais).
   c) Manter como está.
2. **Bruto do torno.**
   a) Fica só leitura, como hoje.
   b) Bruto no trabalho (Ø e comprimento), digitado; operações novas nascem com esse Ø; as antigas mantêm o seu. **(recomendo)**
   c) Bruto obrigatório no trabalho e some o campo por operação (G, muda o arquivo).
3. **Z0: face pronta ou face oposta.**
   a) Campo por trabalho; na face oposta o Core desloca os Z (G-code muda). **(recomendo, junto da 2ª fixação)**
   b) Só texto informando qual é, sem mudar o programa.
   c) Manter o Z0 do perfil.
4. **Bedame de face e sangramento no banco.**
   a) Usar o bedame que já existe (largura do inserto), sem tipo novo. **(recomendo: o banco não muda)**
   b) Tipo novo "bedame de face" no banco.
5. **Ø final do sangramento.**
   a) Digitado: 0 corta até o centro (peça solta, com aviso); maior que 0 deixa um pino. **(recomendo)**
   b) Sempre até o centro.
   c) Sempre com pino (nunca solta a peça).
6. **Avanço e rotação no sangramento e no canal de face.**
   a) Avanço reduzido e seu Ø digitados; limite de rotação obrigatório e conferido com o fuso. **(recomendo)**
   b) Avanço único.
7. **Tipos de fixação.**
   a) Acrescentar castanha dura, castanha mole, contraponto e luneta (informativos; nomes antigos ficam). **(recomendo)**
   b) Manter os 3 atuais.
8. **Arquivo com 2 fixações.**
   a) `.casillas-job` do torno versão 2, com lista de fixações; versão 1 abre como uma fixação. **(recomendo)**
   b) Versão 1 com campo opcional (o app velho perde a fixação 2 ao salvar).
   c) Um arquivo por fixação (sem mudar formato; o lado 2 é outro trabalho).
9. **Como o lado 2 parte do lado 1.**
   a) Derivado do lado 1, mostrado e editável antes de gerar. **(recomendo)**
   b) Digitado de novo, com o valor do lado 1 só de sugestão.
   c) Só digitado (como hoje).
10. **Pacote `.casillas-cam` do torno.**
    a) Sim, depois do mínimo. **(recomendo)**
    b) Sim, dentro do mínimo.
    c) Não.
11. **Faixa e banco (T0):** os três ajustes entram juntos em uma fatia P? a) sim **(recomendo)** b) um PR por ajuste.

## (i) Fatias

Cada PR sai do `dev`, com o teste mestre (8 G-codes) verde antes e depois. "G" = sobe de nível por mexer em G-code ou arquivo.

| Fatia | O que muda | Tamanho | G-code / arquivo |
|---|---|---|---|
| **T0** Ajustes de tela | Faixa visível em 1280; aviso de T fora do banco ao trocar o banco; botão 3D legível | P | igual |
| **T1** Preparação | Cartão Máquina; Programa nº, G5x e refrigeração digitados (Decisão 1a); bruto do trabalho (2b) | M | igual para trabalho antigo (os valores vêm do arquivo); arquivo novo sem a chave se vazio |
| **T2** Canal de face | Operação nova no Core, tela, pós (G0/G1), botão ativado, testes e exemplo | G (G-code novo) | novo; os existentes iguais |
| **T3** Sangramento | Operação nova, avanço reduzido, limite de rotação, aviso de peça solta, "é a última operação" | G | novo; os existentes iguais |
| **T4** Fixação | Tipos novos (castanha dura/mole, contraponto, luneta) | P | arquivo: nomes novos de fixação |
| **T5** Zero | Z0 na face pronta ou oposta (Decisão 3a) | G | muda Z na face oposta; face pronta igual |
| **T6** 2ª fixação | Lista de fixações, programa por fixação, lado 2 do lado 1, arquivo versão 2 | G | arquivo novo; antigo abre igual |
| **T7** Pacote do torno | `.casillas-cam` com bruto, zero, ferramentas e fixação | M (dados: G) | pacote versão 1, chaves opcionais |

Ordem: T0, T1, T2, T3 (mínimo) | T4, T5 e T6 (juntos faz sentido), T7.

**Mínimo para o torno estar pronto: T0 + T1 + T2 + T3.** Torno com faceamento, desbaste, acabamento, canal radial e de face, rosca, furo e sangramento, com Preparação no nível da fresa. Zero na face pronta continua o de hoje. T4 a T7 ficam para depois, nessa ordem.

**Teste do Igor no .exe (3 a 5 passos):**
- **T0:** (1) janela em 1280×720, abrir o Trabalho do torno e ver todos os grupos da faixa, até Rosca; (2) trocar o banco ativo com uma operação cujo T não existe nele e ver o aviso; (3) ligar o 3D e ler o botão com o fundo escuro; (4) abrir `torno-eixo.casillas-job` e ver o mesmo programa.
- **T1:** (1) abrir trabalho novo e ver Programa nº e G5x vazios com o motivo; (2) tentar Gerar e ver travado; (3) digitar os dois e Gerar; (4) abrir `torno-eixo.casillas-job` e conferir que o programa não mudou; (5) ver o cartão Máquina.
- **T2:** (1) Canal de face na faixa; (2) deixar um campo vazio e ver "Falta ..."; (3) preencher e ver o percurso na prévia; (4) abrir no Code e simular; (5) trocar Fanuc/Siemens e comparar.
- **T3:** (1) Sangrar com Ø final 0 e ver o aviso de peça solta; (2) rotação acima do fuso e ver o aviso; (3) tentar pôr outra operação depois e ver o bloqueio; (4) simular no Code e ver o avanço reduzido no fim.
- **T4:** (1) escolher cada tipo novo; (2) salvar e abrir; (3) abrir um arquivo antigo.
- **T5:** (1) Z0 na face pronta: programa igual; (2) Z0 na face oposta: ver os Z deslocados no código; (3) simular.
- **T6:** (1) criar a Fixação 2; (2) ver o bruto do lado 2 vindo do lado 1; (3) gerar um programa por fixação; (4) salvar, fechar, abrir; (5) abrir `torno-eixo.casillas-job` antigo.
- **T7:** (1) Enviar ao Code do torno; (2) ver bruto e zero do pacote no Code; (3) editar o texto e ver o aviso de programa diferente.

## (j) Riscos e o que não muda

**Não muda:** os 8 G-codes de referência (inclusive `torno-eixo.nc`); `.casillas-job` antigo abre e gera igual; fresa; banco de ferramentas; armário; web.

**Riscos:**
- Sangramento e canal de face são G-code novo e **nunca rodaram em máquina**. Simular e rodar a 1ª vez no ar. Peça solta, fixação e colisão não são verificadas pelo app.
- `.casillas-job` versão 2 (T6) e Z deslocado na face oposta (T5) mexem em dados e G-code: só com ordem do Igor, fatia por fatia.
- Se o Igor escolher a Decisão 8b, o app velho pode perder a fixação 2 ao salvar.
- Programa nº e G5x vazios travam Gerar para trabalho novo; trabalho antigo traz o valor no arquivo e segue igual.
- G58/G59 do Siemens no simulador (`MachineExecutor.cs:630`): se o zero do torno passar a usar G58/G59, o simulador trata como zero 5 e 6. Pendência do `ESTADO.md`, fora destas fatias.

**Não conferido:** o conteúdo dos testes do torno; a causa da faixa rolando em 1280 e do botão 3D; como o Code lê pacote de torno; faixa de Ø do bedame de face; perfil atual cobre a peça inteira?; barras MVQNR; "regra antiga do fuso"; nada foi rodado em máquina.
