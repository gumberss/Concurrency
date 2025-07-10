[![pt-br](https://img.shields.io/badge/lang-pt--br-green.svg)](https://github.com/gumberss/Concurrency/blob/main/readme-pt-BR.md)

# Introduction

The challenges related to concurrency in distributed systems are, for the most part, quite complex [1], and there are different levels of safety that can be implemented. The choice of which level to adopt usually depends on what we want to keep consistent. In cases where we prioritize the safety and integrity of information, especially when dealing with data being inserted and read from the database, we choose to sacrifice some performance. However, when performance is prioritized, it is crucial to make decisions carefully, as we do not want to compromise data integrity. This is particularly relevant since data is the most important asset of a company, and strategic decisions often depend on its accuracy and reliability.

# Data Integrity

There are several approaches to ensuring data integrity, each with its own benefits and challenges that must be overcome or, in some cases, accepted due to the solution's limitations. In this document, we will explore some of these strategies.

## Database Transactions

A highly effective artifact developed to handle not only concurrency but also several other aspects (which will not be covered here) is database transaction levels [2]. Each level is designed to address specific potential problems in an application, and the decision to accept such problems in favor of performance varies depending on the needs of each application.

It is important to highlight that different databases may refer to the same concept using different terminology. Therefore, it is recommended to analyze how the database you have chosen behaves at each isolation level.

### Read Uncommitted

The Read Uncommitted isolation level has the least amount of concurrency control and, in my opinion, could more accurately be called "no isolation." In this case, one transaction can affect another without any guarantee of data durability. This happens because this level allows data that has been modified within a transaction, but not yet persisted to the database, to be visible to another transaction.

A simple example illustrates this situation: suppose Alice is transferring 300 reais to John, while at the same time, John is trying to pay a 500-real bill. In Alice’s transaction, the amount is updated to 500 reais, but the transaction has not yet been completed. However, John’s transaction, executed with Read Uncommitted, is able to read the value modified by Alice—even though it has not been committed—and ends up paying the bill with the already modified amount, thereby confirming his transaction. If Alice’s transaction is canceled for any reason, the data in the database loses its integrity.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/732a0bca-ec0e-4096-8562-deb8ad0ae24f)

On the other hand, using the Read Uncommitted isolation level can offer higher performance compared to other transaction levels, since there is no need to handle locks. This increases concurrency between transactions in a somewhat reckless way, placing the full responsibility for avoiding concurrency issues on the developer.

### When to Use Read Uncommitted?

This raises the question: "Why does this isolation level exist?" The answer is simple: there are situations in which we operate in controlled environments and do not need full safety. For example, when generating a report of credit card transactions from the previous month and we are **certain** that no new transactions will occur for that period, it would be possible to use this isolation level.

It is worth emphasizing that the word "certain" is in bold because everything depends on how the application architecture was designed. For instance, if we are using event sourcing to save transactions to the database, and those transactions are inserted sequentially through a message queue (RabbitMQ, Kafka, etc.), there is still a possibility that, even though the transaction occurred last month, it hasn't yet been inserted into the database due to lag in the message queue. In this case, an attempt to insert that data could suffer a rollback even after it has already been included in the report query.

## Read Committed

On the other hand, the Read Committed isolation level ensures isolation between transactions. This means that if another transaction is being executed in parallel with ours, our transaction will not have visibility over data that has been modified by the other transaction but not yet committed (Dirty Read), thus avoiding situations like the one in John’s transaction in the previous example.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/86366fc3-3cef-47c0-a41b-50280dd68636)

On the other hand, consider the scenario where Alice is generating a report with the balance of customers, specifically the balances of accounts with IDs 1 and 2. In this case, the consistency of the data at the moment the report is generated becomes more crucial than having the most up-to-date version. Even if we fetch the latest version of the data to generate the report, seconds later that data might already be outdated. However, consistency is vital at every moment a report is generated.

Let’s suppose that, at this exact moment, John performs a transfer from account 1 to account 2:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/da099a82-f4b4-47d7-a8ff-b8abf8954d4c)

In this specific case, both transactions ran simultaneously at the Read Committed isolation level. However, Alice’s transaction took slightly longer to execute, resulting in a partial execution of the query before John's transaction and the second part after John’s. Before John's query was executed, Alice was able to retrieve the information from account 1, which returned 200 reais. However, by the time she queried account 2, John’s transfer had already been committed. Therefore, the Read Committed isolation level considered the new value of account 2, which in this case was 100 reais, since 300 reais had been transferred from account 2 to account 1.

This issue, known as *Non-repeatable reads*, can create significant challenges, such as having to explain to a manager why 300 reais apparently disappeared out of nowhere. To avoid this kind of problem, let's explore the next isolation levels.

## Snapshot Isolation

Snapshot Isolation is a more robust database isolation level compared to Read Committed. It not only prevents *Dirty Reads*, but also avoids reading data that was committed after the transaction in question began. At this isolation level, when data is modified by a transaction and the system detects the presence of a transaction using Snapshot Isolation, the database keeps both versions of the data [3]: the old version for the Snapshot Isolation transaction, and the new version for transactions that start afterward.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/eed05bcc-6a92-4252-8a98-c145beb3a63d)

