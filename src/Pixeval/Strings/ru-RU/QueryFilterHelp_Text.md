## Полное руководство основано на описании синтаксиса с примерами, определяющими все способы запросов

## Синтаксис запросов

| Синтаксис      | Значение                                                              |
| -------------- | --------------------------------------------------------------------- |
| [str]          | Заголовок                                                             |
| #[str]         | Тег                                                                   |
| @[str \| num]  | Автор                                                                 |
| +[const]       | Положительное условие                                                 |
| -[const]       | Отрицательное условие                                                 |
| i:[int-range]  | Фильтрация по индексу                                                 |
| l:[int-range]  | Фильтрация по количеству избранного                                   |
| r:[frac-range] | Фильтрация по соотношению сторон иллюстрации, не действует для новелл |
| s:[date]       | Дата начала публикации                                                |
| e:[date]       | Дата окончания публикации                                             |

## Синтаксис значений

### [str] Строка  

| Синтаксис | Значение                                                        |
| --------- | --------------------------------------------------------------- |
| abc       | Обычная строка                                                  |
| "ab# c"   | Строка с пробелами или управляющими символами                   |
| abc$      | Точное совпадение строки                                        |
| "ab c$"   | Точное совпадение строки с пробелами или управляющими символами |

### [num] Число

| Синтаксис | Значение      |
| --------- | ------------- |
| 12345     | Обычное число |

### [const] Условие

| Синтаксис | Значение                      |
| --------- | ----------------------------- |
| r18       | Содержимое R18, включая R18G  |
| r18g      | Содержимое R18G               |
| gif       | Анимация                      |
| ai        | AI-сгенерированное содержимое |

### [int-range] Диапазон целых чисел / Множество

| Синтаксис | Значение                                        |
| --------- | ----------------------------------------------- |
| 2-        | Больше или равно 2                              |
| -3        | Меньше или равно 3                              |
| 2-3       | Больше или равно 2 и меньше или равно 3         |
| [2,3]     | Математический интервал, включая 2 и 3          |
| \[2,3)    | Математический интервал, включая 2 и исключая 3 |

> Математические интервалы не поддерживают полубесконечные множества, такие как "2-" или "-3"

### [frac-range] Диапазон дробных чисел

| Синтаксис | Значение                                    |
| --------- | ------------------------------------------- |
| 2-        | Больше или равно 2                          |
| -1.5      | Меньше или равно 1.5                        |
| -1/2      | Меньше или равно 1/2                        |
| 1/2-3     | Больше или равно 1/2 и меньше или равно 3   |
| 0.3-1/2   | Больше или равно 0.3 и меньше или равно 1/2 |

### [date] Дата

| Синтаксис  | Значение                   |
| ---------- | -------------------------- |
| MM-dd      | День и месяц текущего года |
| MM.dd      | День и месяц текущего года |
| yyyy-MM-dd | День, месяц и год          |
| yyyy.MM.dd | День, месяц и год          |

> "." и "-" можно использовать взаимозаменяемо

## Последовательности

| Синтаксис                   | Значение  |
| --------------------------- | --------- |
| !\<segment>                 | Отрицание |
| (and \<segment> \<segment>) | И         |
| (or \<segment> \<segment>)  | Или       |

> Все три модели могут быть вложены любым образом, на верхнем уровне используется модель "И" по умолчанию