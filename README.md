# Crashday Bitmap FileType
A Paint.NET file type plugin that adds support for opening Crashday Self-Playing Demo `.cbm` textures

<img src="https://github.com/fxkCD/Crashday-Bitmap-FileType/blob/main/scr/1.png"/>
<img src="https://github.com/fxkCD/Crashday-Bitmap-FileType/blob/main/scr/2.png"/>

## Compatibility
This plugin is built with the Paint.NET 4.3.12 API and tested on Paint.NET 5.1.9

Required version: Paint.NET 4.3 or newer

## Installation
Build or download `.dll` from the releases and put it into:
**\paint.net\FileTypes** - for default version and installation for all users
**C:\Users\username\Documents\paint.net App Files** - for Microsoft Store version or installation for local user

**Crashday Bitmap (`*.cbm`)** will appear in the Open and Save dialogs. You can also open them directly

## Limitations
- CBM images use an indexed palette with a maximum of 256 colors
- Saving may slightly change colors because the image is converted to the CBM palette format
- Dimensions must be `4`, `8`, `16`, `32`, `64`, `128`, or `256` pixels

## Building

### Requirements

- .NET SDK
- Paint.NET 4.3.12 API assemblies (you can just download Paint.NET 4.3.12 Portable)

The following Paint.NET assemblies are required:

```text
PaintDotNet.Base.dll
PaintDotNet.Core.dll
PaintDotNet.Data.dll
```

Run the following command from the repository root:

```text
dotnet build CBMFileType.csproj -c Release -p:PaintDotNetPath="Directory with Paint.NET 4.3.12 API assemblies (for example D:\Tools\paint.net-4.3.12)"
```
Compiled `.dll` will appear at **bin\Release\net6.0-windows**

