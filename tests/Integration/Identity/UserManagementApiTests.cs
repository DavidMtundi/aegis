namespace Aegis.Tests.Integration.Identity;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aegis.Shared.Security;
using Aegis.Tests.Integration.Alerts;

public sealed class UserManagementApiTests : IAsyncLifetime
{
    private readonly AlertTestFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();
    public Task DisposeAsync() => _fx.DisposeAsync();

    private async Task<Guid> CreateUserAsync(string email, params string[] roles)
    {
        var response = await _fx.AdminClient.PostAsJsonAsync("/api/v1/users", new
        {
            email,
            name = "New User",
            password = "Passw0rd!",
            roles
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> LoginAsync(string email) =>
        await _fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Passw0rd!",
            tenantSlug = _fx.Slug
        });

    [Fact]
    public async Task Admin_creates_lists_changes_roles_and_deactivates_users()
    {
        var email = $"analyst@{_fx.Slug}.test";
        var id = await CreateUserAsync(email, "analyst");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);

        var list = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/users");
        var created = list.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
        Assert.Equal(new[] { RoleNames.Analyst }, created.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("ACTIVE", created.GetProperty("status").GetString());

        var roles = await _fx.AdminClient.PutAsJsonAsync($"/api/v1/users/{id}/roles", new { roles = new[] { "Reviewer" } });
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Equal("Reviewer", (await roles.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles")[0].GetString());

        var deactivate = await _fx.AdminClient.PostAsync($"/api/v1/users/{id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Validation_rejects_unknown_roles_duplicates_and_short_passwords()
    {
        await CreateUserAsync($"dup@{_fx.Slug}.test", "Viewer");

        var unknownRole = await _fx.AdminClient.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"x@{_fx.Slug}.test", name = "X", password = "Passw0rd!", roles = new[] { "Superuser" }
        });
        var duplicate = await _fx.AdminClient.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"DUP@{_fx.Slug}.test", name = "Dup", password = "Passw0rd!", roles = new[] { "Viewer" }
        });
        var shortPassword = await _fx.AdminClient.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"y@{_fx.Slug}.test", name = "Y", password = "short", roles = new[] { "Viewer" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_out()
    {
        var me = await _fx.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/users");
        var adminId = me.EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == $"admin@{_fx.Slug}.test")
            .GetProperty("id").GetGuid();

        var deactivateSelf = await _fx.AdminClient.PostAsync($"/api/v1/users/{adminId}/deactivate", null);
        var dropOwnAdmin = await _fx.AdminClient.PutAsJsonAsync($"/api/v1/users/{adminId}/roles", new { roles = new[] { "Viewer" } });

        Assert.Equal(HttpStatusCode.BadRequest, deactivateSelf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, dropOwnAdmin.StatusCode);
    }

    [Fact]
    public async Task Non_admins_cannot_manage_users()
    {
        var reviewer = await _fx.CreateUserClientAsync($"rev@{_fx.Slug}.test", new[] { RoleNames.Reviewer });

        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"z@{_fx.Slug}.test", name = "Z", password = "Passw0rd!", roles = new[] { "Admin" }
        })).StatusCode);
    }
}
