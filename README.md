# (|>) M sharP3

A MPEG 1 Layer III aka MP3 decoder programmed in F# language. The primary motivation for this project is to know how the MP3 algorithm works and get hands on experience on F# language. Plus I'm curious about how .NET framework performs on this particular resource intensive workload.

## Block Diagram

## Current status
|Task|Status|
|:----:|:------:|
|Parse Frame header| **DONE**|
|Parse Frame Side Information| **DONE**|
|Parse Main Data |**DONE**|
|Generate Samples from Main Data| **PENDING**|
|Metadata handling| **PENDING**|
|Handling proper mp3 files| **PENDING**|

## Build instructions
### Windows
**Method 1**: You need [Visual F# SDK 4.0](https://www.microsoft.com/en-us/download/details.aspx?id=48179)
 and GNU Make (either from cygwin or MinGW) for building this project. A Makefile has been provided in the FSharpMP3 directory. To build just `cd` to the directory and type `make all` or `make debug` to build. 

**Method 2**: You can also use Visual Studio 2017.

### Linux (Untested)
**Method 1**: Get Mono and GNU Make from your distributions package repository. Then follow Method 1 for windows.

**Method 2**: Get .NET core tools for linux and GNU Make. cd to the FSharpMP3 directory and type `make msbuild` to build.

## References
* [ISO 11172:3](https://www.iso.org/standard/22412.html)
* [The Theory behind MP3](www.mp3-tech.org%2Fprogrammer%2Fdocs%2Fmp3_theory.pdf)
* [Let's build an MP3 decoder](http://blog.bjrn.se/2008/10/lets-build-mp3-decoder.html)
* [MP3 Decoding in C++](http://www.fcreyf.com/11114/mp3-decoding-in-c++)
* [MP3Sharp](https://github.com/ZaneDubya/MP3Sharp)