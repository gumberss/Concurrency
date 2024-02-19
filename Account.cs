namespace RinhaPessimisticLockingApi
{
    public class Account
    {
        public int Limite;
        public int Saldo;

        public Account(int limite, int saldo)
        {
            Limite = limite;
            Saldo = saldo;
        }
    }

    public class SaldoView
    {
        public int Limite { get; set; }
        public int Total { get; set; }
        public string data_extrato { get; set; }
    }

    public class AccountView
    {
        public SaldoView Saldo { get; set; }
        public List<TransactionView> ultimas_transacoes { get; set; }

        public AccountView(int limite, int saldo, string data_extrato)
        {
            Saldo = new SaldoView
            {
                Limite = limite,
                Total = saldo,
                data_extrato = data_extrato,
            };

            ultimas_transacoes = new List<TransactionView>();
        }
    }

    public class TransactionView
    {
        public int? Valor { get; set; }
        public String? Tipo { get; set; }
        public String? Descricao { get; set; }
        public String? realizada_em { get; set; }

    }
}
