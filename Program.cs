using RinhaPessimisticLockingApi;
using Npgsql;
using Microsoft.AspNetCore.Mvc;

using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Sharding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options =>
        options.CustomizeProblemDetails = (context) =>
        {
            Console.WriteLine(context.ProblemDetails.Title);
            Console.WriteLine(context.Exception?.ToString());
        }
    );
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();
builder.Services.AddHostedService<Worker>();
var app = builder.Build();
app.UseStatusCodePages();
app.UseExceptionHandler();

int shard = int.Parse(Environment.GetEnvironmentVariable("SHARD") ?? "0");
Console.WriteLine(shard);


static async Task<IResult> ProcessTransaction(int accountId, Transaction transaction, ConcurrentDictionary<int, ConcurrentQueue<Transaction>> queue, ConcurrentDictionary<Transaction, Account> result)
{
    if (TransactionValidator.IsValid(transaction))
    {
        if (queue.TryGetValue(accountId, out ConcurrentQueue<Transaction> accountQueue))
        {
            accountQueue.Enqueue(transaction);
        }
        else
        {
            await Console.Out.WriteLineAsync("Eita vish");
        }
        Stopwatch s = new Stopwatch();
        s.Start();
        await Task.Delay(2);
        Account resultValue;

        while (!result.TryGetValue(transaction, out resultValue))
        {
            await Task.Delay(3);

            if (s.ElapsedMilliseconds > 1500)
            {
                await Console.Out.WriteLineAsync("Tristeza pesada" + accountQueue.Count);
                break;
            }
        }

        s.Stop();
        Console.WriteLine("Aquele Abraço" + s.ElapsedMilliseconds.ToString());
        if (resultValue is not null)
            return Results.Ok(new { limite = resultValue.Limite, saldo = resultValue.Saldo });
        else
            return Results.UnprocessableEntity();
    }
    else
    {
        return Results.UnprocessableEntity();
    }

}

static async Task<IResult> Processor(int accountId, Transaction transaction, int retryTimes = 0)
{
    try
    {
        return await ProcessTransaction(accountId, transaction, Worker.queue, Worker.result);
    }
    catch (Exception e)
    {
        if (retryTimes > 5)
        {
            return Results.Problem(title: "Banana" + e.Message, detail: e.ToString());
        }

        var notExponential = retryTimes * 100;

        var jitter = new Random().Next(1, 100);

        await Task.Delay(TimeSpan.FromMilliseconds(notExponential));

        return await Processor(accountId, transaction, ++retryTimes);
    }
}


static async Task<IResult> Requester(int id, Transaction transaction, int shard)
{
    var toShard = shard == 0 ? 1 : 0;
    string apiUrl = $"http://api0{toShard}:8080/clientes/{id}/transacoes/priority";

    // Create an instance of HttpClient
    using (HttpClient httpClient = new HttpClient())
    {
        try
        {
            string requestData = JsonSerializer.Serialize(transaction);

            // Create a StringContent with the data and specify the media type
            StringContent content = new StringContent(requestData, Encoding.UTF8, "application/json");

            // Make a POST request with the content
            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            if (response.StatusCode != System.Net.HttpStatusCode.OK
                && response.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity
                && response.StatusCode != System.Net.HttpStatusCode.NotFound
                && response.StatusCode != System.Net.HttpStatusCode.BadRequest)
                await Console.Out.WriteLineAsync("Vish " + response.StatusCode);

            return response switch
            {
                { StatusCode: System.Net.HttpStatusCode.OK } => Results.Ok(await response.Content.ReadFromJsonAsync<object?>()),
                { StatusCode: System.Net.HttpStatusCode.UnprocessableEntity } => Results.UnprocessableEntity(),
                { StatusCode: System.Net.HttpStatusCode.NotFound } => Results.NotFound(),
                { StatusCode: System.Net.HttpStatusCode.BadRequest } => Results.BadRequest(),
                _ => Results.Problem(title: "Vish Response")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex.Message);
        }
    }

    return null;
}


app.MapPost("/clientes/{id}/transacoes", async (int id, [FromBody] Transaction transaction) =>
{
    if (id % 2 == shard)
        return await Processor(id, transaction);
    else
        return await Requester(id, transaction, shard);

});

app.MapPost("/clientes/{id}/transacoes/priority", async (int id, [FromBody] Transaction transaction) =>
{
    return await Processor(id, transaction);
});

NpgsqlConnectionStringBuilder b = new NpgsqlConnectionStringBuilder();
b.Host = "db";
b.Port = 5432;
b.Database = "rinha";
b.Username = "theuser";
b.Password = "TheP@ssw0rd!";
b.Pooling = true;
b.MinPoolSize = 10;
b.MaxPoolSize = 25;
//b.MaxAutoPrepare = 50;
//b.AutoPrepareMinUsages = 1;

var dataSourceBuilder = new NpgsqlDataSourceBuilder(b.ToString());

var dataSource = dataSourceBuilder.Build();

app.MapGet("/clientes/{id}/extrato", async (int id) =>
{

    var query = @"select * from accounts aa
left join transactions tt on aa.id = tt.accountId
where aa.id = @accountId
order by tt.id desc
limit 10;";

    try
    {
        Stopwatch s = new Stopwatch();
        s.Start();

        await using (var conn = await dataSource.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {

            cmd.CommandText = query;
            cmd.Parameters.AddWithValue("@accountId", id);
            var r = await cmd.ExecuteReaderAsync();

            if (r.Read())
            {
                var limit = r.GetInt32(2);
                var balance = r.GetInt32(3);
                var acc = new AccountView(limit, balance, DateTime.UtcNow.ToString());

                if (!r.IsDBNull(6))
                {
                    var transaction = new TransactionView
                    {
                        Descricao = r.GetString(8),
                        Tipo = r.GetString(7),
                        Valor = r.GetInt32(6),
                        realizada_em = r.GetDateTime(9).ToString(),
                    };
                    acc.ultimas_transacoes.Add(transaction);
                    while (r.Read())
                    {
                        var t = new TransactionView
                        {
                            Descricao = r.GetString(8),
                            Tipo = r.GetString(7),
                            Valor = r.GetInt32(6),
                            realizada_em = r.GetDateTime(9).ToString(),
                        };

                        acc.ultimas_transacoes.Add(t);
                    }
                }
                s.Stop();

                await Console.Out.WriteLineAsync("Rapidex " + s.ElapsedMilliseconds);
                return Results.Ok(acc);
            }
            else
            {
                s.Stop();

                await Console.Out.WriteLineAsync("Rapidex not found" + s.ElapsedMilliseconds);
                return Results.NotFound();
            }
        }

    }
    catch (Exception e)
    {
        await Console.Out.WriteLineAsync(e.Message);
        await Console.Out.WriteLineAsync(e.ToString());
        return Results.StatusCode(500);
    }
});







app.Run();






