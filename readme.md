# Introdução

Os desafios relacionados à concorrência em sistemas distribuídos são, em sua maioria, bastante complexos [1], e existem diferentes níveis de segurança que podemos implementar. A escolha do nível de segurança a ser adotado geralmente depende do que desejamos manter consistente. No caso em que priorizamos a segurança e integridade das informações, especialmente ao lidar com dados inseridos e lidos no banco de dados, optamos por sacrificar um pouco da performance. No entanto, ao escolher o desempenho como prioridade, é crucial tomar decisões com cautela, uma vez que não queremos comprometer a integridade dos dados. Isso é particularmente relevante, pois os dados constituem a parte mais importante de uma empresa, e as decisões estratégicas muitas vezes dependem da sua precisão e confiabilidade.

# Integridade dos dados

Há diversas abordagens para assegurar a integridade dos dados, cada uma apresentando benefícios e desafios que demandam superação ou, em alguns casos, aceitação devido às limitações da solução. Neste documento, exploraremos algumas dessas estratégias.

## Transações do banco de dados

Um artefato altamente eficaz desenvolvido para lidar não apenas com a concorrência, mas também com vários outros aspectos (que não serão abordados aqui), são os níveis de transação do banco de dados [2]. Cada nível é projetado para resolver potenciais problemas específicos em uma aplicação, e a decisão de aceitar tais problemas em prol do desempenho varia de acordo com as necessidades de cada aplicação.

É crucial destacar que diferentes bancos de dados podem denominar o mesmo conceito de maneiras distintas. Portanto, é recomendável analisar como o banco de dados escolhido por você se comporta em cada nível de isolamento.

### Read Uncommited 

O nível de isolamento Read Uncommitted é o que apresenta o menor controle de concorrência, e, em minha opinião, pode ser chamado mais precisamente de 'sem isolamento'. Nesse caso, uma transação pode afetar outra sem garantias de durabilidade dos dados. Isso ocorre porque esse nível permite que um dado alterado dentro de uma transação, mas ainda não persistido no banco de dados, seja visível para outra transação.

Um exemplo simples ilustra essa situação: suponhamos que Alice esteja pagando um valor de 300 reais para John, enquanto, ao mesmo tempo, John está tentando pagar um boleto de 500 reais. Na transação de Alice, o valor é atualizado para 500 reais, mas a transação ainda não foi concluída. Entretanto, a transação de John, executada como Read Uncommitted, consegue ler o valor alterado por Alice, mesmo que ainda não confirmado, e acaba pagando o boleto com o valor já modificado, confirmando assim a transação. Se a transação de Alice, por algum motivo, for cancelada, os dados no banco de dados perdem a integridade.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/732a0bca-ec0e-4096-8562-deb8ad0ae24f)

Por outro lado, o uso do nível de isolamento Read Uncommitted é capaz de proporcionar um maior desempenho em comparação com outras transações, uma vez que não há a necessidade de se preocupar com locks, aumentando assim a concorrência entre transações de uma maneira um tanto quanto inconsequente, sendo a responsabilidade de evitar concorrências entre transações passada exclusivamente ao desenvolvedor.

### Quando Usar Read Uncommited?
Entretanto, pode surgir a pergunta: 'Por que esse nível de isolamento existe?' A resposta é simples: existem situações em que estamos em ambientes controlados e não precisamos de toda a segurança. Por exemplo, ao tirar um relatório de transações de cartão de crédito realizadas no mês passado e temos a **certeza** de que não ocorrerão novas transações nesse período, seria possível utilizar esse nível de isolamento.

Vale ressaltar que a palavra 'certeza' está em negrito, pois tudo depende de como a arquitetura da aplicação foi desenhada. Por exemplo, se estivermos utilizando event sourcing para salvar as transações no banco de dados, e esses dados forem inseridos sequencialmente por meio de uma fila (RabbitMQ, Kafka, etc.), ainda há a possibilidade de, mesmo que a transação tenha sido efetuada no mês passado, ela ainda não tenha sido inserida no banco de dados devido a um lag na fila de mensagens. Nesse caso, há a possibilidade de uma tentativa de inserção de um dado sofrer rollback mesmo após ter sido considerado pela consulta do relatório.

## Read Commited 

