namespace JiraBridge.Infrastructure.Environment;

public sealed record JiraSettings(
  Uri BaseUri,
  string Email,
  string ApiToken);
