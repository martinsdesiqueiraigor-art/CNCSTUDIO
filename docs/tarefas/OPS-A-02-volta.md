# OPS-A-02: Chanfro e acabamento da tela das operações A
Nível M · Base `dev` em `9b304f6` (merge do #203) · Ramo `ajuste-OPS-A-02`

## O que mudou
- **Chanfro (fresa em V), operação nova da fresa.** Parâmetros: ângulo da ponta, Ø da ferramenta (o campo Diâmetro de sempre), largura, profundidade (da ponta, abaixo do topo), lado (por fora ou por dentro) e raio de entrada. Todos nascem vazios (operação Incompleta, não gera, Salvar não trava).
- Geometria (ponta afiada): eixo a `profundidade·tan(ângulo/2) − largura` da aresta; passe único em Z topo − profundidade; reaproveita o gerador do contorno (arco de entrada e saída, concordante). Erros claros: cone não alcança o Ø, profundidade pequena para a largura, ângulo fora de 0–180, lado sobre a linha.
- Os cinco pontos onde o código caía em `_ =>`/`default:` (criar operação, nome, gerar, validar, tela) agora têm caso próprio; os dois `_ =>` viraram erro explícito.
- Arquivo `.casillas-job` **versão 7** (a mesma da OPS-A-01) só quando há Chanfro; sem Chanfro sai como antes.
- Pacote CAM→Code: o T usado só por Chanfro (mesmo ângulo) leva o tipo `fresa-v` e o ângulo digitado; o simulador do Code desenha o cone. T compartilhado com outra operação, ou ângulo vazio: fica genérico, como antes. Banco de ferramentas não foi mexido.
- **C2:** nas listas das propriedades (Entrada, Estratégia, Ciclo e as demais) o rótulo fica em cima, a lista ocupa a largura toda e o texto quebra de linha em vez de cortar.

## Teste existente alterado (autorizado pelo Igor em 19/11)
`CamPaletteTests.Em_breve_aparece_apagado_e_nao_cria_operacao`: tirei `"chanfro"` da lista de "em breve" (ele virou operação). Nenhum outro teste mudou; os 8 G-codes de referência intactos.

## Testes
Novos: `CamChamferTests` (15). Core: 2087 verdes (2072 + 15); build Release sem avisos (Core e App).

## Não testado
- No .exe (tela do Chanfro e as listas novas); computer use não foi feito.
- Nada em máquina (G-code novo). Ponta plana deixa o chanfro menor que o digitado: meça a primeira peça.
- Simulação do Code com o cone: coberta por teste de perfil, não vista na tela.

## Avisos
- App da OPS-A-01 (também lê versão 7) recusa um arquivo com Chanfro ("operação" desconhecida). Aceito pelo Igor.
- O 3D do CAM só mostra a trajetória; a remoção de material com cone aparece no Code.

## Roteiro (.exe)
1. Faixa 2D › Chanfro: nasce Incompleta; digite ângulo 90, largura 1, profundidade 1, raio de entrada 3 e a ferramenta; gerar, ver no 3D e simular no Code (cone).
2. Salvar, fechar e abrir: o Chanfro volta igual. Trabalho antigo abre e gera igual.
3. Propriedades de Bolsão (Entrada, Estratégia) e Furação (Ciclo): o texto aparece inteiro.
