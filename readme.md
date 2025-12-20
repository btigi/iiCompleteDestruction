iiCompleteDestruction
=========

iiCompleteDestruction is a C# library targetting .NET10 providing basic functions to support modifications to Total Annihilation, the 1997 RTS by Cavedog Entertainment.

| Name   | Read | Write | Comment
|--------|:----:|-------|--------
| BOS    | ✗   |   ✗   | 
| CCX    | ✔   |   ✔   | HPI
| COB    | ✗   |   ✗   | 
| CRT    | ✗   |   ✗   | TA: Kingdoms
| FBI    | ✔   |   ✔   | TDF
| GAF    | ✔   |   ✔   | 
| GP3    | ✔   |   ✔   | HPI
| GUI    | ✔   |   ✔   | TDF
| HPI    | ✔   |   ✔   | 
| KMP    | ✔   |   ✔   | HPI, TA: Kingdoms
| OTA    | ✔   |   ✔   | TDF
| PCX    | ✔   |   ✗   | 
| SCT    | ✔   |   ✔   | 
| TAF    | ✗   |   ✗   | TA: Kingdoms
| TDF    | ✔   |   ✔   | TDF
| TNT    | ✔   |   ✔   |
| TNT2   | 〰️  |   〰️  | TA: Kingdoms
| TSF    | ✔   |   ✔   | TDF, TA: Kingdoms
| UFO    | ✔   |   ✔   | HPI

## Usage

Install via nuget:
https://www.nuget.org/packages/ii.CompleteDestruction

Sample code to use the library is provided below.

```csharp
  // HPI
  var hpi = new HpiProcessor();
  var files = hpi.Read(@"D:\games\ta\totala1.hpi");
  hpi.Write(@"D:\games\ta\totala1.out", files)


  // FBI, TDF, GUI, OTA
  var extensions = new string[] { ".fbi", ".tdf", ".gui", ".ota" };
  var directoryPath = @"D:\games\ta\extracted";
  foreach (var file in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
  {
      if (extensions.Contains(Path.GetExtension(file).ToLower()))
      { 
          definition = parser.Parse(file);
          Console.WriteLine($"{file} {definition.Blocks.First().SectionName}");
      }
  }


  // PCX
  var extensions = new string[] { ".pcx" };
  var directoryPath = @"D:\games\ta\extracted";
  PcxConverter pcxConverter = new();
  foreach (var file in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
  {
      try
      {
          if (extensions.Contains(Path.GetExtension(file).ToLower()))
          {
              var pcx = pcxConverter.Parse(file);
              var f = Path.GetFileNameWithoutExtension(file);
              pcx.SaveAsBmp($"{f}.bmp");
          }
      }
      catch (Exception ex)
      {
          Console.WriteLine($"Error processing {file}: {ex.Message}");
      }
  }  
```

## Download

Compiled downloads are not available.

## Compiling

To clone and run this application, you'll need [Git](https://git-scm.com) and [.NET](https://dotnet.microsoft.com/) installed on your computer. From your command line:

```
# Clone this repository
$ git clone https://github.com/btigi/iiCompleteDestruction

# Go into the repository
$ cd src

# Build  the app
$ dotnet build
```

## Licencing

iiCompleteDestruction is licensed under the MIT license. Full licence details are available in license.md

The HPI related code is largely based on original work by [JoeD](https://github.com/joe-d-cws/hpidump)

## References
https://units.tauniverse.com/tutorials/tadesign/tadesign/ta-files.htm

https://en.wikipedia.org/wiki/PCX

https://web.archive.org/web/20070428112624/http://www.whisqu.se/per/docs/graphics57.htm