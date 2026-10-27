using System.Text.Json.Serialization;

namespace StudioUtil.Testing;

// Keep the existing PascalCase wire/cache schema, independently of JsonUtil's web defaults.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkerMessage))]
[JsonSerializable(typeof(TestNodeUpdate))]
[JsonSerializable(typeof(TestSessionSnapshot))]
internal partial class WorkerJsonContext : JsonSerializerContext;
