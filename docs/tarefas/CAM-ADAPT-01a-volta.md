# CAM-ADAPT-01a — Volta (desbaste adaptativo: motor)

## O que foi feito
- Bolsão com estratégia nova `Adaptive` e campos novos em `PocketParams`: `OptimalLoad` (carga ótima = largura de corte máxima), `MinRadius`, `BothWays`, `FloorAllowance`. O sobremetal da parede é o `FinishAllowance` de sempre; os passes em Z são os do Bolsão (iguais).
- Validação com mensagem: carga vazia; carga acima de 50% do Ø; entrada que não seja hélice ou pré-furo; hélice que não cabe ou com Ø menor que a ferramenta; sobremetal de fundo que não deixa nada a cortar.
- Motor novo `Cam/Adaptive.cs`, por nível de Z:
  - **Grade:** grade 2D (célula = 2% do Ø, no máximo 0,25 mm) com:
    - a área do bolsão (parede e ilhas por preenchimento de linhas);
    - onde o centro pode andar (transformada de distância exata);
    - o material do nível.
  - **Entrada:** hélice no ponto mais folgado do material, com uma volta no fundo para tirar o cone; ou pré-furo, com mergulho no furo e espiral para abrir.
  - **Passo:** cada passo testa 131 direções (±130°, de 2 em 2°). A conta é rápida porque cada célula do anel cobre um intervalo contínuo de ângulos. Fica a direção com a largura mais perto de 85% da carga, sem passar dela, preferindo a concordante. Sem saída para a frente, olha para trás.
  - **Ligação:** acabou a região, liga por baixo, pelo que já está limpo, até a borda do material mais perto (nunca entra de frente no material). Sem caminho, sobe reto, anda na altura de avanço e entra de novo em hélice.
  - **Faixa da parede:** a faixa junto da parede (~60% da carga) fica para a volta na parede, no fim de cada nível. A volta é concordante: parede no sentido anti-horário, ilhas no horário. Trecho que ficaria perto de outra parede é deixado de fora, com aviso.
  - **Saída:** os pontos viram retas e arcos (tolerância 0,01 mm).
  - **Rede de segurança:** todo ponto de corte é conferido contra o contorno exato. Se algum chegasse perto demais da parede ou de uma ilha, a operação não gera e dá erro.
- Arquivo: chaves `optimalLoad`, `minRadius`, `bothWays`, `floorAllowance` e a estratégia `Adaptive`, **versão 10 só com o adaptativo**.
- App: só um item a mais ("Adaptativo") na lista de estratégias do Bolsão, para um arquivo v10 não quebrar a tela. A tela própria é a fatia c.

## Comportamento de referência copiado
Fusion 2D Adaptive Clearing: Optimal load, passes em Z iguais, Stock to leave (parede e fundo), entrada em hélice, ligações por baixo (stay down) e volta final.

## Fica para a fatia b (avisado no plano)
- Borda aberta entrando por fora. Hoje o bolsão automático com borda aberta já gera, mas trata o contorno do bruto como parede: corta ar e fica mais lento, sem erro.
- Raio mínimo e os dois sentidos: os campos são gravados, mas ainda não mudam a trajetória.
- Erro de passe em Z maior que o comprimento de corte do banco. Por ora há um aviso fixo para conferir.

## Casos de canto
- A entrada (hélice ou espiral do pré-furo) corta com a ferramenta cheia, como em qualquer CAM. A carga vale depois da entrada.
- Pré-furo com mais de uma região: pede também a hélice, com mensagem.
- Cantos que nenhuma fresa alcança ficam com material, como no Bolsão de anéis.

## Testes
- Novos em `AdaptiveTests` (17), com uma simulação 2D própria do teste, independente do motor (grade de 0,05 mm). Formas: retângulo, círculo, bolsão com ilha e L.
  - A largura de corte nunca passa da carga + 10% (+ 0,1 mm da grade).
  - A trajetória nunca chega a menos de raio + sobremetal da parede ou da ilha.
  - Depois do último nível, sobram no máximo 2 mm² fora do sobremetal, só onde a fresa alcança.
  - Passes iguais e fundo com o sobremetal.
  - Mensagens de erro.
  - Pré-furo.
  - G-code com arcos e tempo de cálculo: bolsão 120 × 80, Ø10, carga 1, um nível, em ≈0,35 s no teste (≈0,6 s com a partida do programa).
  - Arquivo v10, com ida e volta.
- **Testes existentes alterados (autorizados na ficha, Igor 21/11):** só a "versão futura" de 10 para 11, porque a 10 passou a existir: `CamJobTests`, `CamJobProgramTests`, `CamJobZeroTests` e `CamJobStockShapeTests` (também a mensagem "versão mais nova (11)").
- Core: **2173 verdes**, 8 G-codes intactos. App compila sem avisos.

## O que o Igor confere no .exe
Nada ainda: sem tela própria (fatia c). Quem quiser pode escolher "Adaptativo" na Estratégia de um Bolsão, mas os campos novos (carga etc.) ainda não aparecem na tela.

## Riscos
- Nunca rodado em máquina; simulação é verificação, nunca garantia.
- O G-code sai com muitas retas curtas (passos de ~1 mm; poucos arcos). A suavização vem com o raio mínimo na fatia b.
