# RenewLedger · 续费账本

记录订阅、域名、服务器这类定期续费的小工具。

## 运行

需要先装 .NET 10 SDK，然后在项目目录执行：

```sh
dotnet run --project RenewLedger.csproj --urls http://localhost:5080
```

浏览器打开 http://localhost:5080 。没有账号系统，只适合在本机用，不要直接暴露到公网。

## 功能

- 增删改月付或年付项目，可以按名称搜索
- 按币种分别统计月均支出，年付按 12 个月折算，不做汇率换算
- 列出未来 7 天（含今天）内到期的项目，以及已经过期的项目
- 手动确认续费后，到期日往后推一个周期，月末取目标月的最后一天
- JSON 导入导出，导入前需要确认，并会检查数据格式和重复 ID
- 账本为空时可以一键加载示例数据

## 数据存储

数据存在 `data/ledger.json`，可以用环境变量 `Ledger__Path` 改路径。金额用 `decimal`，日期用 `DateOnly`。

## 测试

```sh
dotnet build RenewLedger.csproj --configuration Release
dotnet run --project tests/RenewLedger.Tests --configuration Release
```
