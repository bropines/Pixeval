Pixeval 提供路径宏用于更精细的设定下载路径。
每个路径宏的形式均为 @{name}，有些有参数的宏则是 @{name=...}。

在下载图片时，这些宏会被替换为对应的文本，例如，@{id} 宏会在下载时被自动替换为作品ID。

如果一个宏带有参数，则说明是一个条件宏，该宏会在条件满足时被替换为其参数内容。
例如如果正在下载一副漫画作品，则 @{if_manga=\漫画\} 会被替换为 "\漫画\"。

要查看每个宏的作用，请将鼠标指针移动到对应宏的按钮上。