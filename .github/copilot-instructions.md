# Copilot Instructions

## 项目指南
- ArrayPool.Rent 租借的数组不直接用于计算，应先定义 Span<float> span = buffer.AsSpan(0, 实际长度) 并用该 span 参与后续计算（循环边界可用 span.Length 或原长度常量）
- if()之后需要添加大括号 {}，即使只有一行代码，也要加大括号