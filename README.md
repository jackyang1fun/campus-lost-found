# Lost and Found System

Windows 10/11 x64 单机离线失物招领桌面应用，使用 WPF、.NET 10 和 SQLite。

## 当前状态

- 需求规格：V2.1-final，已审定
- 软件架构：V1.1-final，已审定
- 实现阶段：可运行 MVP
- 数据库：SQLite，无需额外安装数据库服务器
- 自动化测试：33 项通过
- 发布形式：Windows x64 自包含单文件程序

## 目录

- `docs/01-requirements/`：需求规格、风险矩阵和阶段 2 对话记录
- `docs/02-architecture/`：架构上下文、评审记录、阶段 3 精化和最终架构
- `LostAndFound.Desktop/`：WPF 单窗口前端
- `LostAndFound.App/`：领域、业务服务与 SQLite 基础设施
- `LostAndFound.Tests/`：自动化测试
- `tools/DemoAccountSeeder/`：演示账号与示例数据生成工具

## 已实现功能

- 普通用户注册、登录、个人资料、修改密码和账号注销
- 本地模拟校园卡认证与校园卡号唯一性校验
- 失物/招领发布、图片与替代文本、编辑、撤销和标记解决
- 游客和登录用户的信息浏览、关键词搜索及分类筛选
- 基于分类、地点、描述和时间的基础关联匹配
- 认领发起、匿名聊天、取消认领和交接完成
- 管理员审核、内容删除、用户封禁和操作日志
- 争议提交、管理员敏感访问审计及争议结论
- 每日自动备份、手动备份、完整性检查和恢复保护
- 键盘操作、屏幕阅读器语义、明显焦点和高对比度主题

## 开发命令

```powershell
dotnet restore .\LostAndFound.slnx
dotnet build .\LostAndFound.slnx
dotnet test .\LostAndFound.slnx --no-build
dotnet run --project .\LostAndFound.Desktop\LostAndFound.Desktop.csproj
```

## 自包含发布

```powershell
dotnet publish .\LostAndFound.Desktop\LostAndFound.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

发布后的程序可复制到其他 Windows 10/11 x64 电脑独立运行。每份程序使用自己的本地数据，不跨电脑同步。

当前已生成发布包：

- `output/LostAndFound-v1.1-win-x64/`
- `output/LostAndFound-v1.1-win-x64.zip`

## 演示账号

桌面文件 `LostAndFound测试账号.txt` 保存当前演示环境的管理员和普通用户凭据。

演示账号使用独立的 `demo-data/` 数据库，不与开发调试数据库混用。VSCode 调试时选择
`Launch LostAndFound (WPF demo accounts)` 即可使用已生成的测试账号。

如需重新生成演示账号：

```powershell
dotnet run --project .\tools\DemoAccountSeeder\DemoAccountSeeder.csproj -- .\demo-data
```
