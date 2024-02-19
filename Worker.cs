
using Npgsql;
using RinhaPessimisticLockingApi;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharding
{
    public class Worker : BackgroundService
    {
        public static ConcurrentDictionary<int, ConcurrentQueue<Transaction>> queue = new ConcurrentDictionary<int, ConcurrentQueue<Transaction>>();

        public static ConcurrentDictionary<Transaction, Account> result = new ConcurrentDictionary<Transaction, Account>();


        public Worker()
        {
            for (int i = 1; i < 6; i++)
                queue[i] = new ConcurrentQueue<Transaction>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            NpgsqlConnectionStringBuilder b = new NpgsqlConnectionStringBuilder();
            b.Host = "db";
            b.Port = 5432;
            b.Database = "rinha";
            b.Username = "theuser";
            b.Password = "TheP@ssw0rd!";
            b.Pooling = true;
            b.MinPoolSize = 6;
            b.MaxPoolSize = 25;
            //b.MaxAutoPrepare = 50;
            //b.AutoPrepareMinUsages = 1;

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(b.ToString());

            var dataSource = dataSourceBuilder.Build();

            var tasks = Enumerable.Range(1, queue.Count)
                .Select(x => Task.Run(async () =>
                {
                    await QueueProcessor(x, dataSource);
                })).ToList();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(500);
            }

            tasks.ToList().ForEach(x => x.Dispose());
        }

        private async Task QueueProcessor(int accountId, NpgsqlDataSource dataSource)
        {
            Account account = await GetAccount(accountId, dataSource);
            var originalBalance = account.Saldo;
            ConcurrentQueue<Transaction> processor;

            while (!queue.TryGetValue(accountId, out processor))
            {
                await Task.Delay(20);
            }

            List<Transaction> transactions = new List<Transaction>(20);

            while (true)
            {
                await Task.Delay(3);
                Transaction t = null;
                while (transactions.Count < 500
                    && processor.TryDequeue(out t))
                {
                    transactions.Add(t);
                }

                if (!transactions.Any()) continue;

                List<Transaction> unprocesable = new List<Transaction>();

                var transactionAccVal = account.Saldo;

                foreach (var x in transactions)
                {
                    var fixedTransactionValue = x.Tipo == "c" ? (int)x.Valor.Value : (int)-x.Valor.Value;
                    if (transactionAccVal + fixedTransactionValue > -account.Limite)
                    {
                        transactionAccVal += fixedTransactionValue;
                    }
                    else
                        unprocesable.Add(x);
                }

                transactions = transactions.Except(unprocesable).ToList();

                if(transactions.Any())
                    await ProcessTransactions(accountId, account, transactions, dataSource);

                transactions.ForEach(x =>
                {
                    account.Saldo += x.Tipo == "c" ? (int)x.Valor.Value : (int)-x.Valor.Value;
                    result.TryAdd(x, new Account(account.Limite, account.Saldo));
                });
                unprocesable.ForEach(x => result.TryAdd(x, null));
                transactions.Clear();
                unprocesable.Clear();
            }
        }

        async Task<bool> ProcessTransactions(int accountId, Account account, List<Transaction> transactions, NpgsqlDataSource dataSource)
        {
            if (transactions.Count > 1)
                await Console.Out.WriteLineAsync("virsinator " + transactions.Count);

            int index = 0;
            var finalBalance = account.Saldo + transactions.Sum(x => x.Tipo == "c" ? x.Valor.Value : -x.Valor.Value);
            string accQuery = "update accounts set balance = @balance where id = @accountId";


            string query = @"
insert into transactions(accountid, amount, transactiontype, description)
VALUES " + String.Join(",\n", transactions.Select(x => $"(@v{++index}, @v{++index}, @v{++index}, @v{++index})"));

            await using (var conn = await dataSource.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                Stopwatch s = new Stopwatch();
                s.Start();

                cmd.CommandText = accQuery;
                cmd.Parameters.AddWithValue("@balance", finalBalance);
                cmd.Parameters.AddWithValue("@accountId", accountId);
                cmd.ExecuteNonQuery();


                cmd.CommandText = query;
                index = 0;
                transactions.ForEach(x =>
                {
                    cmd.Parameters.AddWithValue($"@v{++index}", accountId);
                    cmd.Parameters.AddWithValue($"@v{++index}", x.Valor);
                    cmd.Parameters.AddWithValue($"@v{++index}", x.Tipo);
                    cmd.Parameters.AddWithValue($"@v{++index}", x.Descricao);
                });


              
                var result = await cmd.ExecuteNonQueryAsync();
                s.Stop();
                await Console.Out.WriteLineAsync("db: " + s.ElapsedMilliseconds.ToString());

                return true;
            }
        }

        async Task<Account> GetAccount(int accountId, NpgsqlDataSource dataSource)
        {
            try
            {
                var query = "select * from accounts  where id = @accountId limit 1";
                await using (var conn = await dataSource.OpenConnectionAsync())
                await using (var cmd = conn.CreateCommand())
                {

                    cmd.CommandText = query;
                    cmd.Parameters.AddWithValue("@accountId", accountId);
                    var r = await cmd.ExecuteReaderAsync();

                    if (r.Read())
                    {
                        var limit = r.GetInt32(2);
                        var balance = r.GetInt32(3);
                        return new Account(limit, balance);
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                await Task.Delay(1000);
                return await GetAccount(accountId, dataSource);
            }
        }
    }
}
