## UI-B: relatório (interface nova, bloco B — sprint UI-04, UI-05, UI-07)

Ramo `ajuste-UI-B`, saído do `ajuste-UI-A` (PR #197, que contém #195 e #196): **depende de #195, #196 e #197** (mesclar nessa ordem antes). Executor: Claude (chat), pela ficha UI-B (Igor: "pode prosseguir", 19/11).

**Testes:** `dotnet test tests/CncStudio.Core.Tests` em Release, uma vez por fatia: 2015 aprovados nas três; teste mestre (`CamJobGoldenTests`, 8 G-codes) aprovado. Solução inteira em Release sem aviso nem erro. Fotos sem tela em 1280×720 conferidas. **Nenhum teste alterado. Core intocado.**

### O que mudou, por fatia
1. **UI-04 desenho do torno.** Cartão "Bruto e fixação" na Preparação do torno: placa, castanhas (dura, mole ou comuns; placa lisa sem castanhas), bruto, contraponto (marcado ou "Entre pontas") e luneta quando marcados, cotas de Ø, comprimento, preso e balanço e a marca do Z0 (face pronta, face oposta ou do desenho do perfil). Fora de escala (dito no desenho). Medida não informada aparece como "?"; fixação não informada não desenha castanha. Muda junto com os campos.
2. **UI-05 linha do tempo.** Embaixo da vista, na aba Operações (fresa e torno): uma faixa por operação ligada, largura pelo tempo estimado, cor do estado (pronta, incompleta, erro), a escolhida com borda azul; clicar seleciona a operação. A árvore mostra o tempo de cada operação ("≈ 1 min 05 s estimado", "sem tempo", "desligada"). O tempo vem do G-code gerado, passado pelo mesmo analisador do Code (Core sem mudança), somando os movimentos entre o comentário de uma operação e o da seguinte; corte sem avanço deixa a operação "sem tempo". Conferido: o total do `fresa-fixacao` deu 4 min 03 s, o mesmo do Code.
3. **UI-07 Code.** Destaque do Fluent em laranja (botões ligados, caixas marcadas, controle deslizante), no tema escuro e no claro, e fundo do editor no grafite da identidade (antes preto). O tema CMYK continua igual.

### Decisões do Claude
- **Maciço/tubo:** não entrou (precisa de campo novo no `.casillas-job` do torno; é dado, vai em ficha própria se o Igor quiser).
- **Barra de módulos mais fina e painéis soltos:** não entraram.
- **Tempo estimado:** sem pausas (G4) nem troca de ferramenta, como diz a dica da linha do tempo; rápido pela velocidade de rápido padrão do analisador. É estimativa, sempre escrita assim.
- O destaque laranja vale para o app inteiro (Code, CAM e demais telas): mesma regra de cor da identidade.
- Fora da lista da ficha: uma linha em `MainWindow.Modules.cs` (o fundo do editor volta ao tema ao desligar o CMYK, em vez de voltar ao preto do Fluent).

### Teste do Igor (teste automático do PR)
1. Torno, aba Preparação: o cartão "Bruto e fixação" mostra o desenho. Trocar a fixação para castanha mole, marcar "Entre pontas", digitar preso e balanço, trocar o Z0: o desenho acompanha na hora.
2. Fresa e torno, aba Operações, com um trabalho com várias operações: a linha do tempo aparece embaixo da vista; clicar numa faixa seleciona a operação; a árvore mostra o tempo de cada uma. Desligar uma operação: ela sai da linha do tempo.
3. Code: abrir um programa; botões ligados e marcados em laranja; editor em grafite. Trocar para o tema claro e para o CMYK e voltar: nada fica ilegível.
4. Um trabalho antigo abre, salva e gera o mesmo G-code de antes.
