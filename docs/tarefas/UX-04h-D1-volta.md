# Volta UX-04h (D1, torno no banco ativo), 14/11/2026: PARADA antes de codar

Estado do conteúdo técnico: rascunho (só o Igor marca revisado).
Base: `origin/dev` em `3b98343` (#174, com o D5 do #175 dentro). Ramo `fase-ux04h-d1-torno-banco-ativo`.
Nenhum código de produto neste ramo. Nada foi rodado nem testado em máquina: é leitura de código.

## 1. Resposta da conferência pedida na ficha (seção 2)

**O banco hoje não aceita duas ferramentas com o mesmo T, e não aceita ferramenta sem T.** A ficha manda parar aqui.

- **Mesmo T, duas ferramentas: não.**
  - `ToolBank.Validate` (`src/CncStudio.Core/Cam/ToolBank.cs:215-221`) devolve "T… repetido no banco.".
  - `ToolBankJson.Read` chama o `Validate` (`ToolBankJson.cs:115`): um arquivo com T repetido é **recusado inteiro** na leitura.
    Se a migração gravasse dois T iguais, o banco de torno não abriria mais.
  - `ToolBank.With` (`:150`) substitui a de mesmo T; `CanAdd` (`:197`) recusa com "T… já existe neste banco." (é o que a tela
    Ferramentas usa, `ToolBankView.cs:430`).
  - Tudo o que lê o banco procura por T: Ø da análise e do simulador, desenho do torno, folha de setup (`ToolBankLookup`),
    a escolha e o aviso de conflito (`ToolBankPick`). O G-code só traz o T.
  - A migração do B1 já seguia essa regra: T repetido na tabela, "ficou a última linha" (`ToolBankImport.cs:181`).
- **Ferramenta sem T: não.** `BankTool(int Number)` (`:80`): o T é o único campo obrigatório; o arquivo exige `"number"`
  inteiro (`ToolBankJson.cs:142`); a faixa do torno é 1 a 99 (`ToolError`, `:237`). Não existe "sem T" (0 é recusado).

## 2. O que isso trava

A regra nova da ficha: "T já no banco, com nome ou tipo diferente: é outra ferramenta. **Entra também**, a conferir, com aviso
de posição T repetida". Do jeito que o banco é, essa linha **não tem como entrar com o mesmo T**. As outras duas regras
(T livre entra "a conferir"; mesmo T, mesmo nome e mesmo tipo não entra de novo) cabem no banco de hoje.

## 3. Decisão do Igor (não escolhi nenhuma)

Para a ferramenta diferente que chega com um T já ocupado no banco de torno:

a) **Entra no próximo T livre**, "a conferir", com aviso no painel "Avisos da migração": "veio da tabela antiga como T5, que
   já tinha outra ferramenta; entrou como T12, conferir a posição". Não muda o formato do banco. `NextFreeNumber` já existe
   (é o que o "Duplicar" usa). Custo: o T mostrado não é o que estava na tabela, e um programa antigo que chama T5 continua
   pegando a ferramenta que já estava no banco (igual a hoje). Banco cheio (99 T): a linha não entra e sai um aviso que diz
   isso com todas as letras; a tabela antiga continua guardada (o D1 não apaga), então a ferramenta não se perde.
b) **O banco passa a aceitar T repetido.** Muda a regra do banco, a leitura do arquivo, a tela Ferramentas e todos os que
   procuram por T (qual das duas vale no Ø, no desenho, na folha de setup e na escolha?). É outro PR, maior, e mexe em
   coisa que a ficha manda não mexer. Um banco gravado assim não abre em versão anterior do Casillas Code.
c) **O banco passa a aceitar ferramenta sem T** (já é o começo do "armário"): a ferramenta diferente entra sem posição.
   Também muda o formato e a tela; é a direção dos três níveis, que a ficha diz para não fazer agora.
d) **Não entra agora**: fica só na tabela antiga (que o D1 não apaga) e aparece no painel de avisos como "não entrou: T5 já
   tem outra ferramenta". Nada some do disco, mas ela não aparece no banco nem na escolha do Trabalho do torno até o armário.

Observação, sem recomendar contra a direção do Igor: a (a) é a única que cabe no D1 sem mudar o formato do banco e deixa
a ferramenta visível no banco. As opções (b) e (c) pedem ficha própria.

## 4. Feito e não feito

- Feito: `git fetch`; pasta `C:\Projetos\CncStudio-ux04h-d1` e ramo a partir do `dev`; leitura do `CLAUDE.md`, do bloco
  "Estado atual" e do item D1 do plano; a conferência acima.
- Não feito: nenhum item da seção 3 da ficha (config, Code, Trabalho do torno, Enviar ao Code, migração); teste mestre não
  foi rodado; nenhuma foto; `docs/ESTADO.md` não foi alterado; sem PR.
- Com a resposta, o D1 segue como está na ficha. O restante dela não depende desta decisão.