Por outro lado, o nível de isolamento Read Committed garante o isolamento entre transações. Isso significa que, se uma transação estiver sendo executada em paralelo com a nossa em questão, nossa transação não terá visibilidade sobre dados que foram alterados por essa outra transação e ainda não foram persistidos (Dirty Read), evitando assim situações como as ocorridas na transação de John no exemplo anterior.
![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/86366fc3-3cef-47c0-a41b-50280dd68636)

Por outro lado, considere o cenário em que Alice está gerando um relatório com o saldo dos clientes, especialmente o saldo das contas com os IDs 1 e 2. Nesse caso, a consistência dos dados no momento em que o relatório é gerado torna-se mais crucial do que a versão mais recente em si. Mesmo que peguemos a última versão dos dados para gerar o relatório, segundos depois esses dados podem estar desatualizados. No entanto, a consistência é vital em todos os momentos em que o relatório é gerado.

Suponhamos que, nesse exato momento, John realize uma transferência da conta 1 para a conta 2:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/da099a82-f4b4-47d7-a8ff-b8abf8954d4c)

Nesse caso específico, ambas as transações rodaram simultaneamente no nível de isolamento Read Committed. No entanto, a transação da Alice demorou um pouco mais para ser executada, resultando em uma execução parcial da consulta antes da transação de John e a segunda parte da consulta após a de John. Antes da execução da query de John, Alice conseguiu buscar a informação da conta 1, que retornou 200 reais. No entanto, quando ela foi buscar a informação da conta 2, a transferência de John já havia sido confirmada. Portanto, o nível de isolamento Read Committed considerará o novo valor da conta 2, que nesse caso seria 100 reais, pois foram transferidos 300 reais da conta 2 para a conta 1.

Esse problema, conhecido como 'Non-repetable reads', pode criar desafios significativos, como explicar para o gerente que 300 reais aparentemente sumiram do nada. Para evitar esse tipo de problema, vamos explorar os próximos níveis de isolamento.

## Snapshot Isolation

O Snapshot Isolation é um nível de isolamento de banco de dados mais robusto em comparação ao Read Committed. Ele não apenas evita Leitura Suja (Dirty Read), mas também impede a leitura de dados que foram confirmados após o início da transação em questão. Nesse nível de isolamento, quando um dado é alterado por uma transação, mas o sistema identifica a presença de uma transação em Snapshot Isolation, o banco de dados mantém ambas as versões do dado [3]: a versão antiga para a transação de Snapshot Isolation e a versão nova para as transações que serão iniciadas posteriormente.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/eed05bcc-6a92-4252-8a98-c145beb3a63d)

Nesse caso, ao utilizarmos o Snapshot Isolation, o banco de dados considera a informação no momento em que a transação foi iniciada. Portanto, mesmo que a informação tenha sido alterada por outra transação, a Alice continua obtendo a versão consistente com o estado dos dados no momento em que sua transação foi iniciada.

### Observações:

É importante observar que esse nível também pode ser referido como Repeatable Read. No entanto, optei por usar o termo Snapshot Isolation para manter a discussão em um nível conceitual, uma vez que cada banco de dados implementa o Repeatable Read de acordo com sua abordagem preferida. Como Martin Klepperman destaca em seu livro: 'Nobody really knows what repeatable read means' [4], em parte devido à ambiguidade inerente aos próprios níveis de isolamento [5].

Por exemplo, no Sql Server, para ativar o Snapshot Isolation da maneira mencionada aqui, é necessário habilitar essa funcionalidade [6].

## Serializable 

É interessante observar como o Snapshot Isolation resolveu o problema do relatório que a Alice precisava. No entanto, agora imagine que Alice e John queiram evitar ficar sem dinheiro e criaram uma regra que é acionada a cada transação realizada por um dos dois para verificar se a soma do valor guardado nas contas de ambos é maior que um determinado montante. Em outras palavras, no exemplo abaixo, somando o dinheiro na conta da Alice com o dinheiro na conta do John, esse valor não pode ser inferior a 200 reais. Ao seguir essa abordagem, Alice e John podem discutir e decidir como proceder antes de efetuar o pagamento da conta em questão.