In this case, when using Snapshot Isolation, the database considers the data as it was at the moment the transaction began. Therefore, even if the information was modified by another transaction, Alice continues to retrieve the version consistent with the state of the data when her transaction started.

### Notes:

It is important to note that this level can also be referred to as Repeatable Read. However, I chose to use the term Snapshot Isolation to keep the discussion at a conceptual level, since each database implements Repeatable Read according to its own preferred approach. As Martin Kleppmann points out in his book: *"Nobody really knows what repeatable read means"* [4], partly due to the inherent ambiguity in the isolation levels themselves [5].

For example, in SQL Server, to enable Snapshot Isolation as described here, it is necessary to explicitly enable this functionality [6].

## Serializable

It's interesting to see how Snapshot Isolation solved the reporting issue Alice faced. However, now imagine that Alice and John want to avoid running out of money and created a rule that is triggered with every transaction performed by either of them, to check whether the sum of the balances in both of their accounts is greater than a certain threshold. In other words, in the following example, the sum of the money in Alice’s and John’s accounts must not fall below 200 reais. By following this approach, Alice and John can discuss and decide how to proceed before making any payments.

However, when executing transactions using Snapshot Isolation, a *Write Skew* problem can occur. This happens because the updates take place on two different accounts, and each database transaction checks for conflicts only on individual records. Since both transactions are modifying separate records, one does not block the other from committing. In the following example, Alice wants to pay a 400-real bill, and at the same time, John wants to pay a 200-real bill. The system should prevent one of these transactions from happening because, otherwise, their combined balance would be drained without mutual consent:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/e778cda4-18af-4777-b88a-f5b20783468d)

There are two possible ways to handle Write Skew that I’m aware of. One of them is to explicitly lock the records of a table, proactively notifying that the data will be modified. This can be done in Postgres using the `SELECT FOR UPDATE` command. If your database does not support this option, it will most likely be necessary to use the transactional isolation level `SERIALIZABLE` [8], which may impact performance but ensures data integrity. It’s worth noting that even with `SELECT FOR UPDATE`, Postgres may still face the *Phantom Read* problem when a new record is added [7], but I won’t go into detail on that point.

## Pessimistic Locking vs. Optimistic Locking

So, in order to solve all concurrency problems and ensure that our system never violates data integrity, do we always need to use transactions with the `SERIALIZABLE` level and accept the resulting performance penalty?

Let’s consider the implementation of a queue in the database for a system with five instances, all reading messages from that queue. In this scenario, a message might take an indefinite amount of time to be fully processed, and since multiple instances are attempting to process it simultaneously, we’re very likely to face a high level of contention over records (messages), as all instances want to process the same row in the table. In this context, one viable option could be to use a higher isolation level to prevent a message from being processed more than once. Additionally, we need a way to tell the database to skip records that are locked by another transaction. Again, in Postgres, you can achieve this using `SELECT FOR UPDATE` with the `SKIP LOCKED` option [9]. A clear example of this implementation can be found in this video. In this case, we are using the highest isolation level because we believe concurrency is extremely high, and there will be frequent attempts to modify the same record simultaneously. For this reason, we choose a *pessimistic lock*, anticipating the worst-case scenario.

On the other hand, in a real-world bank account scenario, it’s very likely that many updates will happen simultaneously—but on different records. It’s rare for the same person to perform two transactions at the same time. Note that I said *rare*, not *impossible*, since someone might be making a purchase in a physical store while a family member buys something online, or even while a bill is being automatically paid by the system. Concurrency issues can still occur, but they tend to be less frequent. In such cases, we can take on the responsibility of handling exceptions ourselves, reducing the load on the database while still making the most of it.

In this scenario, we tell the database that it may only update certain data if the value hasn’t changed. If, for some reason, the data has been modified, we ask the database to notify us. If it does, we abort our transaction and restart the process from the beginning until we succeed in updating the record. Here, we can use a lower isolation level, such as `Read Committed` [10]. This allows us to reduce the number of locks required by the database, thus lowering its responsibility for managing concurrency, as well as the overall load placed on it.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/b0dd5941-c482-4475-ac46-0bd26b414e77)

In the example above, while Alice was making a purchase, the system automatically paid a bill. Both transactions occurred concurrently, and even though Alice’s transaction started first, it was completed afterward. When the record was being updated to deduct the balance, the second part of the update verification ended up “failing.” This happened because Alice’s balance, which was initially 200 reais, had already become 0 reais due to the system’s transaction, which had persisted the new value first. Since Alice's transaction isolation level was `Read Committed`, she ends up seeing the committed value when attempting the update.

