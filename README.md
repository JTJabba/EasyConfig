# EasyConfig
Source generator that automatically generates static classes for accessing json configs.

## How to use

### Step 1 - Install
Build a package from source or install JTJabba.EasyConfig from nuget.org.

### Step 2 - Add config files to csproj file
**Example 1:**
```xml
<ItemGroup>
    <AdditionalFiles Include="appsettings.json" />
</ItemGroup>
```

**Example 2 (docker):**
```xml
<ItemGroup>
    <AdditionalFiles Include="/app/appsettings.json" />
    <AdditionalFiles Include="/run/secrets/secrets.json" />
</ItemGroup>
```

### Step 3 - Load at start of program
```csharp
using JTJabba.EasyConfig.Loader;
ConfigLoader.Load();
```

### Step 4 - Access config values
```csharp
using JTJabba.EasyConfig;
var mySetting = Config.Path.To.Setting;
```

## Generated code
For the given .json included in a project:
```json
{
  "Listening_Port": 12345,
  "Max_Parallel_Reservations": 20,
  "Reservation_Timeout_Minutes": 90,
  "Enable_Timeout": true,
  "Cert_Path": "/run/secrets/MyCert.pfx",
  "EC2Config": {
    "Ami": "ami-123456789abcdef00",
    "Cache_Timeout": 300,
    "Region_Configs": {
      "us-east-1": {
        "Security_Groups": [
          "sg-123456789abcdef00"
        ],
        "Subnets": [
          "subnet-123456789abcdef00",
          "subnet-123456789abcdef01",
          "subnet-123456789abcdef02"
        ]
      }
    }
  }
}
```
EasyConfig will generate the 2 following files, viewable under Dependancies->Analyzers->EasyConfig->JTJabba.EasyConfig.ConfigGenerator:
```csharp
namespace JTJabba.EasyConfig
{
    public static class Config
    {
        public static int Reservation_Timeout_Minutes;
        public static int Max_Parallel_Reservations;
        public static class Logging
        {
            public static class LogLevel
            {
                public static string Microsoft_Hosting_Lifetime;
                public static string Default;
            }
        }
        public static int Listening_Port;
        public static bool Enable_Timeout;
        public static class EC2Config
        {
            public static class Region_Configs
            {
                public static class us_east_1
                {
                    public static string[] Subnets;
                    public static string[] Security_Groups;
                }
            }
            public static int Cache_Timeout;
            public static string Ami;
        }
        public static string Cert_Path;
    }
}
```
```csharp
using JTJabba.EasyConfig;
using Microsoft.Extensions.Configuration;

namespace JTJabba.EasyConfig.Loader
{
    public static class ConfigLoader
    {
        public static void Load()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile(@"Path\To\Project\appsettings.Development.json")
                .AddJsonFile(@"Path\To\Project\appsettings.json")
                .Build();
            Config.Reservation_Timeout_Minutes = config.GetValue<int>("Reservation_Timeout_Minutes");
            Config.Max_Parallel_Reservations = config.GetValue<int>("Max_Parallel_Reservations");
            Config.Logging.LogLevel.Microsoft_Hosting_Lifetime = config.GetValue<string>("Logging:LogLevel:Microsoft.Hosting.Lifetime");
            Config.Logging.LogLevel.Default = config.GetValue<string>("Logging:LogLevel:Default");
            Config.Listening_Port = config.GetValue<int>("Listening_Port");
            Config.Enable_Timeout = config.GetValue<bool>("Enable_Timeout");
            Config.EC2Config.Region_Configs.us_east_1.Subnets = config.GetSection("EC2Config:Region_Configs:us-east-1:Subnets").Get<string[]>();
            Config.EC2Config.Region_Configs.us_east_1.Security_Groups = config.GetSection("EC2Config:Region_Configs:us-east-1:Security_Groups").Get<string[]>();
            Config.EC2Config.Cache_Timeout = config.GetValue<int>("EC2Config:Cache_Timeout");
            Config.EC2Config.Ami = config.GetValue<string>("EC2Config:Ami");
            Config.Cert_Path = config.GetValue<string>("Cert_Path");
        }
    }
}
```