No entanto, ao executar as transações com o Snapshot Isolation, pode surgir um problema de Write Skew. Isso ocorre porque a atualização ocorre em duas contas diferentes e cada transação do banco de dados vai avaliar conflitos em registros separados, como ambas as transações estão alterando registros diferentes, uma não impede a outra de prosseguir (commit). No exemplo a seguir, Alice deseja pagar uma conta de 400 reais, e ao mesmo tempo, John quer quitar uma conta de 200 reais. O sistema deve evitar que uma das duas transações ocorra, pois, caso contrário, todo o saldo deles seria consumido sem o consentimento de ambos:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/e778cda4-18af-4777-b88a-f5b20783468d)

Há duas possíveis formas de lidar com o Write Skew que eu conheço. Uma delas é travar explicitamente os registros de uma tabela, notificando proativamente que aquele dado sofrerá alteração. Isso pode ser feito no Postgres com o comando 'select for update'. Caso o seu banco de dados não disponha dessa opção, muito provavelmente será necessário utilizar o nível de isolamento transacional como serializable [8], o que pode impactar no desempenho, mas garante a integridade dos dados. Vale observar que mesmo com o 'lock for update', é possível que o Postgres ainda enfrente o problema de Phantom Read quando um novo registro é adicionado [7], mas não irei entrar em detalhes nesse ponto.

## Pessimistic Locking X Optimistic locking

Então para conseguirmos resolver todos os problemas de concorrência e garantir que nosso sistema não vai ferir a integridade dos dados em todas as situações precisamos utilizar as transações sempre em serializable e consequentemente ser penalizado em performance?

Vamos considerar a implementação de uma fila no banco de dados para um sistema que possui cinco instâncias, todas lendo as mensagens dessa fila. Nesse cenário, uma mensagem pode levar um tempo indefinido para ser completamente processada, e como várias instâncias estão tentando processar simultaneamente, é muito provável que enfrentaremos um alto nível de concorrência por registros (mensagens), pois todas as instâncias desejam processar aquele registro na tabela. Nesse contexto, uma opção viável pode ser a utilização de um nível de isolamento mais alto para evitar que a mensagem seja processada mais de uma vez. Além disso, é necessário encontrar uma maneira de alertar o banco de dados para pular registros travados por outra transação. Novamente, no Postgres, você pode alcançar isso com o 'select for update', utilizando o skip locked [9].Um exemplo didático dessa implementação pode ser encontrado neste vídeo. Nesse caso, estamos utilizando o mais alto nível de isolamento, pois acreditamos que o nível de concorrência é muito elevado, e haverá tentativas frequentes de alterações no mesmo registro simultaneamente. Por esse motivo, optamos por uma trava pessimista, antecipando o pior cenário.

Por outro lado, em um cenário real de contas de um banco, é muito provável que tenhamos muitas atualizações ocorrendo ao mesmo tempo, mas em registros diferentes. Dificilmente teremos a mesma pessoa realizando duas transações simultaneamente. Note que usei 'dificilmente' e não 'impossível', pois uma pessoa pode estar fazendo compras em uma loja física enquanto um familiar compra um produto no mercado online, ou até mesmo uma conta sendo automaticamente paga pelo sistema ao mesmo tempo. Problemas de concorrência ainda podem ocorrer, mas tendem a ser menos frequentes. Nesses casos, podemos assumir a responsabilidade de controlar exceções, reduzindo a carga sobre o banco de dados, mas ainda tirando o máximo proveito possível.

Nesse cenário, informamos ao banco de dados que ele só pode atualizar determinada informação se o valor não tiver mudado. Se, por algum motivo, o dado foi alterado, solicitamos ao banco de dados que nos avise. Se ele nos notificar, abortamos nossa transação e iniciamos o processo desde o início até conseguirmos atualizar o registro. Aqui, podemos utilizar um nível de isolamento mais baixo, como por exemplo o Read Committed [10], Assim, conseguimos reduzir a quantidade de travas necessárias no banco de dados, diminuindo, consequentemente, a responsabilidade dele de controlar a concorrência, assim como a carga imposta sobre ele.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/b0dd5941-c482-4475-ac46-0bd26b414e77)

