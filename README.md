# F vs Z
Second best soccer game in the world!

## 局域网对战更新

支持两台 Windows 电脑在同一局域网内对战：房主操控范志毅，加入者输入房主 IP 并操控赵鹏。联机页铺满页面，透明背景保留主菜单球员画面，复用游戏原有按钮，只显示必要操作和连接状态。

已适配上游 `280d6c1`：保留最新双方 AI、16 点加点规则、技能动画与球场背景。经典、特技、加点三种玩法均支持联机；加点模式由房主在创建房间时配置双方，开赛前同步给加入者。[适配说明和本轮验证](UPSTREAM_ADAPTATION.md)。

- [原版完整游戏包（Windows 32 位）](downloads/fzy-vs-zp-original-win32.zip)
- [模型 75% 实验版完整游戏包（Windows 32 位）](downloads/fzy-vs-zp-experimental-win32.zip)
- [使用方法、构建及验证说明](LAN_MULTIPLAYER.md)
- [游戏包 SHA-256 校验值](downloads/SHA256SUMS.txt)

## 性能维护更新

两版同步减少比赛中的重复物理查询、动画数组、组件/反射查找、GUI 样式创建和联机序列化分配；保留物理精度、技能参数、画面及两版差异。

- [实际性能对照与检查范围](PERFORMANCE.md)
- [可复现的测量和回归工具](tools/performance/README.md)

本次按要求采用短测和关键回归。短测结果不能证明持续卡顿已解决，完整数据和已存在的压力测试波动见报告。
