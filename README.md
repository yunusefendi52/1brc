# One Billion Row Challenge

https://github.com/gunnarmorling/1brc in dotnet

### On my M1 Mac

|Rows|Results|Changes|
|---|---|---|
|10_000_000|634 ms|-|
|50_000_000|6.5 seconds|-|
|50_000_000|5.5-6 seconds|-|
|50_000_000|3.8-4.2 seconds|add csFastFloat|

### Benchmark

```
dotnet trace collect --format speedscope -- bin/Release/net8.0/publish/1brc
```