No exemplo acima, enquanto a Alice estava realizando uma compra, o sistema automaticamente pagou uma conta. Ambas as transações ocorreram concorrentemente e, mesmo que a transação da Alice tenha começado antes, ela foi concluída depois. Quando o registro estava sendo atualizado para deduzir o saldo, a segunda parte da verificação do update acabou 'falhando'. Isso ocorreu porque o saldo da Alice, que inicialmente era de 200 reais, agora era de 0 reais, devido à transação do sistema que persistiu a informação antes. Como o isolamento da transação da Alice é Read Committed, ela acaba visualizando a informação que foi confirmada quando tenta realizar a atualização. Assumindo a responsabilidade pelo controle da concorrência, verificamos que nenhum registro foi alterado, abortamos a transação (tirando proveito da atomicidade (A)CID) e reiniciamos o processo desde o início. Alguns bancos de dados oferecem uma funcionalidade específica para essas situações, chamada de compare and substitute ou compare and swap [11]. O ponto crucial a ser observado nesse caso é que estamos adotando uma abordagem otimista e confiando que teremos poucos conflitos. Caso contrário, estaríamos sujeitos a abortar e reiniciar nossa transação várias vezes, desperdiçando recursos valiosos do sistema e do banco de dados. Se optarmos por utilizar o lock otimista quando prevemos uma alta concorrência, existe a possibilidade de o sistema perder desempenho em comparação com o uso do lock pessimista.

Ainda considerando o lock otimista, poderíamos tentar reduzir ainda mais o nível de isolamento para Read Uncommitted, por exemplo. No entanto, poderíamos acabar gerando inconsistências no banco de dados, como exemplificado abaixo:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/5a7c3589-65f3-4805-b706-72b8d800313e)

Existem outras formas de implementar um lock otimista, especialmente quando desejamos garantir que o dado que estamos prestes a atualizar não sofreu qualquer alteração. O lock otimista e sua verificação baseiam-se geralmente em uma propriedade do registro que contém a versão do mesmo. Cada vez que o registro é alterado, sua versão é atualizada, proporcionando assim uma garantia completa da integridade dos dados diante de qualquer atualização no banco de dados.

![image](https://github.com/gumberss/Concurrency/assets/38296002/9a55be67-c739-454c-a032-db8cb860d483)


# Particionamento por Load Balancer

Outra abordagem para resolver o problema de concorrência é designar apenas uma instância do serviço para ser responsável por atualizar determinados dados. Neste exemplo específico, poderíamos utilizar o load balancer como particionador da requisição. Em outras palavras, configurar o Nginx para aplicar uma função hash à conta que será atualizada e, com base nesse resultado, determinar a instância responsável por efetuar essa atualização [12].

É importante mencionar que, mesmo ao utilizar o particionamento por load balancer, cada instância ainda pode ter múltiplos threads e gerar concorrência no banco de dados, o que deve ser tratado separadamente. Um exemplo prático desse tipo de implementação foi utilizado nesse repositório e será explicado posteriormente.

# Log Based Message Brokers

Outra abordagem para lidar com problemas de concorrência é utilizar um sistema de mensageria baseado em logs, como o Kafka. Esse tipo de sistema permite ordenar as mensagens por uma chave. No caso do Kafka, é possível adicionar uma chave para ser usada como base para o particionamento das mensagens. No exemplo da rinha, poderíamos criar duas partições no Kafka e utilizar o ID da conta como base para o particionamento. Cada instância dos nossos serviços poderia então consumir eventos de uma partição específica. Dessa forma, apenas uma instância seria responsável por inserir transações para uma determinada conta, resolvendo assim o problema de concorrência entre instâncias.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/fcab9947-dbcf-4d32-8328-81caa4a8cda4)

1. Gatling envia a transação para o Nginx;
2. Nginx seleciona a instância que não é responsável pelo gerenciamento da conta;
3. A instância 2 que não é responsável pela conta publica no Kafka uma mensagem com o número da conta como chave de particionamento;
4. O Kafka coloca na partição que a Instância 1 é responsável e a Instância 1 recebe esse evento;
5. A Instância 1 corretamente atualiza o banco de dados com a informação.

Para que a instância 2, que recebeu o request, possa fornecer a resposta HTTP correta, ela pode enviar junto com o evento um tópico de resposta, e a instância 1 pode publicar uma nova mensagem colocando a resposta. Outra abordagem seria disponibilizar um endpoint HTTP dedicado para retornar a resposta. Em resumo, a implementação pode variar de acordo com a criatividade de cada um, considerando os trade-offs e os testes de carga subsequentes. O gerenciamento da resposta pode ser implementado em memória, como exemplificado no código deste repositório.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/f3018d6c-e585-472b-ad36-bc7b8c5a0cf0)

