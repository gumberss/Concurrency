
namespace RinhaPessimisticLockingApi
{
    public static class TransactionValidator
    {
        public static bool IsValid(Transaction transaction)
         => new List<Func<Transaction, bool>> { ValidValue, ValidType, ValidDescription }
         .All(x => x(transaction));
        
        private static bool ValidValue(Transaction transaction)
            => transaction.Valor is not null && Math.Ceiling(transaction.Valor.Value) == transaction.Valor;

        private static bool ValidType(Transaction transaction)
            => transaction is { Tipo: "c" or "d" };

        private static bool ValidDescription(Transaction transaction)
          => transaction is { Descricao: { Length: <= 10  and > 0} };

    }
}
