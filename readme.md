# Introdução

Problemas de concorrência em sistemas distribuidos são em geral muito complexo e há diferentes níveis de segurança que podemos prover, geralmente a decisão entre qual nível de segurança queremos usar deve ser escolhida com base no que precisamos manter consistente. Caso a escolha seja o dado que inserimos e lemos do banco de dados, escolhemos priorizar a segurança da informação e a sua integridade em detrimento da performance, caso escolhemos performance, temos que escolher o nosso caminho com cautela, pois em geral, não vamos querer perder a integridade dos dados, visto que os dados são a parte mais impotante de uma empresa, uma vez que as decisões são tomadas em cima deles.

# Integridade dos dados

Há diferentes formas de garantir a integridade dos dados e cada uma delas tem benefícios e desafios que precisam ser superados ou em alguns casos precisam ser aceitos devido a limitação da solução e nós vamos passar por algumas delas aqui nesse documento.

## Transações do banco de dados

Um excelente artefato construído para lidar não apenas com concorrência mas com varios outros pontos que não vamos abordar aqui, foram os níveis de transação do banco de dados. Cada nível vai conseguir resolver um potencial problema para a sua aplicação e a decisão de aceitar tais problemas em prol de performance por exemplo varia para cada aplicação.

Um ponto importante é que diferentes bancos de dados podem chamar o mesmo conceito de formas diferentes, então sugiro olhar como o banco de dados escolhidopor voce se comporta em cada nível de isolamento.

### Read Uncommited 

Read Uncommited é o nível de transação com o menor controle de concorrência, honestamente na minha opinião é mais fácil chamar esse nível de sem isolamento. Nesse caso uma transação pode impactar a outra sem qualquer garantia de durabilidade do dado, pois esse nível permite que mesmo que um dado alteradod entro de uma transação ainda não persistida no banco de dados seja visível para outra transação, como vamos ver no exemplo bem simples a seguir, onde Alice está pagando um valor de 300 reais para John e ao mesmo tempo John está tentando pagar um boleto de 500 reais. Nesse cenário, a transação da Alice iniciou e atualizou o valor para 500 reais mas ainda não encerrou a transação, porém a transação de John sendo executada como Read Uncommited, consegue ler o valor alterado por Alice mas ainda não confirmado e acaba pagando o boleto com o valor já alterado e confirma a transação. A transação da alice por sua vez cancela a alteração por algum motivo e os dados no banco de dados acabam perdendo a integridade:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/732a0bca-ec0e-4096-8562-deb8ad0ae24f)

Por outro lado, usar Read Uncommited consegue prover maior nível de performance do que as outras transações, uma vez que não precisa ficar se preocupando com locks aumentando a concorrência entre transações de uma maneira um pouco, digamos assim, inconsequente.

### Quando Usar Read Uncommited?
Mas se há essa inconsequência, pode surgir a pergunta "por que esse nível existe?", bom a resposta é simples, há momentos em que estamos em ambientes controlados e não precisamos de toda a segurança. Por exemplo, se vamos tirar um relatório de transações de cartão de credito efetuadas no mês passado e temos a **certeza** de que não vai surgir uma nova transação nesse período, seria possível utilisar esse nível de isolamento. 
Obs: Certeza está em negrito pois tudo depende de como a arquitetura da aplicação foi desenhada. Por exemplo, digamos que no exemplo acima estamos utilizando event sourcing para salvar as transações no banco de dados e esses dados são inseridos sequencialmente através de uma fila (RabbitMq, Kafka, etc), há a possibilidade de mesmo que a transação tenha sido efetuada no mês passado, ela ainda não tenha sido inserida no banco de dados devido a um lag na fila de mensagens. Nesse caso ainda há a possibilidade de tentativa de inserção de um dado e ele sofrer rollback mesmo após ter sido considerado pela consulta do relatório.

## Read Commited 

Read Commited por outro lado garante o isolamento entre transações, ou seja, se uma transação foi iniciada em paralelo com a nossa em questão, nossa transação não vai ver o dado que foi alterado pela outra (Dirty Read), como aconteceu com a transação do John no exemplo anterior.
![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/86366fc3-3cef-47c0-a41b-50280dd68636)

Por outro lado, imagine que Alice está gerando um relatório com o saldo dos clientes dele, em especial o saldo das contas com o id 1 e 2, nesse caso, a parte mais importante é a consistência dos dados no momento em que o relatório foi gerado e não a ultima versão em sí, pois por mais que peguemos a ultima versão do dado e geremos o relatório, segundos depois esse dado já pode estar desatualizado, porém em todos os momentos ele deve estar consistente. Porém John faz uma transferencia da conta 1 para a conta 2 nesse exato momento:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/da099a82-f4b4-47d7-a8ff-b8abf8954d4c)

Nesse caso em específico duas transações rodaram ao mesmo tempo em nível de Read Commited, porém a transação da Alice demorou um pouco mais para executar e uma parte da consulta rodou antes da de John e a segunda parte rodou depois. Antes da query de John rodar, Alice consegiu buscar a informação da conta 1, que retornou 200 reais, porém quando ela foi pegar a informação da conta 2, a transferência de John já havia sido comitada, portanto o nível de isolamento Read Commited vai considerar o novo valor daquela conta, nesse caso 100 reais, pois foram transferidos 300 reais da conta 2 para a conta 1. Nesse caso, Não sei como a Alice vai explicar para o gerente dela que 300 reais sumiram do nada. Para evitarmos esse problema que é conhecido como "Non-repetable reads" vamos ver os proximos níveis de isolamento.

## Snapshot Isolation

Uma observação aqui é que esse nível também pode ser conhecido como Repeatable Read, mas como cada banco de dados implementa o Repeatable Read como acha melhor, eu vou chamar de Snapshot Isolation para manter a discussão em nível conceitual. Como Martin Klepperman fala no livro dele: "Nobody really knows what repeatable read means" [1] isso acontace um pouco porque os isolamentos em si são um pouco ambiguos [2] .

Snapshot Isolation é um nível de isolamento de banco de dados mais forte que Read Commited, que evita não apenas Leitura Suja (Dirty Read), mas também envita a leitura de dados que foram comitados posteriormente ao início da transação em questão. Nesse nível de isolament, quando um dado é alterado por uma transação mas identifica que existe uma transação em Snapshot Isolation ativa, o banco de dados mantém as duas versões salvas, a antiga para a transação de Snapshot Isolation e a nova para as transações que serão abertas após ela.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/eed05bcc-6a92-4252-8a98-c145beb3a63d)






[1] Kleppmann, Martin. Designing Data-Intensive Applications: The Big Ideas Behind Reliable, Scalable, and Maintainable Systems (p. 380). O'Reilly Media. Kindle Edition. 
[2] https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/tr-95-51.pdf