1. Instância 1 publica a resposta no tópico de resposta do evento que recebeu;
2. O Kafka encaminha a resposta para a instância 2;
3. A Instância 2 gerencia a resposta e devolve para o Nginx (pode ser implementado como foi nesse repositório);
4. O Nginx devolve a resposta para o Gatling.

# Api Sharding 

Essa foi a solução adotada no repositório em questão. Devido às limitações de memória e processamento, a introdução eficiente de um Message Broker robusto, como o Kafka, que lida com questões como tolerância a falhas, particionamento, monitoramento da saúde dos consumidores e assegura um consumidor sempre disponível através de protocolos de consenso, seria desafiadora.

Diante das limitações mencionadas acima, a solução deste repositório necessitaria de uma revisão substancial para incorporar todos esses elementos, bem como outros aspectos essenciais em sistemas distribuídos. Mesmo assim, essa solução serviu como uma valiosa prova de conceito sobre como enfrentar e resolver problemas de concorrência.

A solução adotada pelo repositório envolve atribuir a cada instância a responsabilidade por determinadas contas do banco de dados. Como existem 2 instâncias e 6 contas, cada uma é responsável por 3 contas específicas. Se uma instância recebe um request que não está sob sua responsabilidade, ela o encaminha automaticamente para a outra instância (etapa número 3 na imagem abaixo). O endpoint HTTP insere os requests em uma fila de processamento e aguarda a resposta na fila de resposta. Um job assíncrono é encarregado de ler as mensagens da fila de processamento, realizar as validações, inserir em lote (batch) no banco de dados e colocar a resposta na fila de resposta. Essa fila é então lida pelo endpoint HTTP, que encaminha a resposta ao solicitante.

![image](https://github.com/gumberss/Concurrency/assets/38296002/6f1247b8-4598-4a7d-94e4-bd1aaf1d92be)

## Resultados dessa solução

Como comentado anteriormente essa foi a solução usada nesse repositorio e abaixo está o resultado obtido

![image](https://github.com/user-attachments/assets/b2e94fa3-4ff5-4154-8565-61336685b5fa)

# Outras formas de resolver

Há inúmeras outras formas de resolver problemas de concorrência, e é extremamente interessante utilizar desafios como o da rinha para aprimorar técnicas. Claro que, ao levar uma solução para produção, há muitos outros aspectos a serem considerados além da resolução de problemas de desempenho e concorrência. No entanto, durante o desafio, nada impede que você treine suas habilidades e a capacidade de resolver problemas, adicionando novas ferramentas em seu arsenal.

# Referências 

[1] https://www.geeksforgeeks.org/concurrency-problems-in-dbms-transactions

[2] https://www.geeksforgeeks.org/transaction-isolation-levels-dbms/

[3] https://www.geeksforgeeks.org/what-is-snapshot-isolation/

[4] Kleppmann, Martin. Designing Data-Intensive Applications: The Big Ideas Behind Reliable, Scalable, and Maintainable Systems (p. 380). O'Reilly Media. Kindle Edition. 

[5] https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/tr-95-51.pdf

[6] https://learn.microsoft.com/en-us/troubleshoot/sql/analysis-services/enable-snapshot-transaction-isolation-level

[7] https://jimgray.azurewebsites.net/papers/On%20the%20Notions%20of%20Consistency%20and%20Predicate%20Locks%20in%20a%20Database%20System%20CACM.pdf?from=https://research.microsoft.com/en-
us/um/people/gray/papers/On%20the%20Notions%20of%20Consistency%20and%20Predicate%20Locks%20in%20a%20Database%20System%20CACM.pdf&type=path

[8] https://www.geeksforgeeks.org/snapshot-isolation-vs-serializable/

[9] https://www.postgresql.org/docs/current/sql-select.html#:~:text=With%20SKIP%20LOCKED%20%2C%20any%20selected,accessing%20a%20queue%2Dlike%20table.

[10] https://learn.microsoft.com/en-us/sql/odbc/reference/develop-app/optimistic-concurrency?view=sql-server-ver16

[11] https://docs.datomic.com/pro/transactions/transaction-functions.html

[12] https://www.nginx.com/resources/wiki/modules/consistent_hash/
