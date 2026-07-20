# Centro de Treinamento Pokémon "Alto Nível"

API REST para gestão de matrículas de Pokémon em planos de treinamento mensais. Desafio técnico para vaga de desenvolvedor(a) júnior.

## Stack

- Backend: .NET 10 com Minimal APIs, EF Core (abordagem database-first)
- Banco: SQL Server 2022 via Docker
- Frontend: Angular

## Como executar (backend + banco)

1. Subir o SQL Server:
   ```
   docker-compose up -d
   ```
2. Criar o schema (usuário `sa`, senha `PokemonCenter@2026`, porta `1433`) rodando o `sqlcmd` de dentro do próprio container, sem precisar ter nada instalado na máquina:
   ```
   docker cp Database/schema.sql pokemoncenter-sqlserver:/schema.sql
   docker exec -it pokemoncenter-sqlserver /opt/mssql-tools18/bin/sqlcmd \
     -S localhost -U sa -P "PokemonCenter@2026" -C \
     -i /schema.sql
   ```
   (ou rode o conteúdo de `Database/schema.sql` em qualquer client SQL Server, tipo Azure Data Studio.)
3. Rodar a API:
   ```
   cd BackEnd/PokemonCenter
   dotnet run
   ```
   A connection string já está configurada em `appsettings.json` apontando pro banco do passo 1.

## Como executar (frontend)

1. Com o backend rodando (passo anterior), instale as dependências:
   ```
   cd FrontEnd/CentroDeTreinamentoPokemon
   npm install
   ```
2. Suba o servidor de desenvolvimento:
   ```
   npm start
   ```
   (equivalente a `ng serve`)
3. Acesse `http://localhost:4200`. A URL da API já está configurada em `src/app/config/api.config.ts` apontando pra `http://localhost:5289/api`, e o backend já libera CORS pra essa porta — se o front rodar em outra porta, ajuste o `Program.cs` do backend.

## Decisões técnicas e premissas assumidas

**Tipo do Pokémon como coluna simples (`VARCHAR`), não tabela separada.** O enunciado pede "tipo (ex.: Fogo, Água, Planta...)" no singular, diferente do jogo real onde um Pokémon pode ter até 2 tipos. Optei por manter simples e fiel ao escopo pedido, evitando uma modelagem N:N desnecessária.

**Treinador e Pokémon só têm endpoints de criar e listar/buscar.** Não implementei PUT/DELETE genéricos pra esses dois recursos porque nenhuma regra de negócio, tela mínima do Angular ou critério de avaliação pede edição/exclusão deles — o GET de listagem existe só pra alimentar dropdowns no formulário de nova matrícula. Preferi investir o tempo nas regras R1-R5 e na consulta de MRR, que é onde o desafio diz que a nota está.

**R2 (upgrade) — simulação separada da confirmação.** O enunciado pede que o frontend "exiba o valor da primeira cobrança retornado pela API antes de confirmar". Se um único endpoint já persistisse o upgrade ao calcular o valor, não haveria como o usuário desistir depois de ver o valor. Por isso separei em dois endpoints: `GET /api/matriculas/{id}/upgrade?novoPlanoId=` (simula o cálculo, não grava nada) e `PATCH /api/matriculas/{id}/upgrade` (confirma e persiste). Os dois compartilham a mesma validação e fórmula de cálculo por trás, pra não duplicar a regra de negócio. Fiquei na dúvida entre usar PATCH ou POST nessa rota de persistir, já que por baixo dos panos o banco cancela uma matrícula e cria outra. Mas como isso é um detalhe de implementação interna, optei pelo PATCH: do ponto de vista de quem consome a API, ela só está alterando a matrícula para um plano melhor.

**R2 (upgrade) — ciclo de cobrança fixo em 30 dias, não por mês calendário.** O enunciado descreve o pro-rata em termos de "ciclo de 30 dias" (dia 16 de 30 → R$35 de primeira cobrança), e segui essa mesma premissa: cada ciclo dura exatamente 30 dias a partir da `DataInicio`, independente do mês do calendário, em vez de fixar um "dia de cobrança" (ex.: sempre dia 1) e variar os dias do ciclo conforme o mês tem 28 a 31 dias. Isso simplifica o cálculo (`diasDecorridos % 30`) e reproduz exatamente o exemplo do enunciado, ao custo de a data de cobrança derivar ao longo do tempo em vez de cair sempre no mesmo dia do mês (ex.: matrícula iniciada em 01/jan renovaria em 31/jan, depois por volta de 02/mar, e assim por diante).

