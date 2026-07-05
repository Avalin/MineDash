using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleActivityStore
{
  event Action<string>? HistoryChanged;

  IReadOnlyList<CommandHistoryItem> GetHistory(string serverId);

  Task AppendAsync(string serverId, CommandHistoryItem item, CancellationToken ct = default);
}
