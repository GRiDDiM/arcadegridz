using System.Net;
using System.Text.Json;
using ArcadeGridz.App.Models;
namespace ArcadeGridz.App.Services;
public sealed class CatalogService
{
    const string Endpoint="https://arcadegridz.com/api/launcher/catalog";
    readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(8)};
    readonly JsonSerializerOptions json=new(){PropertyNameCaseInsensitive=true};
    string CacheFile=>Path.Combine(AppPaths.Cache,"catalog.json");
    string EtagFile=>Path.Combine(AppPaths.Cache,"catalog.etag");
    public async Task<(LauncherCatalog Catalog,bool Offline)> LoadAsync(CancellationToken ct=default)
    {
        AppPaths.Ensure();
        try {
            using var req=new HttpRequestMessage(HttpMethod.Get,Endpoint);
            if(File.Exists(EtagFile)) req.Headers.TryAddWithoutValidation("If-None-Match",await File.ReadAllTextAsync(EtagFile,ct));
            using var res=await http.SendAsync(req,ct);
            if(res.StatusCode==HttpStatusCode.NotModified)return (await ReadCache(ct),false);
            res.EnsureSuccessStatusCode();
            var raw=await res.Content.ReadAsStringAsync(ct);var value=JsonSerializer.Deserialize<LauncherCatalog>(raw,json)??throw new InvalidDataException("Empty catalog");
            var tmp=CacheFile+".tmp";await File.WriteAllTextAsync(tmp,raw,ct);File.Move(tmp,CacheFile,true);
            if(res.Headers.ETag is not null)await File.WriteAllTextAsync(EtagFile,res.Headers.ETag.ToString(),ct);
            return (value,false);
        } catch when(File.Exists(CacheFile)) { return (await ReadCache(ct),true); }
    }
    async Task<LauncherCatalog> ReadCache(CancellationToken ct)=>JsonSerializer.Deserialize<LauncherCatalog>(await File.ReadAllTextAsync(CacheFile,ct),json)??throw new InvalidDataException("Bad cached catalog");
}
