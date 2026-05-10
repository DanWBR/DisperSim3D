using System.IO;
using System.Text.Json;

namespace DisperSim3D.Validation
{
    public static class BenchmarkLoader
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public static BenchmarkSpec Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Benchmark file not found", path);
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BenchmarkSpec>(json, Options);
        }

        public static void Save(BenchmarkSpec spec, string path)
        {
            string json = JsonSerializer.Serialize(spec, Options);
            File.WriteAllText(path, json);
        }
    }
}