**R4 (matrículas canceladas) — dois jeitos de uma matrícula deixar de ser Ativa, com status diferentes.** O enunciado não definia como isso acontece, então decidi: quando o upgrade substitui uma matrícula por outra de plano superior, a matrícula antiga vira `Concluida` (ela não foi cancelada, foi naturalmente encerrada por uma mudança de plano). Quando o cancelamento é uma ação explícita do usuário (`PATCH /api/matriculas/{id}/cancelar`), ela vira `Cancelada`. Ambos os status são excluídos dos cálculos de MRR e dos relatórios de matrículas ativas — só `Ativa` conta.

**R5 (transferência de Pokémon) — endpoint de ação dedicado**, não parte de um PUT genérico do Pokémon (`PATCH /api/pokemons/{id}/transferir`). Ele só atualiza o `treinadorId` do Pokémon; não mexe em cobrança nem no status das matrículas existentes, como pedido no enunciado.

**Entidades geradas via scaffold do EF Core, com um ajuste manual necessário.** As classes em `Models/` vieram de `dotnet ef dbcontext scaffold` em cima do `Database/schema.sql`, seguindo a abordagem database-first do projeto. Só que o scaffold interpretou o relacionamento entre `Pokemon` e `Matricula` como **um-para-um**, porque viu o índice único `UX_Matricula_PokemonAtiva` na coluna `pokemonId` e não considerou que esse índice é filtrado (`WHERE status = 'Ativa'`) — ele garante só uma matrícula ativa por Pokémon, não uma matrícula só na vida inteira. Um Pokémon precisa acumular várias matrículas ao longo do tempo (histórico de `Cancelada`/`Concluida`), então o relacionamento correto é um-para-muitos. Isso só apareceu testando a API de verdade: o fluxo de upgrade quebrava com uma exceção do EF Core ao tentar criar uma segunda matrícula pro mesmo Pokémon. Corrigi manualmente `Pokemon.cs` (navegação `ICollection<Matricula>` em vez de `Matricula?`) e o mapeamento em `PokemonCenterContext.cs` (`WithMany` em vez de `WithOne`) — reexecutar o scaffold sozinho não resolveria, porque a heurística dele não relê o filtro do índice.
## Mensagens de erro padronizadas

Todo erro de regra de negócio (R1, R3, downgrade em R2) e de recurso não encontrado passa por um `GlobalExceptionHandler` central, que converte exceções em respostas JSON consistentes: `RegraDeNegocioException` → 400, `RecursoNaoEncontradoException` → 404, qualquer outra coisa → 500 com mensagem genérica (pra não vazar detalhe interno pro cliente). O formato de resposta é sempre `{ "mensagem": "..." }`, pronto pro frontend exibir direto.

## Organização dos endpoints

Cada recurso (Treinador, Pokémon, Matrícula) tem seu próprio arquivo em `Endpoints/`, usando `MapGroup` pra agrupar as rotas (`/api/treinadores`, `/api/pokemons`, `/api/matriculas`). O `Program.cs` só compõe esses grupos — não tem lógica de negócio nem validação nele.

## O que eu faria diferente ou melhoraria com mais tempo

