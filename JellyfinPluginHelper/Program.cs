#region

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

#endregion

Console.WriteLine("JPH - Jellyfin Plugin Helper starting");

// TODO fix ZIP path
// TODO detect manifest path

const string GitUser = "hexxone";
const string GitProject = "TeleJelly";
const string GitManifestBranch = "dist";
const string GitManifestPath = "manifest.json";

const string TargetAbi = "10.8.0.0";
const string ChangeMessage = "Automatic Release by Github Actions: ";

if (args.Length != 3)
{
    Console.WriteLine("JPH - Invalid arguments.\r\nUsage: jph <version> <projectDir> <dllPath>");
    Console.WriteLine("Actual arguments: " + string.Join(", ", args));
    return;
}


var version = args[0];
var projectDir = args[1];
var dllPath = args[2];

await Main(version, projectDir, dllPath);

static async Task Main(string version, string projectDir, string dllPath)
{
    // Assuming meta.json is in the project directory.
    var metaPath = Path.Combine(projectDir, "meta.json");

    Console.WriteLine("JPH - Working dir:     " + Directory.GetCurrentDirectory());
    Console.WriteLine("JPH - Using version:   " + version);
    Console.WriteLine("JPH - Using project:   " + projectDir);
    Console.WriteLine("JPH - Using dll path:  " + dllPath);
    Console.WriteLine("JPH - Using meta path: " + metaPath);

    if (!Directory.Exists(projectDir))
    {
        Console.WriteLine("JPH - Error: project dir does not exist.");
        return;
    }

    var dllFullPath = Path.Combine(projectDir, dllPath);
    if (!File.Exists(dllFullPath))
    {
        Console.WriteLine("JPH - Error: dll file does not exist.");
        return;
    }

    if (!File.Exists(metaPath))
    {
        Console.WriteLine("JPH - Error: meta file does not exist.");
        return;
    }

    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    UpdateMeta(metaPath, version, timestamp);

    var zipFilename = $"{GitProject}_v{version}.zip";
    var zipPath = Path.Combine(projectDir, zipFilename);
    var sourceUrl = $"https://github.com/{GitUser}/{GitProject}/releases/download/{version}/{zipFilename}";

    MakeZip(zipPath, new[] { dllFullPath, metaPath });

    var checksum = Md5Sum(zipPath);
    var manifestVersion = MakeManifestVersion(checksum, sourceUrl, version, timestamp);

    var manifestUrl = $"https://raw.githubusercontent.com/{GitUser}/{GitProject}/{GitManifestBranch}/{GitManifestPath}";
    var manifestTargetPath = Path.Combine(projectDir, "manifest.json");
    await AddManifestVersion(manifestUrl, manifestTargetPath, manifestVersion);

    Console.WriteLine("JPH - Done.");
}


static string Md5Sum(string filename)
{
    using var md5 = MD5.Create();
    using var stream = File.OpenRead(filename);
    var hash = md5.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
}

static string FixVersionString(string versionStr)
{
    if (!versionStr.Contains("-alpha.0"))
    {
        return versionStr;
    }

    versionStr = versionStr.Replace("-alpha.0", "");
    var parts = versionStr.Split('.');
    parts[2] = (int.Parse(parts[2]) - 1).ToString();
    return string.Join(".", parts);
}

static Dictionary<string, object> MakeManifestVersion(string checksum, string sourceUrl, string version, string timestamp)
{
    return new Dictionary<string, object>
    {
        ["targetAbi"] = TargetAbi,
        ["checksum"] = checksum,
        ["sourceUrl"] = sourceUrl,
        ["timestamp"] = timestamp,
        ["version"] = FixVersionString(version),
        ["changelog"] = $"{ChangeMessage} https://github.com/{GitUser}/{GitProject}/releases/tag/{version}"
    };
}

static async Task AddManifestVersion(string manifestUrl, string manifestTargetPath, Dictionary<string, object> manifestVersion)
{
    Console.WriteLine("JPH - Downloading and updating manifest.json.");

    using var webClient = new HttpClient();
    var manifestJson = await webClient.GetStringAsync(manifestUrl);
    var manifest = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(manifestJson);
    manifest[0]["versions"] = new List<Dictionary<string, object>> { manifestVersion };

    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(manifestTargetPath, json);

    Console.WriteLine("JPH - Successfully updated manifest.json.");
}

static void UpdateMeta(string metaPath, string version, string timestamp)
{
    Console.WriteLine("JPH - Updating meta.json.");

    // Read the existing meta.json
    string metaJson = File.ReadAllText(metaPath);
    var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson);

    if (meta == null)
    {
        Console.WriteLine("JPH - Failed to deserialize meta.json");
        return;
    }

    // Update the fields
    meta["timestamp"] = timestamp;
    meta["version"] = FixVersionString(version);
    meta["changelog"] = $"{ChangeMessage} https://github.com/{GitUser}/{GitProject}/releases/tag/{version}";

    // Serialize and write back to meta.json
    string updatedMetaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(metaPath, updatedMetaJson);

    Console.WriteLine("JPH - Successfully updated meta.json.");
}

static void MakeZip(string targetFile, string[] sourceFiles)
{
    if (File.Exists(targetFile))
    {
        Console.WriteLine("JPH - Recreating zip file.");
        File.Delete(targetFile);
    }
    else
    {
        Console.WriteLine("JPH - Creating zip file.");
    }

    // Ensure the directory for the target file exists
    var targetDirectory = Path.GetDirectoryName(targetFile);
    if (!string.IsNullOrWhiteSpace(targetDirectory) && !Directory.Exists(targetDirectory))
    {
        Directory.CreateDirectory(targetDirectory);
    }

    // Create a new zip archive or overwrite an existing one
    using (var zip = ZipFile.Open(targetFile, ZipArchiveMode.Create))
    {
        foreach (var sourceFilePath in sourceFiles)
        {
            // Avoid adding non-existing files to the zip
            if (File.Exists(sourceFilePath))
            {
                // Extract the filename and use it as the entry name to ensure files are at the top-level
                var entryName = Path.GetFileName(sourceFilePath);
                // Create an entry for each file
                zip.CreateEntryFromFile(sourceFilePath, entryName, CompressionLevel.Optimal);
            }
            else
            {
                Console.WriteLine($"JPH - Warning: File '{sourceFilePath}' not found and will be skipped.");
            }
        }
    }

    Console.WriteLine($"JPH - Successfully created zip file '{targetFile}'.");
}
