# Card.Contracts

协议与配表形状。`netstandard2.1`，Unity 与服务端共用。编译后随 `Card.Battle` 拷到 `Card/Assets/Plugins/Card`。

允许：HTTP/WS DTO、错误码、配表行、`IGameTables`。  
不允许：对局规则、出伤计算、仓储、Unity / ASP.NET。

`Config/` 下由导表生成，不要手改。

计算与编码规则见 [`../Card.Battle/README.md`](../Card.Battle/README.md)。
