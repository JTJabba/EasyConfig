using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JTJabba.EasyConfig
{
    [Generator]
    internal class ConfigGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            var configFiles =
                from file in context.AdditionalFiles
                where file.Path.EndsWith(".json")
                select file.Path;

            if (!configFiles.Any())
                return;

            var configBuilder = new ConfigurationBuilder();
            foreach (var configFile in configFiles)
            {
                configBuilder.AddJsonFile(configFile);
            }
            IConfiguration config = configBuilder.Build();

            var configLoaderSB = new StringBuilder();
            configLoaderSB.Append(
$@"using JTJabba.EasyConfig;
using Microsoft.Extensions.Configuration;

namespace JTJabba.EasyConfig.Loader
{{
    public static class ConfigLoader
    {{
        public static void Load()
        {{
            IConfiguration config = new ConfigurationBuilder()");

            foreach (var configFile in configFiles)
            {
                configLoaderSB.Append($@"
                .AddJsonFile(@""{configFile}"")");
            }
            configLoaderSB.Append($@"
                .Build();");

            var configSB = new StringBuilder();
            configSB.Append(
$@"namespace JTJabba.EasyConfig
{{
    public static class Config
    {{");
            int currentIndentLength = 8;
            string indent;
            foreach (var node in config.AsEnumerable())
            {
                if (int.TryParse(GetPropertyName(node), out _))
                    continue; //part of array or invalid name

                indent = GetNodeIndent(node);
                ReduceIndent(currentIndentLength, indent.Length, configSB);
                currentIndentLength = indent.Length;

                if (IsArray(config, node))
                {
                    configSB.Append($@"
{indent}public static string[] {GetPropertyName(node)};");
                    configLoaderSB.Append($@"
            {GetPropertyPath(node)} = config.GetSection(""{node.Key}"").Get<string[]>();");
                    continue;
                }
                if (node.Value == null) //nested values and not array
                {
                    currentIndentLength = indent.Length + 4;
                    configSB.Append($@"
{indent}public static class {GetPropertyName(node)}
{indent}{{");
                    continue;
                }
                if (bool.TryParse(node.Value, out _))
                {
                    configSB.Append($@"
{indent}public static bool {GetPropertyName(node)};");
                    configLoaderSB.Append($@"
            {GetPropertyPath(node)} = config.GetValue<bool>(""{node.Key}"");");
                    continue;
                }
                if (int.TryParse(node.Value, out _))
                {
                    configSB.Append($@"
{indent}public static int {GetPropertyName(node)};");
                    configLoaderSB.Append($@"
            {GetPropertyPath(node)} = config.GetValue<int>(""{node.Key}"");");
                    continue;
                }
                configSB.Append($@"
{indent}public static string {GetPropertyName(node)};");
                configLoaderSB.Append($@"
            {GetPropertyPath(node)} = config.GetValue<string>(""{node.Key}"");");
            }

            ReduceIndent(12, 0, configLoaderSB);
            context.AddSource("ConfigLoader.g.cs", configLoaderSB.ToString());
            ReduceIndent(currentIndentLength, 0, configSB);
            context.AddSource("Config.g.cs", configSB.ToString());
        }
        public void Initialize(GeneratorInitializationContext context)
        {

        }
        static string GetNodeIndent(KeyValuePair<string, string> node) =>
            new string(' ', 4 * (1 + node.Key.Split(':').Length));
        static string GetPropertyName(KeyValuePair<string, string> node) =>
            node.Key.Split(':').Last().Replace('.', '_').Replace('-', '_').Replace(' ', '_');
        static string GetPropertyPath(KeyValuePair<string, string> node) =>
            "Config." + node.Key.Replace('.', '_').Replace(':', '.').Replace('-', '_').Replace(' ', '_');
        static void ReduceIndent(int oldIndent, int newIndent, StringBuilder stringBuilder)
        {
            while (newIndent < oldIndent)
            {
                oldIndent -= 4;
                stringBuilder.AppendLine();
                stringBuilder.Append(' ', oldIndent);
                stringBuilder.Append('}');
            }
        }
        static bool IsArray(IConfiguration config, KeyValuePair<string, string> node)
        {
            var section = config.GetSection(node.Key).GetChildren().AsEnumerable();
            if (!section.Any())
                return false;
            foreach (var item in section)
            {
                if (!int.TryParse(item.Key, out _))
                    return false;
            }
            return true;
        }
    }
}