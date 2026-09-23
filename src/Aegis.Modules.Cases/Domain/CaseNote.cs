namespace Aegis.Modules.Cases.Domain;

public sealed record CaseNote(Guid Id, string Text, string AuthorId, DateTimeOffset CreatedAt);
