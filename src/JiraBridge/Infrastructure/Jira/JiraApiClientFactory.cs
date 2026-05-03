using JiraBridge.Infrastructure.Environment;

namespace JiraBridge.Infrastructure.Jira;

public sealed class JiraApiClientFactory : IJiraApiClientFactory
{
  public JiraApiClient Create(JiraSettings settings) => new(settings);
}
