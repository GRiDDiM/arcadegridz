using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
namespace ArcadeGridz.App.Runtimes;
public sealed class NativeGameRuntime : IGameRuntime
{
    readonly HttpClient http=new();Process? process;public event EventHandler? Exited;
    public bool CanRun(GameEntry g)=>g.Runtime.Type.Equals("native",StringComparison.OrdinalIgnoreCase)&&g.Runtime.PackageUrl is not null&&g.Runtime.Sha256 is not null&&g.Runtime.Exe is not null;
    public async Task LaunchAsync(GameEntry g,CancellationToken ct=default){if(!CanRun(g))throw new InvalidOperationException("Incomplete native runtime manifest");AppPaths.Ensure();var dir=Path.Combine(AppPaths.Games,g.Id,g.Runtime.Version??"current");var exe=Path.Combine(dir,g.Runtime.Exe!);
      if(!File.Exists(exe)){Directory.CreateDirectory(dir);var zip=Path.Combine(dir,"package.zip");await File.WriteAllBytesAsync(zip,await http.GetByteArrayAsync(g.Runtime.PackageUrl!,ct),ct);await using var f=File.OpenRead(zip);var hash=Convert.ToHexString(await SHA256.HashDataAsync(f,ct));if(!hash.Equals(g.Runtime.Sha256,StringComparison.OrdinalIgnoreCase)){File.Delete(zip);throw new InvalidDataException("Native package SHA-256 mismatch");}ZipFile.ExtractToDirectory(zip,dir,true);File.Delete(zip);if(!File.Exists(exe))throw new FileNotFoundException("Native executable missing",exe);}
      process=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=dir,UseShellExecute=false})??throw new InvalidOperationException("Could not start game");process.EnableRaisingEvents=true;process.Exited+=(s,e)=>Exited?.Invoke(this,EventArgs.Empty);}
    public Task ExitAsync(){if(process is {HasExited:false})process.CloseMainWindow();return Task.CompletedTask;}
}
