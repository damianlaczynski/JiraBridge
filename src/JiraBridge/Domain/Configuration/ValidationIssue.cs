namespace JiraBridge.Domain.Configuration;

public sealed record ValidationIssue(string FilePath, string Message);
