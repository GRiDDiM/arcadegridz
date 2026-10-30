using ArcadeGridz.App.Models;
namespace ArcadeGridz.App.Runtimes;
public interface IGameRuntime { bool CanRun(GameEntry game); Task LaunchAsync(GameEntry game,CancellationToken ct=default); Task ExitAsync(); event EventHandler? Exited; }
