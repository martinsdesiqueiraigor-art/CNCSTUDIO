## WEB-01: relatório (Code web: setup de usinagem, ferramentas e simulação)

Ramo `ajuste-WEB-01`, base `main` em `7db0be1` (1.1.0). Executor: Claude (chat), em sprint, por ordem do Igor (18/11). Plano: `claude/plano-WEB-01-code-web-2026-11-18.md` (Projeto).

**Commits:** S1 e S2 (bruto pelo programa e setup do torno) · S3 (ferramentas) · S4 (peças de revolução mais lisas).

**Testes:**
- Aprovados: `dotnet test tests/CncStudio.Core.Tests` em Release, 1957 (5 novos em `Web01SetupTests`). Build Release do App sem erro; web publicada como no CI.
- Conferido a olho num Chrome sem tela (360×760): setup de torno sem setup salvo (sugestão do programa), setup com Ø80 salvo (aviso e botão "Usar o bruto do programa"), ferramenta nova vazia, torno com raio da ponta, simulação do `REBAIXO_CANAL.nc` sem alarme de G00, setup de fresa como antes. A conta foi contornada só na cópia local (nada no repositório).
- Nenhum teste existente alterado; os 8 G-codes do CAM não foram tocados.

### Achado
- A web roda sem tabelas de cultura: texto do Core com `pt-BR` derrubava o app ao abrir o setup. O texto novo usa vírgula à mão. (Outros textos do CAM usam `pt-BR`, mas só no desktop.)
- O alarme "10 rápidos encostaram no material" da foto do Igor não se repetiu com o bruto do comentário nem com as ferramentas dele: a causa provável é um bruto salvo no setup diferente do `BRUTO` do programa. Agora isso aparece escrito.

### S1 Bruto pelo programa
- Setup salvo diferente do comentário `BRUTO`: aviso no rodapé (também ao salvar), na lista de "G00 que encostaram no material" e na tela do setup, com o botão "Usar o bruto do programa".
- Sem setup salvo: a sugestão diz "Bruto sugerido pelo comentário do programa (BRUTO Ø50 × 70, face em Z 0). Confira e salve."

### S2 Setup do torno
- Programa de torno abre o setup do torno: "Barra redonda" ou "Tubo" no eixo Z (sem prisma nem eixo deitado), "Z da face do bruto", fixação "Sem fixação" ou "Placa de três castanhas" (diâmetro e espessura da placa, abertura e largura das castanhas). Fresa igual a antes.

### S3 Ferramentas
- Lista diz o que é: "T1 · Torno · Desbaste externo R0,8", "T2 · Torno · Bedame externo L3", "T3 · Fresa de topo Ø10".
- Ferramenta nova nasce vazia ("Escolha o tipo"); não salva sem o tipo e as medidas que a tela mostra.
- Torno externo e interno: "Raio da ponta (mm)" (vazio = 0,4 na simulação, como antes); a remoção do simulador usa o raio digitado.
- Ferramenta de torno não vira diâmetro (antes o Ø10 escondido podia furar o centro na simulação ao facear até X0).
- "Guardar esta lista como minhas ferramentas ao salvar" (texto mais claro).

### S4 Simulação
- Barra, placa e ferramentas redondas com 72 divisões por volta (antes 48): menos degraus no 3D. Vale também no desktop.
- O painel já abre recolhido no celular em pé (quem o abriu uma vez fica com a última escolha): não mudou.

### Como validar (prévia no celular)
1. Abrir a prévia, Code, `REBAIXO_CANAL` (sem setup salvo): Gráfico → Setup: tela do torno com a sugestão Ø50 × 70.
2. Trocar o diâmetro para 80 e salvar: aviso no rodapé; abrir o Setup de novo: aviso e "Usar o bruto do programa".
3. Ferramentas: "+ Adicionar" nasce vazia; escolher "Torno · Desbaste externo", raio 0,8; a lista mostra "R0,8".
4. 3D: simular até o fim sem alarme de G00 com o bruto do programa.
5. Um programa de fresa: setup como antes.
