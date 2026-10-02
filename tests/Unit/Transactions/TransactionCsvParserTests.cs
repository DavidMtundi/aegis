namespace Aegis.Tests.Unit.Transactions;

using Aegis.Application.Transactions;

public sealed class TransactionCsvParserTests
{
    private const string Header = "externalReference,accountId,customerId,amount,currency,direction,transactionType,channel,timestamp,counterpartyCountry";
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Customer = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Parses_rows_using_header_names_in_any_order_and_case()
    {
        var csv = "Amount,externalreference,AccountId,customerId,currency,direction,transactionType,channel,timestamp\n" +
                  $"95000.50,TX-1,{Account},{Customer},kes,CREDIT,TRANSFER,MOBILE,2026-10-01T08:00:00Z\n";

        var result = TransactionCsvParser.Parse(csv);

        Assert.Empty(result.HeaderErrors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.LineNumber);
        Assert.Null(row.Error);
        Assert.Equal("TX-1", row.Payload!.ExternalReference);
        Assert.Equal(95000.50m, row.Payload.Amount);
        Assert.Equal(Account, row.Payload.AccountId);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), row.Payload.Timestamp);
        Assert.Null(row.Payload.CounterpartyCountry);
    }

    [Fact]
    public void Handles_quoted_fields_with_commas_and_escaped_quotes()
    {
        var csv = Header + "\n" +
                  $"\"TX,\"\"2\"\"\",{Account},{Customer},10,KES,DEBIT,TRANSFER,MOBILE,2026-10-01T08:00:00Z,KE\n";

        var row = Assert.Single(TransactionCsvParser.Parse(csv).Rows);

        Assert.Null(row.Error);
        Assert.Equal("TX,\"2\"", row.Payload!.ExternalReference);
        Assert.Equal("KE", row.Payload.CounterpartyCountry);
    }

    [Fact]
    public void Reports_missing_required_columns()
    {
        var result = TransactionCsvParser.Parse("externalReference,amount\nTX-1,10\n");

        Assert.Contains("accountId", result.HeaderErrors.Single());
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Bad_values_fail_only_their_row_and_blank_lines_are_skipped()
    {
        var csv = Header + "\r\n" +
                  $"TX-1,{Account},{Customer},not-a-number,KES,CREDIT,TRANSFER,MOBILE,2026-10-01T08:00:00Z,\r\n" +
                  "\r\n" +
                  $"TX-2,{Account},{Customer},10,KES,CREDIT,TRANSFER,MOBILE,2026-10-01T08:00:00Z,\r\n";

        var rows = TransactionCsvParser.Parse(csv).Rows;

        Assert.Equal(2, rows.Count);
        Assert.Contains("amount", rows[0].Error);
        Assert.Equal(2, rows[0].LineNumber);
        Assert.Null(rows[1].Error);
        Assert.Equal(4, rows[1].LineNumber);
    }

    [Fact]
    public void Rows_with_wrong_column_count_fail()
    {
        var csv = Header + "\nTX-1,only-two\n";

        var row = Assert.Single(TransactionCsvParser.Parse(csv).Rows);

        Assert.Contains("columns", row.Error);
    }
}
