using System;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;

namespace JTJabba.EasyConfig
{
    [Generator]
    internal sealed class ConfigGenerator : ISourceGenerator
    {
        private static readonly DiagnosticDescriptor InvalidNodeWarning = new DiagnosticDescriptor(
            id: "EZCONF001",
            title: "Invalid node",
            messageFormat: "Invalid node '{0}'",
            category: "EasyConfig",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        // StringBuilders need to be cleared every time source is added.
        // Shouldn't be stateful but oh well.
        private readonly StringBuilder configSB = new StringBuilder();
        private readonly StringBuilder configLoaderSB = new StringBuilder();
        private GeneratorExecutionContext Context;
        private int configIndentLength = 0;

        private enum NodeType : ushort
        {
            Invalid,
            ArrayMember,
            Object,
            StringArray,
            ObjectArray,
            Int,
            Float,
            Bool,
            String
        }
        public void Initialize(GeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
#endif 
            Debug.WriteLine("Initialized code generator");
        }

        public void Execute(GeneratorExecutionContext context)
        {
            Context = context;
            var configFiles =
                from file in Context.AdditionalFiles
                where file.Path.EndsWith(".json")
                select file.Path;
            if (!configFiles.Any())
                return;

            var configBuilder = new ConfigurationBuilder();
            foreach (var configFile in configFiles)
            {
                if (File.Exists(configFile))
                    configBuilder.AddJsonFile(configFile);
            }
            Debug.WriteLine("Building config");
            IConfiguration config = configBuilder.Build();

            configLoaderSB.Append(
@"#nullable enable
using JTJabba.EasyConfig;
using Microsoft.Extensions.Configuration;

namespace JTJabba.EasyConfig.Loader
{
    public static class ConfigLoader
    {
        public delegate void OnFirstStaticLoadCallback();
        private static event OnFirstStaticLoadCallback? OnFirstStaticLoadEvent;
        private static object FirstStaticLoadCompletedLock = new Object();
        private static bool FirstStaticLoadCompleted = false;
        /// <summary>
        /// Will attempt to add a void returning callback with no parameters to an event invoked after the first load completes.
        /// If the first load is completed the callback will be invoked immediately. This method is threadsafe.
        /// </summary>
        public static void AddOnFirstStaticLoadCallback(OnFirstStaticLoadCallback callback)
        {
            lock (FirstStaticLoadCompletedLock)
            {
                if (FirstStaticLoadCompleted) callback();
                else OnFirstStaticLoadEvent += callback;
            }
        }
        /// <summary>
        /// Attempts to load files included in AdditionalFiles, then any additional files provided, then files in environment variable 'EasyConfigFiles'.
        /// Duplicate values will be overwritten.
        /// </summary>
        public static void Load(string[]? additionalConfigFiles = null)
        {
            var configBuilder = new ConfigurationBuilder();");
            foreach (var configFile in configFiles)
            {
                if (!configFile.Contains("template"))
                    configLoaderSB.Append($@"
            configBuilder.AddJsonFile(@""{configFile}"");");
            }
            configLoaderSB.Append(@"
            if (additionalConfigFiles != null)
                foreach (var filePath in additionalConfigFiles)
                    if (filePath.EndsWith("".json""))
                        configBuilder.AddJsonFile(filePath);
            var env = Environment.GetEnvironmentVariable(""EasyConfigFiles"")?.Split(',');
            if (env != null)
                foreach (var filePath in env)
                    if (filePath.EndsWith("".json""))
                        configBuilder.AddJsonFile(filePath);
            Load(configBuilder.Build());
        }
        /// <summary>
        /// Attempts to only load the passed configuration.
        /// </summary>
        public static void Load(IConfiguration config)
        {");

            configSB.Append(
@"namespace JTJabba.EasyConfig
{
    public static class Config
    {");

            foreach (var node in config.AsEnumerable())
            {
                var nodeType = GetNodeType(config, node);
                Debug.WriteLine("Handling node");
                Debug.WriteLine($"  Key:   {node.Key}");
                Debug.WriteLine($"  Type: {nodeType}");
                AddNodeToConfigSB(config, node.Key, nodeType);
                AddNodeToConfigLoaderSB(node.Key, nodeType);
            }

            configLoaderSB.Append(@"
            lock (FirstStaticLoadCompletedLock)
            {
                FirstStaticLoadCompleted = true;
            }
            OnFirstStaticLoadEvent?.Invoke();
            OnFirstStaticLoadEvent = null; // Remove hanging references");

            ReduceIndent(12, 0, configLoaderSB);
            context.AddSource("ConfigLoader.g.cs", configLoaderSB.ToString());
            ReduceIndent(configIndentLength, 0, configSB);
            context.AddSource("Config.g.cs", configSB.ToString());

            configSB.Clear();
            configLoaderSB.Clear();
            configIndentLength = 0;
        }
        void AddNodeToConfigSB(IConfiguration config, string nodeKey, NodeType nodeType, bool isStatic = true)
        {
            string typeModifier = isStatic ? "static " : "";

            if (nodeType == NodeType.Invalid)
            {
                HandleInvalidNode(nodeKey);
                return;
            }
            if (nodeType == NodeType.ArrayMember)
                return;

            string indent = GetNodeIndent(nodeKey);
            ReduceIndent(configIndentLength, indent.Length, configSB);
            configIndentLength = indent.Length;

            switch (nodeType)
            {
                case NodeType.Object:

                    configIndentLength = indent.Length + 4;
                    configSB.Append($@"
{indent}public {typeModifier}class {GetPropertyName(nodeKey)}
{indent}{{");
                    break;

                case NodeType.StringArray:

                    configSB.Append($@"
{indent}public {typeModifier}string[] {GetPropertyName(nodeKey)} {{ get; set; }}");
                    break;

                case NodeType.ObjectArray:

                    // Dictionary of property names to their type
                    var properties = new Dictionary<string, NodeType>();
                    NodeType propertyType;

                    // Populate dictionary with all properties that show up in all objects in the array
                    foreach (var section in config.GetSection(nodeKey).GetChildren())
                    {
                        foreach (var property in section.AsEnumerable(makePathsRelative: true))
                        {
                            propertyType = GetNodeType(section, property);

                            // Objects nested in objects in arrays is not supported
                            if (propertyType == NodeType.Object || propertyType == NodeType.ObjectArray)
                            {
                                HandleInvalidNode($"{nodeKey}:{section.Key}:{property.Key}");
                                continue;
                            }

                            // If property exists, make sure types match; else add property
                            if (properties.ContainsKey(property.Key))
                            {
                                if (properties[property.Key] != propertyType)
                                    HandleInvalidNode($"{nodeKey}:{section.Key}:{property.Key}");
                            }
                            else
                                properties[property.Key] = propertyType;
                        }
                    }

                    // Create new type that can hold all the properties that showed up in objects in the array
                    configSB.Append($@"
{indent}public class {GetPropertyName(nodeKey)}Object
{indent}{{");
                    configIndentLength += 4;

                    // Add all the properties we saw to the type
                    foreach (var property in properties)
                    {
                        AddNodeToConfigSB(config, $"{nodeKey}:{property.Key}", property.Value, isStatic: false);
                    }
                    // Add property for an array of this type to containing object
                    ReduceIndent(configIndentLength, configIndentLength - 4, configSB);
                    configIndentLength -= 4;
                    configSB.Append($@"
{indent}public {typeModifier}List<{GetPropertyName(nodeKey)}Object> {GetPropertyName(nodeKey)} {{ get; set; }} = new();");

                    break;

                default: // Handle simple values

                    configSB.Append($@"
{indent}public {typeModifier}{nodeType.ToString().ToLower()} {GetPropertyName(nodeKey)} {{ get; set; }}");
                    break;
            }
        }
        void AddNodeToConfigLoaderSB(string nodeKey, NodeType nodeType)
        {
            switch (nodeType)
            {
                case NodeType.Invalid:
                    HandleInvalidNode(nodeKey);
                    break;

                case NodeType.ArrayMember:
                    break;

                case NodeType.Object:
                    break;

                case NodeType.StringArray:
                    configLoaderSB.Append($@"
            {GetPropertyPath(nodeKey)} = config.GetSection(""{nodeKey}"").Get<string[]>();");
                    break;

                case NodeType.ObjectArray:
                    configLoaderSB.Append($@"
            foreach (var item in config.GetSection(""{nodeKey}"").GetChildren())
                {GetPropertyPath(nodeKey)}.Add(item.Get<{GetPropertyPath(nodeKey)}Object>());");
                    break;

                default:
                    configLoaderSB.Append($@"
            {GetPropertyPath(nodeKey)} = config.GetValue<{nodeType.ToString().ToLower()}>(""{nodeKey}"");");
                    break;
            }
        }
        static string GetNodeIndent(string nodeKey) =>
            new string(' ', 4 * (1 + nodeKey.Split(':').Length));
        static string GetPropertyName(string nodeKey) =>
            nodeKey.Split(':').Last().Replace('.', '_').Replace('-', '_').Replace(' ', '_');
        static string GetPropertyPath(string nodeKey) =>
            "Config." + nodeKey.Replace('.', '_').Replace(':', '.').Replace('-', '_').Replace(' ', '_');
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
        static NodeType GetNodeType(IConfiguration config, KeyValuePair<string, string> node)
        {
            // Check if ArrayMember
            for (int i = 1; i < node.Key.Length; i++)
                if (char.IsNumber(node.Key[i]) &&
                    node.Key[i - 1] == ':')
                    return NodeType.ArrayMember;

            // Handle cases where nested values
            if (node.Value == null)
            {
                var section = config.GetSection(node.Key);

                // Check if not array, no keys should be integers
                if (!section.GetChildren().Any(x => int.TryParse(x.Key, out _)))
                    return NodeType.Object;

                // If array, all keys should be integers
                if (section.GetChildren().Any(x => !int.TryParse(x.Key, out _)))
                    return NodeType.Invalid;

                // Check if not ObjectArray
                if (section.AsEnumerable().Count() - 1 == section.GetChildren().Count())
                    return NodeType.StringArray;

                return NodeType.ObjectArray;
            }

            if (int.TryParse(node.Value, out _)) return NodeType.Int;
            if (float.TryParse(node.Value, out _)) return NodeType.Float;
            if (bool.TryParse(node.Value, out _)) return NodeType.Bool;

            return NodeType.String;
        }
        void HandleInvalidNode(string key)
        {
            Context.ReportDiagnostic(Diagnostic.Create(InvalidNodeWarning, Location.None, key));
        }
    }
}