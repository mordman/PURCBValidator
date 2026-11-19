using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

// Измерение памяти при построении схем ЦБ.

var schemasRoot = Path.GetFullPath(Path.Combine(
    Directory.GetCurrentDirectory(), "src", "PurcbValidator.Core", "Resources", "cbr_schemas"));

long M(bool force) => Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;

Console.WriteLine($"старт: RSS={M(false)} МБ");
var registry = new SchemaRegistry();
var bo = new BuildOptions { SchemaRegistry = registry };
JsonSchema? entry = null;
int n = 0;
foreach (var file in Directory.EnumerateFiles(schemasRoot, "*.def.json", SearchOption.AllDirectories))
{
    var s = JsonSchema.FromText(File.ReadAllText(file), bo);
    registry.Register(s);
    if (file.EndsWith("ep_nso_purcb_oper_nr_uod_reestr.def.json")) entry = s;
    n++;
    Console.WriteLine($"  после {Path.GetFileName(file)} ({n}/16): RSS={M(false)} МБ");
}
Console.WriteLine($"все схемы построены: RSS={M(false)} МБ");

// оценка точки входа на примере ЦБ
var mapEl = JsonSerializer.SerializeToElement(JsonNode.Parse(
    File.ReadAllText("samples/example_package/mapping.json")));
var opts = new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, RequireFormatValidation = false };
var sw = Stopwatch.StartNew();
var r = entry!.Evaluate(mapEl, opts);
sw.Stop();
Console.WriteLine($"Evaluate(example): valid={r.IsValid}, {sw.ElapsedMilliseconds} мс, RSS={M(false)} МБ");
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
Console.WriteLine($"после GC.Collect: RSS={M(false)} МБ, managed={GC.GetTotalMemory(true)/1024/1024} МБ");
