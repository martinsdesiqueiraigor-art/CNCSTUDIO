# Plano da Etapa 2 — Produto final

Proposta para aprovação. Parte do protótipo validado (Etapa 1) e incorpora os pedidos feitos
durante os testes: torno, vista isométrica, recursos de edição e melhorias de interface.

Cada fase termina com `dotnet test` passando no CI, executável gerado e commit em português.
Ao fim de cada fase eu mostro o resultado antes de seguir.

## Decisões já tomadas

- **Stack:** C# / .NET 10 + Avalonia + AvaloniaEdit (validada no protótipo).
- **Dialetos:** Fanuc e Siemens, para fresa e torno.
- **Torno:** Fanuc Série T e Siemens; ciclos G71/G70 e rosca G76 (CYCLE95/CYCLE97 no Siemens).
- **Vista 3D girável com o mouse: fora do escopo.** Vistas fixas (superior, frontal, lateral, isométrica) continuam.

## Já pronto (vindo do protótipo)

- Estrutura da solução, CI com testes e executável Windows.
- Lexer, parser modal, trajetória com arcos, backplot (XY/XZ/YZ/isométrica), simulação 2.5D com 4 vistas.
- Editor: highlighting, procurar/substituir, ir para linha, fonte, renumerar, maiúsculas, faixa de comandos em abas.

## Fases

### Fase 1 — Fresa: comandos do MVP
- G90/G91, G54–G59 (com tabela de zeros editável), G17/G18/G19.
- Furação: G81, G83 (Fanuc) e CYCLE81, CYCLE83 (Siemens).
- Ferramentas: T, M06, M04, tabela de ferramentas (diâmetro por T) usada na simulação.
- Subprogramas: M98/M99 (Fanuc) e L (Siemens); arcos por CR= (Siemens) e R (Fanuc).
- Opção para números sem ponto decimal no Fanuc (`X10` = 10 mm ou 0,010 mm).
- Validador: avisos de avanço zero em G01, rápido dentro do material, etc.

### Fase 2 — Estatísticas
- Tempo estimado, distância de corte e de rápido, tempo por ferramenta.
- Painel de estatísticas na interface.

### Fase 3 — Torno: núcleo
- Tipo de máquina (fresa/torno), com detecção automática e troca manual.
- X em diâmetro, plano G18, G96/G97 (velocidade de corte constante).
- Ciclos G71/G70/G76 (Fanuc) e CYCLE95/CYCLE97 (Siemens), expandidos em movimentos.
- Exemplos EXEMPLO_TORNO.nc e .mpf, com testes.

### Fase 4 — Torno: simulação
- Bruto cilíndrico; remoção de material no perfil.
- Vista 2D em meia seção XZ (como no painel do CNC) e peça 3D por revolução em vista isométrica fixa.

### Fase 5 — Interface da simulação (inspirada no CIMCO)
- Barra de reprodução embaixo, com régua para arrastar a qualquer ponto do programa e "ir para linha".
- Painel de coordenadas: X, Y, Z, avanço, linha e distância.
- Ferramenta desenhada com suporte (porta-ferramenta).
- Trajetória desenhada por cima do material.
- Editor acompanha a linha em execução.

### Fase 6 — Editor e funções NC
- Barra de busca/substituição própria, em português.
- Painel de erros com filtro (erros/avisos) e contagem.
- Funções NC extras: remover/inserir espaços, remover comentários, remover números de bloco.

### Fase 7 — Polimento
- Tema claro/escuro, configurações salvas (fonte, tema, pastas recentes, ferramentas).
- Arquivos recentes, ícone, instalador e executável menor.
- Versão do Avalonia fixada.

## Fora do escopo

4º/5º eixos, CYCLE800, compensação G41/G42 complexa, DNC/serial, macros paramétricas completas,
CAD/CAM e vista 3D girável.
