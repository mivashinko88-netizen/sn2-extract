using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;



const string PakDir   = @"C:\Program Files\Epic Games\Subnautica2\Subnautica2\Content\Paks";
const string UsmapPath = @"C:\Users\mivas\Desktop\sn2-extract\mappings\Subnautica2-build23446003.usmap";
const string AesKey   = "0x0000000000000000000000000000000000000000000000000000000000000000";

var provider = new DefaultFileProvider( // make the object that knows how to open SN2 contrainers
    PakDir,
    SearchOption.AllDirectories,
    isCaseInsensitive: true,
    new VersionContainer(EGame.GAME_UE5_6));

provider.Initialize();                                      // mount the IoStore + pak
provider.SubmitKey(new FGuid(), new FAesKey(AesKey));       // zero key (unencrypted)
provider.MappingsContainer = new FileUsmapTypeMappingsProvider(UsmapPath);  // the usmap

// ---- helper: find a file (case-insensitive) and return its exports as JSON ----
JArray? Load(string folder, string exactName, string prefix)
{
    var cands = provider.Files.Keys
        .Where(k => k.Contains(folder, StringComparison.OrdinalIgnoreCase))
        .Where(k => Path.GetFileNameWithoutExtension(k)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .ToList();

    var pick = cands.FirstOrDefault(k =>
                    Path.GetFileNameWithoutExtension(k)
                        .Equals(exactName, StringComparison.OrdinalIgnoreCase))   // exact wins
                ?? (cands.Count == 1 ? cands[0] : null);                          // else unique-only

    if (pick == null) return null;
    try { return JArray.Parse(JsonConvert.SerializeObject(provider.LoadPackage(pick).GetExports())); }
    catch { return null; }
}

  // pull Properties.<prop> from the first export that has it
JToken? Prop(JArray? exports, string prop)
{
    if (exports == null) return null;
    foreach (var e in exports)
        if (e["Properties"]?[prop] is JToken t) return t;
    return null;
}

  // FText -> its English string
string? Text(JToken? t) => t?["SourceString"]?.ToString();

  // ---- roster: all fauna from ScanData/Fauna (drop test rows) ----

var roster = provider.Files.Keys
    .Where(k => k.Contains("/ScanData/Fauna/DA_") && k.Contains("_ScanData"))
    .Select(k => Regex.Replace(Path.GetFileNameWithoutExtension(k),
                                 "^DA_|_ScanData\\d*$", "", RegexOptions.IgnoreCase))
    .Where(n => !n.Contains("Test", StringComparison.OrdinalIgnoreCase))
    .Distinct()
    .OrderBy(n => n)
    .ToList();
Console.WriteLine($"Roster: {roster.Count} creatures");

  // ---- build one clean record per creature ----
var creatures = new List<object>();
foreach (var name in roster)
{
    var bio = Load("/BioScans/",          $"DA_{name}_BioScanData",      $"DA_{name}");
    var dbk = Load("/DatabankEntry/",     $"DA_{name}_DatabankEntry",    $"DA_{name}");
    var scn = Load("/ScanData/Fauna/",    $"DA_{name}_ScanData",         $"DA_{name}");
    var sts = Load("/InitialAttributes/", $"GE_{name}InitialAttributes", $"GE_{name}");
    var abl = Load("/AbilitySets/",       $"DA_{name}AbilitySet",        $"DA_{name}");

        // stats: flatten the GAS modifiers into { AttributeName: value }
    var stats = new Dictionary<string, object?>();
    if (Prop(sts, "Modifiers") is JArray mods)
        foreach (var m in mods)
        {
            var attr = m["Attribute"]?["AttributeName"]?.ToString();
            var val  = m["ModifierMagnitude"]?["ScalableFloatMagnitude"]?["Value"];
            if (attr != null && val != null && !stats.ContainsKey(attr))
                stats[attr] = (double?)val;
        }

        // categories: list of the category strings
    var categories = (Prop(dbk, "Categories") as JArray)?
        .Select(c => Text(c)).Where(s => s != null).ToList();

        // abilities: pull the readable name out of each asset path
    var abilities = (Prop(abl, "GrantedAbilities") as JArray)?
        .Select(a => a["AssetPathName"]?.ToString()?.Split('.').Last())
        .Where(s => s != null)
        .Select(s => s!.EndsWith("_C") ? s[..^2] : s)   // trim trailing _C
        .ToList();

    creatures.Add(new
    {
        id           = name,
        name         = Text(Prop(scn, "Name")) ?? Text(Prop(dbk, "EntryTitle"))
                           ?? Text(Prop(bio, "BioScanName")) ?? name,
        categories,
        description  = Text(Prop(dbk, "EntryText")),
        scanDuration = (double?)Prop(scn, "ScanDuration"),
        stats,
        abilities,
    });
    Console.WriteLine($"  {name}");
}

    // ---- write the catalog ----
var outJson = JsonConvert.SerializeObject(creatures, Formatting.Indented);
File.WriteAllText(@"C:\Users\mivas\Desktop\sn2-extract\creatures.json", outJson);
Console.WriteLine($"Wrote {creatures.Count} creatures to creatures.json");
