
CREATE TABLE accounts (
	id SERIAL PRIMARY KEY,
	name VARCHAR(50) NOT NULL,
	limitAmount INTEGER NOT NULL,
	balance INTEGER NOT NULL
);

CREATE TABLE transactions (
	id SERIAL PRIMARY KEY,
	accountId INTEGER NOT NULL,
	amount INTEGER NOT NULL,
	transactionType CHAR(1) NOT NULL,
	description VARCHAR(10) NOT NULL,
	date TIMESTAMP NOT NULL DEFAULT NOW(),
	CONSTRAINT fk_accounts_transactions_id
		FOREIGN KEY (accountId) REFERENCES accounts(id)
);

DO $$
BEGIN
  INSERT INTO accounts (name, limitAmount, balance)
  VALUES
    ('o barato sai caro', 1000 * 100, 0),
    ('zan corp ltda', 800 * 100, 0),
    ('les cruders', 10000 * 100, 0),
    ('padaria joia de cocaia', 100000 * 100, 0),
    ('kid mais', 5000 * 100, 0);
END; $$