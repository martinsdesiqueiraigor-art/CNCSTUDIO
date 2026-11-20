# UI-PRO-P1a: parâmetros da operação em abas (fresa)
Nível G · Base `dev` em `070a158` (merge do #204) · Ramo `ajuste-UI-PRO-P1a`

## O que mudou (só `src/CncStudio.App`)
- **Abas no painel da operação (fresa):** Ferramenta, Corte, Alturas e Estratégia, como na maquete aprovada. Título e Nome ficam acima das abas. Só aparecem as abas que têm campo: Texto no cilindro não tem Alturas.
- **Mesmos campos, outro lugar:** Ferramenta = banco, nome, T, Ø. Corte = rotação (com o aviso de fuso) e avanços. Alturas = Z seguro, aproximação, topo, fundo, passe (no Chanfro continuam sem fundo e passe). Estratégia = Geometria do 3D, Desenho, bloco da operação (Contorno, Chanfro, Bolsão, Faceamento, Furação, Texto no cilindro) e 4º eixo e 3+2. Os títulos "Ferramenta", "Condição de corte" e "Alturas (Z)" saíram, porque a aba já diz isso.
- **Marca de Incompleta:** a aba que tem campo vazio ou inválido (que a operação usa agora) ganha "●" laranja, com a dica "falta: …". A marca some ao preencher o campo, sem refazer a tela.
- **Ir ao campo:** o item de pendência da barra (✕ inválido) agora abre a aba certa e põe o cursor no primeiro campo faltando. O aviso de rotação acima do fuso leva ao campo Rotação.
- **Aba lembrada:** ao trocar de operação, fica na mesma aba; se a nova operação não tem a aba, abre a primeira.
- Arquivos: `OpTabs.cs` (novo), `JobView.Props.cs`, `JobView.axaml.cs` (duas chamadas). Peça, Fixação, Zero e Programa não mudam.

## Commits
1. abas + marca de Incompleta (agrupa Ferramenta, Corte, Alturas e Estratégia de uma vez; o plano previa 4 fatias, mas separá-las deixaria campos na coluna antiga sem ganho) · 2. Ir ao campo · 3. ajuste da marca da aba.

## Testes
Core não mudou: 2087 verdes (inclui `CamJobGoldenTests`, os 8 G-codes). Nenhum teste alterado. O App não tem projeto de teste; build Release do App sem avisos.

## Conferido na tela (App aberto no Linux em tela virtual)
- 1280 de largura e escala 150%: abas, campos e listas sem corte para Bolsão, Chanfro, Faceamento, Furação e Texto no cilindro (as 4 abas de cada um).
- Operação Incompleta: marca nas abas Ferramenta, Corte e (Chanfro) Estratégia; clique no item da barra abriu Ferramenta e pôs o cursor em "Ferramenta (T)".
- Não conferido: escala 125%, trabalho antigo salvo aberto, operação com 3D/geometria do STEP, 3+2 com perfil de mesa dupla, Windows de verdade.

## Para o Igor testar no .exe
1. Crie Bolsão, Chanfro, Furação e Texto no cilindro: as abas abrem e todo campo de antes está em alguma aba.
2. Deixe campos vazios: a aba mostra a marca; clique no item vermelho da barra de baixo: a aba abre e o cursor vai ao campo.
3. Abra um trabalho antigo e confira os valores. Gere o G-code como antes.
4. Escala do Windows em 125% e 150%: nada cortado.

## Avisos
- O ● fica no canto de cima da aba; em janelas bem estreitas o rótulo pode ser cortado com "…".
- Torno não mudou (P1b).
