# CAM-ADAPT-01c — Volta (desbaste adaptativo: tela)

## O que foi feito
- **Operações ▾ → "Desbaste adaptativo"** (depois de Bolsão). Cria um Bolsão com estratégia Adaptativo e entrada em hélice. Carga ótima, Ø e passo da hélice vêm vazios (●). Pela seleção do STEP, o Bolsão criado também vira adaptativo.
- **Nome na árvore e na linha do tempo:** "Desbaste adaptativo".
- **Aba Estratégia** (seção própria):
  - carga ótima (vazia trava o Gerar; vai até 50% do Ø);
  - sobremetal na parede e no fundo;
  - raio mínimo (preferência);
  - os dois sentidos;
  - contornar ilhas.
  - **Entrada:** só hélice ou pré-furo. Com borda aberta, Ø e passo da hélice não são exigidos (aviso explica que entra por fora).
  - O passe em Z continua nas Alturas, com nota na aba.
- **Passe em Z × comprimento de corte** (conta no Core: `CamJobInfo.AdaptiveDepth`). A ferramenta é achada no banco ativo pelo T.
  - Passe maior que o comprimento: **erro no console**, o código não sai, e Copiar/Enviar travam.
  - Sem o comprimento, ou T fora do banco: **Conferir**.
  - Outras operações não são conferidas.
- O aviso geral do adaptativo ficou mais curto, porque agora a conferência é específica.

## Diferenças do plano
- `CamPalette.OperationItems` deixou de listar variantes (lista `Variants`). Assim o teste antigo "um botão por tipo" segue igual. O Desbaste adaptativo é variante do Bolsão. Só os testes usam esse método.

## Testes
- Novo `AdaptiveScreenTests` (5):
  - o item da paleta cria o adaptativo com carga em falta;
  - passe maior que o corte → erro;
  - passe igual → limpo;
  - sem comprimento, T fora do banco ou sem banco → Conferir;
  - bolsão comum e operação desligada não são conferidos.
- **Nenhum teste existente alterado.** O arquivo segue na versão 10.
- Core completo: **2182 verdes**, 8 G-codes intactos. A solução compila sem avisos.
- Tela conferida em 1280 × 720 (Xvfb):
  - aba Estratégia com a seção;
  - carga vazia → ● na aba, erro "Falta Carga ótima" e operação vermelha;
  - Conferir do comprimento de corte com T fora do banco.

## O que o Igor confere no .exe
1. Operações ▾ → Desbaste adaptativo num bolsão do BOLTED_FLANGE: digitar carga, hélice e passe; gerar; simular.
2. Pôr comprimento de corte na fresa do banco menor que o passe em Z: o código não sai, com erro no console.
3. Borda aberta (face de fora): entra por fora, sem pedir hélice.