Taking responsibility for concurrency control, we detect that the record was modified, abort the transaction (taking advantage of the atomicity in (A)CID), and restart the process from the beginning. Some databases offer a specific functionality for these situations, called *compare and substitute* or *compare and swap* [11]. The crucial point here is that we are adopting an *optimistic* approach and relying on the assumption that there will be few conflicts. Otherwise, we could end up repeatedly aborting and restarting our transaction, wasting valuable system and database resources. 

If we choose to use optimistic locking in scenarios with high concurrency, there is a chance the system will perform worse compared to using pessimistic locking.

Still considering optimistic locking, we might try to reduce the isolation level even further, for example to `Read Uncommitted`. However, this could lead to inconsistencies in the database, as illustrated below:

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/5a7c3589-65f3-4805-b706-72b8d800313e)

There are other ways to implement optimistic locking, especially when we want to ensure that the data we are about to update has not been modified. Optimistic locking and its verification are generally based on a property of the record that contains its version. Each time the record is updated, its version is incremented, thus providing a strong guarantee of data integrity in the face of any update in the database.

![image](https://github.com/gumberss/Concurrency/assets/38296002/9a55be67-c739-454c-a032-db8cb860d483)

# Load Balancer-Based Partitioning

Another approach to solving concurrency issues is to assign only one service instance to be responsible for updating certain data. In this specific example, we could use the load balancer as a request partitioner. In other words, we can configure Nginx to apply a hash function to the account being updated and, based on the result, determine which instance is responsible for performing the update [12].

It’s important to mention that even when using load balancer-based partitioning, each instance may still have multiple threads and generate concurrency in the database, which must be handled separately. A practical example of this type of implementation was used in the referenced repository and will be explained later.

# Log-Based Message Brokers

Another approach to handling concurrency issues is to use a log-based messaging system, such as Kafka. This type of system allows messages to be ordered by a key. In Kafka’s case, it is possible to assign a key to be used for partitioning messages. In the “rinha” example, we could create two partitions in Kafka and use the account ID as the basis for partitioning. Each service instance could then consume events from a specific partition. This way, only one instance would be responsible for inserting transactions for a given account, thus resolving the concurrency problem between instances.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/fcab9947-dbcf-4d32-8328-81caa4a8cda4)

1. Gatling sends the transaction to Nginx;  
2. Nginx selects the instance that is not responsible for managing the account;  
3. Instance 2, which is not responsible for the account, publishes a message to Kafka with the account number as the partitioning key;  
4. Kafka places the message in the partition that Instance 1 is responsible for, and Instance 1 receives this event;  
5. Instance 1 correctly updates the database with the information.

For Instance 2, which received the request, to provide the correct HTTP response, it can send a response topic along with the event, and Instance 1 can publish a new message containing the response. Another approach would be to provide a dedicated HTTP endpoint to return the response. In summary, the implementation may vary according to each developer’s creativity, considering trade-offs and subsequent load testing. Response management can be implemented in memory, as exemplified in the code of this repository.

![image](https://github.com/gumberss/Rinha-Sharding/assets/38296002/f3018d6c-e585-472b-ad36-bc7b8c5a0cf0)

1. Instance 1 publishes the response to the response topic of the event it received;  
2. Kafka forwards the response to Instance 2;  
3. Instance 2 handles the response and returns it to Nginx (this can be implemented as in this repository);  
4. Nginx returns the response to Gatling.

# API Sharding

This was the solution adopted in the repository in question. Due to memory and processing limitations, the efficient introduction of a robust Message Broker like Kafka—which handles issues such as fault tolerance, partitioning, consumer health monitoring, and ensures a consumer is always available through consensus protocols—would be challenging.

Given the limitations mentioned above, the solution in this repository would require a substantial redesign to incorporate all these elements, as well as other essential aspects in distributed systems. Even so, this solution served as a valuable proof of concept on how to face and solve concurrency problems.

The solution adopted by the repository involves assigning each instance responsibility for specific database accounts. Since there are 2 instances and 6 accounts, each is responsible for 3 specific accounts. If an instance receives a request that is not under its responsibility, it automatically forwards it to the other instance (step number 3 in the image below). The HTTP endpoint inserts the requests into a processing queue and waits for the response in the response queue. An asynchronous job is responsible for reading messages from the processing queue, performing validations, batch inserting into the database, and placing the response in the response queue. This queue is then read by the HTTP endpoint, which forwards the response to the requester.

![image](https://github.com/gumberss/Concurrency/assets/38296002/6f1247b8-4598-4a7d-94e4-bd1aaf1d92be)

## Results of this solution

As mentioned earlier, this was the solution used in this repository and below are the results obtained.

![image](https://github.com/user-attachments/assets/b2e94fa3-4ff5-4154-8565-61336685b5fa)

# Other Ways to Solve

There are countless other ways to solve concurrency problems, and it is extremely valuable to use challenges like the “rinha” to improve techniques. Of course, when taking a solution to production, many other aspects need to be considered beyond just solving performance and concurrency issues. However, during the challenge, nothing prevents you from training your skills and problem-solving abilities, adding new tools to your arsenal.

# References


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
