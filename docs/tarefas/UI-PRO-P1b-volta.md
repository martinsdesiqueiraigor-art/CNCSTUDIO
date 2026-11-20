# UI-PRO-P1b: parâmetros da operação em abas (torno)
Nível M · Base `dev` em `f402980` (merge do #205) · Ramo `ajuste-UI-PRO-P1b`

## O que mudou (só `src/CncStudio.App`)
- **Abas no painel da operação do torno:** Ferramenta, Corte, Segurança e Estratégia (as 4 em todas as operações), com o mesmo controle da fresa (`OpTabs.cs`). Título e Nome ficam acima das abas.
- **Divisão dos campos:** Ferramenta = banco, aviso de conflito, T, nome. Corte = G96, rotação máxima, rotação fixa, aviso de fuso e os avanços da operação (Torneamento: desbaste e acabamento; Faceamento, Furação e Canal: Avanço; Rosca: Rotação (fixa); Canal de face e Sangramento: Avanço, Avanço reduzido e Ø onde começa o avanço reduzido). Segurança = Z seguro, Ø seguro (troca), folga de aproximação. Estratégia = o resto de cada operação, com os avisos do Sangramento e do Canal de face no topo. Os títulos "Ferramenta", "Condição de corte" e "Segurança" saíram (a aba já diz isso).
- **Marca de Incompleta:** a aba com campo vazio ganha "●" (dica "falta: …"); some ao preencher.
- **Ir ao campo:** o item vermelho da barra abre a aba do primeiro campo faltando e põe o cursor nele; o aviso de rotação acima do fuso leva a "Rotação máxima".
- **Aba lembrada** ao trocar de operação.
- **Correção encontrada no caminho (pré-existente):** com bruto incompleto ou problema de zero, a lista de atalhos da barra do torno ficava mais curta que a de textos do Core e nenhum item levava a lugar nenhum. Agora o bruto leva ao cartão do Bruto e o zero ao cartão do Zero, na Preparação, e os itens das operações voltam a funcionar.
- **Ajuste no controle da P1a:** a aba aberta não usa mais negrito (o negrito cortava "Ferramenta" com "…" nas abas do torno); a aba aberta se destaca pela linha de baixo e pela cor.
- Preparação (Peça, Bruto, Fixação, Zero, Programa) e a fresa: sem mudança de comportamento.

## Testes
Core não mudou: 2087 verdes (inclui os 8 G-codes). Nenhum teste alterado. Build Release do App sem avisos. O App não tem projeto de teste.

## Conferido na tela (App aberto no Linux em tela virtual, 1280 de largura; escalas 125% e 150% não conferidas por ordem do Igor)
- Faceamento, Torneamento externo, Canal de face e Sangramento com as 4 abas; as outras operações usam o mesmo código. Operação Incompleta com marca nas abas; clique no item vermelho da barra abriu Ferramenta com o cursor em "Ferramenta (T)".
- Aba escolhida lembrada entre operações.
- O trabalho de exemplo `sangramento-fanuc.casillas-job` abriu normalmente.
- **Não conferido:** Rosca, Furação axial, Canal externo/interno e Torneamento interno na tela; trabalho de torno antigo com todos os valores preenchidos; Windows.

## Para o Igor testar no .exe
1. Crie cada operação do torno e veja as abas; todo campo de antes está em alguma aba.
2. Deixe campos vazios: marca nas abas; clique no item vermelho da barra: a aba abre e o cursor vai ao campo.
3. Bruto incompleto ou zero pendente: os itens da barra levam à Preparação.
4. Abra um trabalho de torno antigo e gere o G-code como antes.

## Avisos
- Canal de face tem muitos campos: a aba Estratégia rola (a seção "Bedame de face" continua recolhível).
- Um commit só para abas, marca e Ir ao campo (separar deixaria o torno sem as marcas no meio).
