using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
namespace QuantixTweakingUtility.Services;
public static class BiosToolSetup {
    public static async Task<string> PrepareAsync(Action<string> progress) {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","BiosTool");Directory.CreateDirectory(root);
        using var gate=new FileStream(Path.Combine(root,"setup.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string ready=Path.Combine(root,"ready","SCEWIN_64.exe");if(File.Exists(ready))return ready;
        string stage=Path.Combine(root,"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(20)};
        async Task Download(string url,string file,long limit) {using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();using var input=await response.Content.ReadAsStreamAsync();using var output=File.Create(file);byte[] buffer=new byte[131072];long total=0;int length;while((length=await input.ReadAsync(buffer))>0){total+=length;if(total>limit)throw new IOException("BIOS package exceeded size limit.");await output.WriteAsync(buffer.AsMemory(0,length));}}
        static string ExtractOne(string zip,string folder,Func<ZipArchiveEntry,bool> match) {using var archive=ZipFile.OpenRead(zip);var entries=archive.Entries.Where(match).ToArray();if(entries.Length!=1 || entries[0].Length>2000000000)throw new IOException("Unexpected BIOS package layout.");Directory.CreateDirectory(folder);string file=Path.Combine(folder,Path.GetFileName(entries[0].FullName));entries[0].ExtractToFile(file);return file;}
        string extractorZip=Path.Combine(stage,"extractor.zip");progress("Preparing BIOS package extractor…");
        await Download("https://github.com/dscharrer/innoextract/releases/download/1.9/innoextract-1.9-windows.zip",extractorZip,20000000);
        using(var hashFile=File.OpenRead(extractorZip))if(Convert.ToHexString(SHA256.HashData(hashFile))!="6989342C9B026A00A72A38F23B62A8E6A22CC5DE69805CF47D68AC2FEC993065")throw new IOException("BIOS extractor verification failed.");
        string extractor=ExtractOne(extractorZip,Path.Combine(stage,"extractor"),e=>e.FullName=="innoextract.exe");
        async Task Unpack(string installer,string folder) {Directory.CreateDirectory(folder);var info=new ProcessStartInfo(extractor){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(installer);info.ArgumentList.Add("--output-dir");info.ArgumentList.Add(folder);using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await output;string log=await error;if(p.ExitCode!=0)throw new IOException("BIOS package extraction failed: "+log);}
        progress("Downloading BIOS support package from MSI (large first-time download)…");string package=Path.Combine(stage,"msi.zip");await Download("https://download.msi.com/uti_exe/desktop/MSI-Center.zip",package,1500000000);
        string installer=ExtractOne(package,Path.Combine(stage,"installer"),e=>Path.GetFileName(e.FullName).StartsWith("MSI Center_",StringComparison.OrdinalIgnoreCase)&&e.FullName.EndsWith(".exe",StringComparison.OrdinalIgnoreCase));
        progress("Extracting BIOS support files…");string app=Path.Combine(stage,"app");await Unpack(installer,app);
        string bundle=Directory.GetFiles(app,"*.appxbundle",SearchOption.AllDirectories).Single();string appx=ExtractOne(bundle,Path.Combine(stage,"bundle"),e=>e.FullName.EndsWith("_x64.appx",StringComparison.OrdinalIgnoreCase));
        string sdk=ExtractOne(appx,Path.Combine(stage,"sdk"),e=>e.FullName=="DCv2/Package/MSI%20Center%20SDK.exe");string sdkFolder=Path.Combine(stage,"sdk-unpacked");await Unpack(sdk,sdkFolder);
        string engine=Directory.GetFiles(Path.Combine(sdkFolder,"tmp","PrePackage"),"Engine Lib_*.exe").Single();string engineFolder=Path.Combine(stage,"engine");await Unpack(engine,engineFolder);
        string source=Directory.GetFiles(Path.Combine(engineFolder,"app","Lib","SCEWIN"),"SCEWIN_64.exe",SearchOption.AllDirectories).OrderByDescending(p=>p,StringComparer.Ordinal).First();
        string prepared=Path.Combine(stage,"ready");Directory.CreateDirectory(prepared);foreach(string name in new[]{"SCEWIN_64.exe","amifldrv64.sys","amigendrv64.sys"})File.Copy(Path.Combine(Path.GetDirectoryName(source)!,name),Path.Combine(prepared,name));
        Directory.Move(prepared,Path.Combine(root,"ready"));progress("BIOS package prepared. Checking signatures and reading firmware…");return ready;
    }
}
