using System.Security.Cryptography;
namespace ArcadeGridz.App.Services;
public sealed class ArtCache
{
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(8)};
    public async Task<Uri?> GetAsync(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var remote))return null;AppPaths.Ensure();
        var ext=Path.GetExtension(remote.AbsolutePath);if(ext.Length>5)ext=".img";
        var file=Path.Combine(AppPaths.Art,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+ext);
        if(!File.Exists(file))try{var bytes=await http.GetByteArrayAsync(remote);var tmp=file+".tmp";await File.WriteAllBytesAsync(tmp,bytes);File.Move(tmp,file,true);}catch{return remote;}
        return new Uri(file);
    }
}
