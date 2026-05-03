using JiraBridge.Infrastructure.Environment;

namespace JiraBridge.Infrastructure.Jira;

public interface IJiraApiClientFactory
{
  JiraApiClient Create(JiraSettings settings);
}
