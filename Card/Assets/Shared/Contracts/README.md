# Card.Contracts

协议与配表形状。源码与 Unity 客户端共享（ET 式）：本目录即客户端 `Assets/Shared/Contracts`，服务端 `Server/src/Card.Contracts`（`netstandard2.1`）用 `<Compile Include>` 链入同一份源文件，两端各自编译，不走 DLL。

允许：HTTP/WS DTO、错误码、配表行、`IGameTables`。  
不允许：对局规则、出伤计算、仓储、Unity / ASP.NET。

`Config/` 下由导表生成，不要手改。生成的表类继承 `ConfigTableBase.cs` 里的 `ConfigRowBase<T>` / `ConfigConstBase<T>`，自带静态注册表（`Get(id)` / `TryGet` / `All` / `Instance`）：客户端由 `ConfigTables.LoadAsync` 灌数据，服务端由 `FileGameConfigLoader` 灌数据，JSON 同为 `Card/Assets/Res/Config`。

计算与编码规则见 [`../Battle/README.md`](../Battle/README.md)。
