# EasyConfig
Source generator that automatically generates static classes for accessing json configs.
- [How to Use](#how-to-use)
- [Generated Code](#generated-code)
- [Limitations](#limitations)
## How to Use

### Step 1 - Install
Add a package reference to your project. The package is available on Nuget or you can build from source.
```xml
<ItemGroup>
    <PackageReference Include="JTJabba.EasyConfig" Version="1.0.0" />
</ItemGroup>
```

### Step 2 - Add config files and templates to csproj file
- Currently only Json files are supported.
- These files will be directly loaded unless they are marked as templates by including ```template``` in their name.
- Template files should contain an example json config containing values EasyConfig expects to be findable somewhere else on run.
- Do not include paths to files not accessible on compilation. Add templates to generate a structure for their values and see Step 3.
```xml
<ItemGroup>
    <!-- IMPORTANT: MSBuild now recognizes these as analyzer files, and will not copy them to the output directory unless specified-->
    <AdditionalFiles Include="appsettings.json" CopyToOutputDirectory="Always" />
    <AdditionalFiles Include="template.secrets.json" />
</ItemGroup>
```

### Step 3 - Load config at start of program
Make a call to ConfigLoader.Load. Paths to config files not included in AdditionalFiles can be passed as a string array, a manually created IConfiguration can be loaded, or file paths can be specified in the environment variable ```EasyConfigFiles``` as a comma-delimited string.
Duplicate values are overwritten as higher-priority files are loaded. The load order (lowest to highest priority) is AdditionalFiles, file paths passed to Load(), then file paths in an ```EasyConfigFiles``` environment variable.
```csharp
using JTJabba.EasyConfig.Loader;

// METHOD 1
// Only load non-template files included in AdditionalFiles then any files specified in an EasyConfigFiles environment variable.
ConfigLoader.Load();

// METHOD 2
// Load non-template files included in AdditionalFiles, then matching config values from passed in files, then any files specified in an EasyConfigFiles environment variable.
var otherFiles = new string[] { "/run/secrets/secrets.json" };
ConfigLoader.Load(otherFiles);

// METHOD 3
// Load a user provided IConfiguration. Environment variable EasyConfigFiles is ignored.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
var myConfig = new ConfigurationBuilder()
    .AddJsonFile(@"appsettings.json")
    .AddJsonFile(@"/run/secrets/secrets.json")
    .Build();
ConfigLoader.Load(myConfig);
```

### Step 4 - Access config values
```csharp
using JTJabba.EasyConfig;

// ACCESS BASIC PROPERTY
var mySetting = Config.Path.To.Setting;

// QUERY OBJECT ARRAY
// Get list of subnets for an object in an object array where its RegionName = regionName
var myRegion = "us-east-1";
var myRegionsSubnets = Config.AWS.Regions.Where(x => x.RegionName == regionName).First().Subnets.ToList();
```

## Generated code
For the given project:
```xml
// Somewhere in .csproj file
<ItemGroup>
    <PackageReference Include="JTJabba.EasyConfig" Version="1.0.0" />
    <AdditionalFiles Include="appsettings.json" />
    <AdditionalFiles Include="secrets.template.json" />
</ItemGroup>
```
```json
// appsettings.json
{
  "Listening_Port": 12345,
  "Max_Parallel_Reservations": 20,
  "Reservation_Timeout_Minutes": 90,
  "Enable_Timeout": true,
  "Cert_Path": "/run/secrets/MyCert.pfx",
  "AWS": {
    "Ami": "ami-123456789abcdef00",
    "Cache_Timeout": 300,
    "Regions": [
      {
        "RegionName": "us-east-1",
        "Security_Groups": [
          "sg-123456789abcdef00"
        ],
        "Subnets": [
          "subnet-123456789abcdef00",
          "subnet-123456789abcdef01",
          "subnet-123456789abcdef02"
        ]
      }
    ]
  }
}
```
```json
// template.secrets.json
{
  "Secrets": {
    "Cert_Import_Key": "",
    "AWS_Access_Key": "",
    "AWS_Secret_Key": ""
  }
}
```

EasyConfig will generate the following two files, viewable under Dependancies->Analyzers->EasyConfig->JTJabba.EasyConfig.ConfigGenerator:
```csharp
namespace JTJabba.EasyConfig
{
    public static class Config
    {
        public static class Secrets
        {
            public static string Cert_Import_Key { get; set; }
            public static string AWS_Secret_Key { get; set; }
            public static string AWS_Access_Key { get; set; }
        }
        public static int Reservation_Timeout_Minutes { get; set; }
        public static int Max_Parallel_Reservations { get; set; }
        public static int Listening_Port { get; set; }
        public static bool Enable_Timeout { get; set; }
        public static string Cert_Path { get; set; }
        public static class AWS
        {
            public class RegionsObject
            {
                public string[] Subnets { get; set; }
                public string[] Security_Groups { get; set; }
                public string RegionName { get; set; }
            }
            public static List<RegionsObject> Regions { get; set; } = new();
            public static int Cache_Timeout { get; set; }
            public static string Ami { get; set; }
        }
    }
}
```
```csharp
#nullable enable
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
        /// Will attempt to add a void returning callback with no parameters to an event called after the first load completes.
        /// If the first load is completed the callback will be called immediately. This method is threadsafe.
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
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(@"Path\To\Project\appsettings.json");
            if (additionalConfigFiles != null)
                foreach (var filePath in additionalConfigFiles)
                    if (filePath.EndsWith(".json"))
                        configBuilder.AddJsonFile(filePath);
            var env = Environment.GetEnvironmentVariable("EasyConfigFiles")?.Split(',');
            if (env != null)
                foreach (var filePath in env)
                    if (filePath.EndsWith(".json"))
                        configBuilder.AddJsonFile(filePath);
            Load(configBuilder.Build());
        }
        /// <summary>
        /// Attempts to only load the passed configuration.
        /// </summary>
        public static void Load(IConfiguration config)
        {
            Config.Secrets.Cert_Import_Key = config.GetValue<string>("Secrets:Cert_Import_Key");
            Config.Secrets.AWS_Secret_Key = config.GetValue<string>("Secrets:AWS_Secret_Key");
            Config.Secrets.AWS_Access_Key = config.GetValue<string>("Secrets:AWS_Access_Key");
            Config.Reservation_Timeout_Minutes = config.GetValue<int>("Reservation_Timeout_Minutes");
            Config.Max_Parallel_Reservations = config.GetValue<int>("Max_Parallel_Reservations");
            Config.Listening_Port = config.GetValue<int>("Listening_Port");
            Config.Enable_Timeout = config.GetValue<bool>("Enable_Timeout");
            Config.Cert_Path = config.GetValue<string>("Cert_Path");
            foreach (var item in config.GetSection("AWS:Regions").GetChildren())
                Config.AWS.Regions.Add(item.Get<Config.AWS.RegionsObject>());
            Config.AWS.Cache_Timeout = config.GetValue<int>("AWS:Cache_Timeout");
            Config.AWS.Ami = config.GetValue<string>("AWS:Ami");
            lock (FirstStaticLoadCompletedLock)
            {
                FirstStaticLoadCompleted = true;
            }
            OnFirstStaticLoadEvent?.Invoke();
            OnFirstStaticLoadEvent = null; // Remove hanging references
        }
    }
}
```
## Limitations
- No support yet for loading config into an object (high priority).
- Thread safety isn't implemented and properties aren't locked if config is reloaded.
- Only arrays of objects and string are supported.
- Keys must not begin with a number, and only contain alphanumerics, periods, dashes, underscores, and spaces.
- Objects in an object array cannot contain nested objects or object arrays.
- Generator assumes types - Ex. if you want floats supported for a type after compilation you'd have to compile it with the type in float form.
- Objects in object arrays can contain fields not in other objects, but one type is created for the entire array with all the fields it saw and identical fields must have matching types.
