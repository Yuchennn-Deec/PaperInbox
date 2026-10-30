# Zotero 10 兼容更新 · 0.5.1

2026-10-30。Windows EXE 仍为 0.5.0，连接插件更新到 0.5.1。

已有程序只需[下载新连接插件](https://github.com/Yuchennn-Deec/PaperInbox/raw/refs/heads/main/releases/v0.5.1/PaperInbox-Zotero-Bridge-0.5.1.xpi)，在 Zotero → 工具 → 插件 → 齿轮菜单 → 从文件安装插件，选择该 XPI，按提示重启。原 EXE 与现有 API 设置、Prompt 可继续使用。

- [完整运行包](https://github.com/Yuchennn-Deec/PaperInbox/raw/refs/heads/main/releases/v0.5.1/PaperInbox-0.5.1.zip)
- [源码 ZIP](https://github.com/Yuchennn-Deec/PaperInbox/raw/refs/heads/main/releases/v0.5.1/PaperInbox-0.5.1-source.zip)
- [SHA256 校验文件](SHA256SUMS.txt)
- [本次验证记录](verification.json)

修复 Zotero 9 上限导致的禁用，并为 Zotero 10 使用 `pdf.getFulltext` 命令；Zotero 9 保留 `getFulltext`。分析前不导入临时附件。

本次 60 项检查通过，在实际 Zotero 10.0.6 独立库验证安装、PDF 提取、Prompt 分类、查重和自动打开。未调用真实付费 API、未在正式文库创建测试条目。Zotero 9 分支本轮使用回归测试验证。

官方变更参考：[Zotero 10 for Developers](https://www.zotero.org/support/dev/zotero_10_for_developers)。
