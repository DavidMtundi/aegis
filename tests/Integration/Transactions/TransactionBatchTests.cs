namespace Aegis.Tests.Integration.Transactions;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class TransactionBatchTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    private object Tx(string reference, decimal amount = 1_000m, Guid? accountId = null) => new
    {
        externalReference = reference,
        accountId = accountId ?? _fx.AccountId,
        customerId = _fx.CustomerId,
        amount,
        currency = "KES",
        direction = "CREDIT",
        transactionType = "TRANSFER",
        channel = "MOBILE",
        timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    [Fact]
    public async Task Json_batch_reports_created_duplicate_and_failed_rows()
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/transactions/batch", new
        {
            transactions = new[] { Tx("B-1"), Tx("B-2"), Tx("B-1"), Tx("B-3", accountId: Guid.NewGuid()) }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("created").GetInt32());
        Assert.Equal(1, body.GetProperty("duplicates").GetInt32());
        Assert.Equal(1, body.GetProperty("failed").GetInt32());

        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(new[] { "CREATED", "CREATED", "DUPLICATE", "FAILED" },
            results.Select(r => r.GetProperty("status").GetString()).ToArray());
        Assert.Equal(4, results[3].GetProperty("row").GetInt32());
        Assert.Contains("Account", results[3].GetProperty("error").GetString());

        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>($"/api/v1/transactions?customerId={_fx.CustomerId}");
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Csv_import_creates_valid_rows_and_reports_line_numbers_for_bad_ones()
    {
        var ts = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
        var csv = new StringBuilder()
            .AppendLine("externalReference,accountId,customerId,amount,currency,direction,transactionType,channel,timestamp")
            .AppendLine($"C-1,{_fx.AccountId},{_fx.CustomerId},500,KES,CREDIT,TRANSFER,MOBILE,{ts}")
            .AppendLine($"C-2,{_fx.AccountId},{_fx.CustomerId},abc,KES,CREDIT,TRANSFER,MOBILE,{ts}")
            .AppendLine($"C-3,{_fx.AccountId},{_fx.CustomerId},700,KES,DEBIT,TRANSFER,MOBILE,{ts}")
            .ToString();
        using var form = new MultipartFormDataContent();
        var file = new StringContent(csv, Encoding.UTF8);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "transactions.csv");

        var response = await _fx.AdminClient.PostAsync("/api/v1/transactions/import", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("created").GetInt32());
        Assert.Equal(1, body.GetProperty("failed").GetInt32());
        var failed = body.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("status").GetString() == "FAILED");
        Assert.Equal(3, failed.GetProperty("row").GetInt32());
        Assert.Equal("C-2", failed.GetProperty("externalReference").GetString());
    }

    [Fact]
    public async Task Csv_with_missing_columns_is_rejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("externalReference,amount\nX,1\n", Encoding.UTF8), "file", "bad.csv");

        var response = await _fx.AdminClient.PostAsync("/api/v1/transactions/import", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Batch_over_limit_or_empty_is_rejected()
    {
        var tooMany = Enumerable.Range(0, 1001).Select(i => Tx($"L-{i}")).ToArray();

        var over = await _fx.AdminClient.PostAsJsonAsync("/api/v1/transactions/batch", new { transactions = tooMany });
        var empty = await _fx.AdminClient.PostAsJsonAsync("/api/v1/transactions/batch", new { transactions = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_batch_ingest()
    {
        var viewer = await _fx.CreateUserClientAsync($"viewer@{_fx.Slug}.test", new[] { RoleNames.Viewer });

        var response = await viewer.PostAsJsonAsync("/api/v1/transactions/batch", new { transactions = new[] { Tx("V-1") } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
