# RenewLedger · 续费账本

记录订阅、域名、服务器这类定期续费的小工具。使用 C# / .NET 10 和 Razor Pages，数据保存在本机 JSON 文件中。

## 运行

需要先装 .NET 10 SDK，然后在项目目录执行：

```sh
dotnet run --project RenewLedger.csproj --urls http://localhost:5080
```

浏览器打开 http://localhost:5080 。没有账号系统，只适合在本机用，不要直接暴露到公网。

在终端按 `Ctrl+C` 停止服务。服务关闭时不能访问账本，也不会发送提醒。

## 功能

- 增删改月付或年付项目，可以按名称搜索
- 保存失败时保留表单输入，输入错误显示在对应字段附近；文件读写错误单独提示
- 编辑、删除、续费和备份操作保留搜索与筛选条件
- 按币种分别统计月均支出，年付按 12 个月折算，不做汇率换算
- 列出未来 7 天（含今天）内到期的项目，以及已经过期的项目
- 手动确认续费后，到期日往后推一个周期，月末取目标月的最后一天
- JSON 导入导出，导入前需要确认，并会检查数据格式和重复 ID
- 账本为空时可以一键加载示例数据

## 数据存储

默认数据路径是应用内容目录下的 `data/ledger.json`。从项目目录运行时，就是项目里的 `data/ledger.json`。金额用 `decimal`，日期用 `DateOnly`。

可以用环境变量 `Ledger__Path` 指定其他位置。建议使用绝对路径，把长期数据放在程序目录以外。例如，在 PowerShell 中：

```powershell
$env:Ledger__Path = Join-Path $HOME 'RenewLedger/ledger.json'
dotnet run --project RenewLedger.csproj --urls http://localhost:5080
```

Linux shell 中可这样运行：

```sh
Ledger__Path="$HOME/.local/share/renewledger/ledger.json" \
  dotnet run --project RenewLedger.csproj --urls http://localhost:5080
```

**更换路径不会自动搬迁旧账本。** 请先导出旧账本，再在新位置导入；之后启动时使用相同的路径配置。

当前最多支持 1000 条记录。只允许一个应用进程管理同一个账本文件，不支持多个实例同时写入。不要在服务运行时手工编辑账本。

## 备份与恢复

### 正常账本

- 点击“导出”，将 JSON 备份保存在程序目录以外。
- 导入只接受 2 MB 以内、格式版本为 `1` 的备份，会检查必需字段、数据范围和重复 ID。
- **导入会替换整个账本，不会合并。** 先导出当前数据，再选择备份并勾选替换确认。
- 无效备份不会修改现有账本；成功导入后检查项目名称、币种、金额和到期日。

### 账本损坏

损坏账本会显示错误，应用不会将它清空，也不允许用页面导入直接覆盖它。导出损坏账本同样会失败。恢复步骤：

1. 按 `Ctrl+C` 停止服务，确认没有其他实例正在使用该文件。
2. 确认实际数据路径：设置了 `Ledger__Path` 时使用该路径，否则使用默认路径。
3. 将损坏文件重命名为其他文件名，例如 `ledger.corrupted-20261006.json`；保留原文件，不要删除，也不要覆盖已有的同名文件。
4. 使用原来的路径配置重新启动服务。原路径现在没有账本，页面会显示空账本。
5. 通过页面导入一份已知有效的 JSON 备份。若没有有效备份，先保留损坏文件，不能保证恢复全部记录。
6. 检查恢复结果，再导出一份新的备份。

读写失败也可能由权限、磁盘空间或文件被其他程序占用导致。先排查这些问题，不要把权限错误当成账本损坏而直接替换数据。

### 更换程序版本

升级前先导出备份并停止服务。如果仍使用默认数据目录，保留旧的 `data` 目录，不要随程序一起删除。新程序使用相同的数据路径，启动后检查账本是否完整。当前没有安装器、自动升级或自动升级备份。

## 测试

```sh
dotnet restore RenewLedger.sln
dotnet build RenewLedger.sln --configuration Release --no-restore
dotnet test tests/RenewLedger.Tests --configuration Release --no-build --no-restore
```

测试使用 xUnit，直接引用应用项目。规则与文件存储测试之外，`WebApplicationFactory` 会检查真实 Razor Pages 请求流程，包括防伪令牌、增删改、续费、上传导入、下载导出、输入保留和损坏文件保护。

每个测试实例使用独立临时数据目录，不读取或改写自用账本。CI 在 Windows 和 Linux 上执行构建和测试；HTTP 集成测试不等同于浏览器布局、安装包或干净环境验收。