- **Tirar strings e números mágicos do código:** o nome do plano `"Elite dos 4"` e o nível mínimo `50` pra validar se o Pokémon é apto a esse plano aparecem soltos em mais de um lugar do `MatriculaService`; extrairia os dois pra constantes. Não fiz isso agora porque o código ainda é pequeno (só duas ocorrências) e criar uma constante pra cada um pareceu mais overengineering do que ganho real nesse tamanho de projeto.
- Implementar o CRUD completo das outras entidades (Treinador e Pokémon).
- Implementar a relação N:N entre Pokémon e Tipos, gerando a tabela de tipos com todos os tipos de Pokémon existentes.
- Hoje a transferência de Pokémon (R5) só atualiza o `treinadorId` no registro do Pokémon, sem guardar quem era o treinador antes da troca. Isso significa que, depois de um Pokémon trocar de treinador, não dá mais pra saber com certeza qual treinador estava associado a ele quando uma matrícula antiga foi criada — a matrícula guarda o `pokemonId`, mas o treinador daquele Pokémon na época pode não ser mais o mesmo de hoje. Com mais tempo, eu criaria uma tabela de histórico (algo como `HistoricoTreinadorPokemon`, com `treinadorId`, `pokemonId`, `dataInicio` e `dataFim`) pra registrar cada vínculo Pokémon-treinador ao longo do tempo, permitindo reconstruir quem era o treinador ativo de um Pokémon em qualquer data — inclusive na data de uma matrícula específica.
  - Essa lacuna afeta principalmente relatórios de **gastos por treinador**. Hoje, gasto por matrícula e gasto por Pokémon saem certos, porque `Matricula.pokemonId` é uma FK fixa que nunca muda. Mas gasto por treinador só é calculado via `Matricula → Pokemon → Treinador`, usando o `treinadorId` **atual** do Pokémon — então, se um Pokémon já foi transferido, todo o gasto das matrículas antigas dele (de antes da troca) acaba sendo somado no treinador novo, e não no treinador que era dono na época. Com a tabela de histórico, o relatório passaria a cruzar o período de cada matrícula (`dataInicio`/`dataFim`) com o período de posse do Pokémon, atribuindo o gasto ao treinador correto mesmo depois de trocas de dono.
- Implementaria um esquema de paginação no retorno das matrículas.

## Como usei IA

Usei o Claude Code (Anthropic) durante o desenvolvimento: code reviews, bug hunting, ajuda com sintaxe, padronização das mensagens de erro e ajuda na escrita da documentação. Principais contribuições:

- **Configuração do Docker com SQL Server:** eu tinha mais familiaridade com Postgres, então pedi ajuda com a sintaxe e as variáveis de ambiente corretas pro `docker-compose.yml` do SQL Server (usuário `sa`, `ACCEPT_EULA`, etc.).
- **Padronização das mensagens de erro:** revisei com a IA como centralizar as respostas de erro num `GlobalExceptionHandler` único, em vez de tratar exceção por exceção em cada endpoint, garantindo um formato JSON consistente pro frontend. Minha experiência até então era usar try/catch em cada rota.
- **Modularização dos endpoints:** pedi ajuda a modularizar os endpoints em arquivos separados para o Program.cs não ficar muito grande. Antes eu geralmente escrevia os endpoints direto no Program.cs mesmo.
- **Testes de ponta a ponta:** a IA subiu a API de verdade contra o SQL Server local e testou os fluxos reais via `curl` (criar treinador/Pokémon/matrícula, simular e confirmar upgrade, cancelar, re-matricular um Pokémon já cancelado) em vez de só verificar se o código compilava. Isso pegou dois bugs que só se manifestavam em runtime: o relacionamento `Pokemon`↔`Matricula` mal inferido pelo scaffold (um-para-um em vez de um-para-muitos, descrito acima) e um `Include` faltando no endpoint de cancelamento que causava erro 500 mesmo com a operação já persistida no banco.
- **RxJS e Signals no frontend:** essa foi a parte que mais me trouxe dificuldade — misturar Observables do RxJS (as chamadas HTTP dos services) com Signals do Angular pra estado local dos componentes. O Claude Code me ajudou bastante a entender o padrão de converter `valueChanges` de `FormControl` em signal via `toSignal()` e depois derivar estado com `computed()` (por exemplo, recalcular os Pokémon disponíveis no formulário de matrícula toda vez que o treinador selecionado muda, ou re-simular o upgrade quando o plano novo é trocado), sem cair em assinatura duplicada ou em signal desatualizado.
- **CSS e estrutura dos templates HTML:** pedi pro Claude Code gerar o CSS e a estrutura HTML dos componentes (tabela de matrículas, formulários, layout geral) — como o próprio enunciado do desafio diz que beleza visual não é critério de avaliação, preferi usar esse tempo focando nas regras de negócio (R1-R5) e no tratamento de erro, e deixar a parte visual por conta da IA.
