# Casillas Code — regras para agentes externos (GPT, DeepSeek, outros)

Você recebeu uma tarefa em `docs/tarefas/`. Leia esta página e a tarefa. Não leia o resto do repositório além do necessário.

## Como entregar
1. Crie o ramo `ext-<seu-nome>-<tarefa>` a partir do ramo indicado na tarefa.
2. Mexa **só** nos arquivos e pastas listados na tarefa.
3. Commits pequenos, em português.
4. Abra um Pull Request descrevendo o que fez e o que não conseguiu. **Não faça merge.**
5. O dono e o Claude revisam e rodam os testes antes de aceitar.

## Regras
- Textos em português do Brasil, com acentos.
- Não mexer em: `src/CncStudio.Core` (a menos que a tarefa diga), `.github/`, `supabase/`, `admin/`, `installer/`, termos de uso, página de vendas, licença ou ativação.
- Não adicionar pacotes ou dependências.
- Não colocar senhas, chaves ou dados de clientes em nenhum arquivo.
- Programas CNC de exemplo: comentário de cabeçalho com máquina, comando, bruto e ferramentas; zero da peça explicado.
- Se a tarefa estiver ambígua, escreva a dúvida no PR em vez de adivinhar.

## Projeto em uma linha
Editor e simulador CNC (Fanuc e Siemens, fresa e torno) em C#/.NET 10 + Avalonia, desktop e web. Lógica no `src/CncStudio.Core`, com testes em `tests/`.
