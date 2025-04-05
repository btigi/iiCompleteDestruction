iiTotalAnnihilation
=========

iiTotalAnnihilation is a C# library targetting .NET8 providing basic functions to support modifications to Total Annihilation, the 1997 RTS by Cavedog Entertainment.
The library currently supports dumping the contents of HPIs and related archives.

## Usage

Sample code to use the library is provided below.

```
  var dumper = new HpiDumper();
  dumper.Process(hpiName, outDir);
```

## Download

Compiled downloads are not available.

## Compiling

To clone and run this application, you'll need [Git](https://git-scm.com) and [.NET](https://dotnet.microsoft.com/) installed on your computer. From your command line:

```
# Clone this repository
$ git clone https://github.com/btigi/iiTotalAnnihilation

# Go into the repository
$ cd src

# Build  the app
$ dotnet build
```

iiTotalAnnihilation is largely based on original work by [JoeD](https://github.com/joe-d-cws/hpidump)